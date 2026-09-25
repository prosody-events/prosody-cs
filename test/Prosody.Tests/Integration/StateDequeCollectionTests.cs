using Prosody;
using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for the deque keyed-state collection against real Kafka and Cassandra:
/// push front/back, index/count/empty, pop front/back, scan both directions, and
/// empty-deque behavior. A write event and a read event on the same key exercise cross-invocation
/// visibility. <see cref="StateDequeRecoveryTests"/> covers the round trip across a consumer restart.
/// </summary>
public sealed class StateDequeCollectionTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record DequeObservation
    {
        public long Count { get; init; }
        public bool IsEmpty { get; init; }
        public bool Get0HasValue { get; init; }
        public string Get0 { get; init; } = "";
        public bool PopFrontHasValue { get; init; }
        public string PopFront { get; init; } = "";
        public bool PopBackHasValue { get; init; }
        public string PopBack { get; init; } = "";
        public string[] Forward { get; init; } = [];
        public string[] Backward { get; init; } = [];
        public bool PeekFrontHasValue { get; init; }
        public string PeekFront { get; init; } = "";
        public bool PeekBackHasValue { get; init; }
        public string PeekBack { get; init; } = "";
    }

    private static async Task SeedAndSendAsync(IntegrationTestContext ctx, TestProsodyHandler<TestPayload> handler)
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
    public async Task Deque_PushBackFront_LenGetZero()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    await deque.PushBackAsync("b", ct);
                    await deque.PushFrontAsync("z", ct);
                    return;
                }

                var get0 = await deque.GetAsync(0, ct);
                observations.Send(
                    new DequeObservation
                    {
                        Count = await deque.CountAsync(ct),
                        IsEmpty = await deque.IsEmptyAsync(ct),
                        Get0HasValue = get0.HasValue,
                        Get0 = get0.GetValueOrDefault(""),
                    }
                );
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(3, obs.Count),
            () => Assert.False(obs.IsEmpty),
            () => Assert.True(obs.Get0HasValue),
            () => Assert.Equal("z", obs.Get0)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_PopFrontBack()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    await deque.PushBackAsync("b", ct);
                    await deque.PushFrontAsync("z", ct);
                    return;
                }

                var front = await deque.PopFrontAsync(ct);
                var back = await deque.PopBackAsync(ct);
                observations.Send(
                    new DequeObservation
                    {
                        PopFrontHasValue = front.HasValue,
                        PopFront = front.GetValueOrDefault(""),
                        PopBackHasValue = back.HasValue,
                        PopBack = back.GetValueOrDefault(""),
                    }
                );
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(obs.PopFrontHasValue),
            () => Assert.Equal("z", obs.PopFront),
            () => Assert.True(obs.PopBackHasValue),
            () => Assert.Equal("b", obs.PopBack)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_ScanBothDirections()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    await deque.PushBackAsync("b", ct);
                    await deque.PushFrontAsync("z", ct);
                    return;
                }

                var forward = new List<string>();
                await foreach (var item in deque.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    forward.Add(item);
                }

                var backward = new List<string>();
                await foreach (var item in deque.EnumerateAsync(ScanDirection.Backward, ct))
                {
                    backward.Add(item);
                }

                observations.Send(new DequeObservation { Forward = [.. forward], Backward = [.. backward] });
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(["z", "a", "b"], obs.Forward),
            () => Assert.Equal(["b", "a", "z"], obs.Backward)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_PeekFrontBack_EqualEndpointGets_NoConsume()
    {
        // Peeks are endpoint-slot reads: PeekFront == Get(0), PeekBack == Get(len-1), and neither
        // consumes — a follow-up Count still sees every element.
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    await deque.PushBackAsync("b", ct);
                    await deque.PushFrontAsync("z", ct);
                    return;
                }

                var front = await deque.PeekFrontAsync(ct);
                var back = await deque.PeekBackAsync(ct);
                var get0 = await deque.GetAsync(0, ct);
                var getLast = await deque.GetAsync((int)await deque.CountAsync(ct) - 1, ct);
                observations.Send(
                    new DequeObservation
                    {
                        PeekFrontHasValue = front.HasValue,
                        PeekFront = front.GetValueOrDefault(""),
                        PeekBackHasValue = back.HasValue,
                        PeekBack = back.GetValueOrDefault(""),
                        Get0 = get0.GetValueOrDefault(""),
                        // Count read after the peeks proves the peeks did not consume.
                        Count = await deque.CountAsync(ct),
                        PopFront = getLast.GetValueOrDefault(""),
                    }
                );
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.True(obs.PeekFrontHasValue),
            () => Assert.Equal("z", obs.PeekFront), // == Get(0)
            () => Assert.Equal(obs.Get0, obs.PeekFront),
            () => Assert.True(obs.PeekBackHasValue),
            () => Assert.Equal("b", obs.PeekBack), // == Get(len-1)
            () => Assert.Equal(obs.PopFront, obs.PeekBack),
            () => Assert.Equal(3, obs.Count) // peeks did not consume
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_EmptyPeeks_ReturnAbsent_NoThrow()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                var front = await deque.PeekFrontAsync(ct);
                var back = await deque.PeekBackAsync(ct);
                observations.Send(
                    new DequeObservation { PeekFrontHasValue = front.HasValue, PeekBackHasValue = back.HasValue }
                );
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload(),
            TestContext.Current.CancellationToken
        );

        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(() => Assert.False(obs.PeekFrontHasValue), () => Assert.False(obs.PeekBackHasValue));
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_Capacity_EvictsOldestOnPush()
    {
        // A capacity-3 deque pushed 5 elements back keeps only the newest 3; the front (oldest) is
        // evicted lazily on each over-capacity push. Pins that the C# config threads capacity into
        // core and that eviction is observable client-side. Core owns the eviction property tests.
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<int[]>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.BoundedDeque);
                if (msg.Payload?.Sequence == 1)
                {
                    for (var i = 1; i <= 5; i++)
                    {
                        await deque.PushBackAsync(i, ct);
                    }

                    return;
                }

                var window = new List<int>();
                await foreach (var item in deque.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    window.Add(item);
                }

                observations.Send([.. window]);
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var window = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Equal([3, 4, 5], window);
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_Empty_LenZero_IsEmpty_PopsNone()
    {
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<DequeObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                var front = await deque.PopFrontAsync(ct);
                var back = await deque.PopBackAsync(ct);
                observations.Send(
                    new DequeObservation
                    {
                        Count = await deque.CountAsync(ct),
                        IsEmpty = await deque.IsEmptyAsync(ct),
                        PopFrontHasValue = front.HasValue,
                        PopBackHasValue = back.HasValue,
                    }
                );
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        await ctx.Client.SendAsync(
            ctx.Topic,
            TopicGenerator.GenerateKey(),
            new TestPayload(),
            TestContext.Current.CancellationToken
        );

        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(0, obs.Count),
            () => Assert.True(obs.IsEmpty),
            () => Assert.False(obs.PopFrontHasValue),
            () => Assert.False(obs.PopBackHasValue)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_GetNegativeIndex_ThrowsTransient()
    {
        // A negative index is a caller mistake: it must classify TRANSIENT (recovered from the
        // exception TYPE, not the message) so a data-dependent handler bug retries rather than
        // committing the offset and silently losing the message. Asserting the exact "transient"
        // discriminant also pins that it is NOT misclassified permanent.
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<string>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var deque = context.State(StateTestSupport.Backlog);
                if (msg.Payload?.Sequence == 1)
                {
                    await deque.PushBackAsync("a", ct);
                    return;
                }

                try
                {
                    await deque.GetAsync(-1, ct);
                    observations.Send("no-throw");
                }
                catch (PermanentStateException)
                {
                    observations.Send("permanent");
                }
                catch (TransientStateException)
                {
                    observations.Send("transient");
                }
            }
        );

        await SeedAndSendAsync(ctx, handler);
        var obs = await observations.ReceiveAsync(
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("transient", obs);
    }
}
