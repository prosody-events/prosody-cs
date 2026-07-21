using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for the ordered-map keyed-state collection against real Kafka and Cassandra:
/// forward/backward scan order, remove, unicode keys, and positional
/// <c>GetManyAsync</c>. A write event and a read event on the same key exercise cross-invocation
/// visibility.
/// </summary>
public sealed class StateMapCollectionTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record MapObservation
    {
        public string[] Keys { get; init; } = [];
        public string[] BackwardKeys { get; init; } = [];
        public bool RemovedKeyPresent { get; init; }
        public bool[] Presence { get; init; } = [];
        public int[] Values { get; init; } = [];
    }

    private static TestProsodyHandler<TestPayload> SeedThenObserve(
        Func<IMapState<int>, CancellationToken, Task> observe
    ) =>
        new(
            onMessage: async (context, msg, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                if (msg.Payload?.Sequence == 1)
                {
                    await map.SetAsync("k1", 1, ct);
                    await map.SetAsync("k2", 2, ct);
                    await map.SetAsync("k3", 3, ct);
                    await map.RemoveAsync("k2", ct);
                    return;
                }

                await observe(map, ct);
            }
        );

    private static async Task RunSeededAsync(IntegrationTestContext ctx, TestProsodyHandler<TestPayload> handler)
    {
        await ctx.Client.SubscribeAsync(handler);
        var key = TopicGenerator.GenerateKey();
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 2 },
            TestContext.Current.CancellationToken
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_SetRemove_ForwardScan_KeyOrder()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MapObservation>();

        var handler = SeedThenObserve(
            async (map, ct) =>
            {
                var keys = new List<string>();
                await foreach (var entry in map.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    keys.Add(entry.Key);
                }

                observations.Send(new MapObservation { Keys = [.. keys] });
            }
        );

        await RunSeededAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(["k1", "k3"], obs.Keys);
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_BackwardScan_Reverses()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MapObservation>();

        var handler = SeedThenObserve(
            async (map, ct) =>
            {
                var keys = new List<string>();
                await foreach (var entry in map.EnumerateAsync(ScanDirection.Backward, ct))
                {
                    keys.Add(entry.Key);
                }

                observations.Send(new MapObservation { Keys = [.. keys] });
            }
        );

        await RunSeededAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(["k3", "k1"], obs.Keys);
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_GetRemovedKey_ReturnsNone()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MapObservation>();

        var handler = SeedThenObserve(
            async (map, ct) =>
            {
                var got = await map.GetAsync("k2", ct);
                observations.Send(new MapObservation { RemovedKeyPresent = got.HasValue });
            }
        );

        await RunSeededAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.False(obs.RemovedKeyPresent);
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_UnicodeKeys_RoundTrip()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MapObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                if (msg.Payload?.Sequence == 1)
                {
                    await map.SetAsync("k1", 1, ct);
                    await map.SetAsync("café", 9, ct);
                    await map.SetAsync("😀", 7, ct);
                    return;
                }

                var keys = new List<string>();
                var values = new List<int>();
                await foreach (var entry in map.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    keys.Add(entry.Key);
                    values.Add(entry.Value);
                }

                observations.Send(new MapObservation { Keys = [.. keys], Values = [.. values] });
            }
        );

        await RunSeededAsync(ctx, handler);

        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        var byKey = obs.Keys.Zip(obs.Values).ToDictionary(pair => pair.First, pair => pair.Second);
        Assert.Multiple(
            () => Assert.Equal(3, byKey.Count),
            () => Assert.Equal(1, byKey["k1"]),
            () => Assert.Equal(9, byKey["café"]),
            () => Assert.Equal(7, byKey["😀"])
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_ContainsKeyAsync_TracksOverlay()
    {
        // Presence follows the seeded overlay: absent → false, set → true, removed → false, and (in the
        // read event) a set-after-clear → true while the cleared siblings read false. Pins the cheap
        // presence path against the same seed the scan tests use.
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<bool[]>();

        var handler = SeedThenObserve(
            async (map, ct) =>
            {
                await map.ClearAsync(ct);
                await map.SetAsync("k1", 10, ct);
                observations.Send([
                    await map.ContainsKeyAsync("k1", ct), // set-after-clear → true
                    await map.ContainsKeyAsync("k3", ct), // cleared, not re-set → false
                    await map.ContainsKeyAsync("absent", ct), // never written → false
                ]);
            }
        );

        await RunSeededAsync(ctx, handler);
        var presence = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Equal([true, false, false], presence);
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_EnumerateKeysAsync_MatchesPairScan_BothDirections()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MapObservation>();

        var handler = SeedThenObserve(
            async (map, ct) =>
            {
                var forward = new List<string>();
                await foreach (var key in map.EnumerateKeysAsync(ScanDirection.Forward, ct))
                {
                    forward.Add(key);
                }

                var backward = new List<string>();
                await foreach (var key in map.EnumerateKeysAsync(ScanDirection.Backward, ct))
                {
                    backward.Add(key);
                }

                observations.Send(new MapObservation { Keys = [.. forward], BackwardKeys = [.. backward] });
            }
        );

        await RunSeededAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(["k1", "k3"], obs.Keys), // key-only scan == pair-scan key set (k2 removed)
            () => Assert.Equal(["k3", "k1"], obs.BackwardKeys)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Map_GetManyAsync_Positional_AbsentHasValueFalse()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<MapObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                if (msg.Payload?.Sequence == 1)
                {
                    await map.SetAsync("k1", 1, ct);
                    await map.SetAsync("k3", 3, ct);
                    return;
                }

                var results = await map.GetManyAsync(["k1", "k2", "k3", "k1"], ct);
                observations.Send(
                    new MapObservation
                    {
                        Presence = [.. results.Select(r => r.HasValue)],
                        Values = [.. results.Select(r => r.GetValueOrDefault(-1))],
                    }
                );
            }
        );

        await RunSeededAsync(ctx, handler);

        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal([true, false, true, true], obs.Presence),
            () => Assert.Equal(1, obs.Values[0]),
            () => Assert.False(obs.Presence[1]),
            () => Assert.Equal(3, obs.Values[2]),
            () => Assert.Equal(obs.Values[0], obs.Values[3])
        );
    }
}
