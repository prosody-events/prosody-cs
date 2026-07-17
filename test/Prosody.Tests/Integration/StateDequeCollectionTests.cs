using Prosody;
using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for the deque keyed-state collection against real Kafka and Cassandra
/// (appendix-1 item 3): push front/back, index/count/empty, pop front/back, scan both directions, and
/// empty-deque behavior. A write event and a read event on the same key exercise cross-invocation
/// visibility. The scalar/array round-trip pins (appendix-1 codec honesty) also live here: a bare
/// scalar/array stored as a deque item and read back by a fresh client after a consumer restart, so
/// the item travels the full serialize -> durable -> recover -> deserialize path rather than being
/// served from an in-session materialized cell. The "an envelope-coupled codec rejects a bare
/// <c>42</c> or <c>[1,2,3]</c>" guarantee is core-owned (the erased C# vend path fixes the cell codec
/// to the passthrough codec via <c>ErasedStateCodec</c>); see the per-test remarks.
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
                        Get0 = get0.ValueOr(""),
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
                        PopFront = front.ValueOr(""),
                        PopBackHasValue = back.HasValue,
                        PopBack = back.ValueOr(""),
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

    /// <summary>
    /// Drives the write half of the two-phase cold-recovery codec pin: run 1 (<paramref name="write"/>)
    /// pushes and commits items on <paramref name="key"/>, then the writer unsubscribes; a fresh
    /// sibling client (run 2, <paramref name="read"/>) — whose empty in-memory state forces a cold
    /// decode of the durable cells through the registered state codec — reads them back. A same-session
    /// read is served from a materialized cell layer that never re-decodes, so only this restart
    /// exercises the codec's decode. The returned client is the caller's to dispose after asserting.
    /// </summary>
    private static async Task<ProsodyClient> StartColdRecoveryReaderAsync(
        IntegrationTestContext ctx,
        string key,
        Func<ProsodyContext, CancellationToken, Task> write,
        Func<ProsodyContext, CancellationToken, Task> read
    )
    {
        var written = new MessageChannel<string>();
        var writer = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                await write(context, ct);
                written.Send("written");
            }
        );

        await ctx.Client.SubscribeAsync(writer);
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            "written",
            await written.ReceiveAsync(IntegrationTestFixture.DefaultTimeout, TestContext.Current.CancellationToken)
        );
        await ctx.Client.UnsubscribeAsync();

        var reader = await ctx.CreateSiblingClientAsync(StateTestSupport.WithAllCollections());
        var readerHandler = new TestProsodyHandler<TestPayload>(onMessage: (context, _, ct) => read(context, ct));
        await reader.SubscribeAsync(readerHandler);
        await reader.SendAsync(ctx.Topic, key, new TestPayload { Sequence = 2 }, TestContext.Current.CancellationToken);
        return reader;
    }

    private static async Task DisposeReaderAsync(ProsodyClient reader)
    {
        if (await reader.GetConsumerStateAsync() == ConsumerState.Running)
        {
            await reader.UnsubscribeAsync();
        }

        await reader.DisposeAsync();
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_TopLevelScalarItems_RoundTripViaColdRecovery()
    {
        // Bare scalar JSON documents stored as deque items must round-trip across a consumer restart:
        // run 2 is a fresh client whose empty in-memory state forces the items to be read back from
        // the durable store and re-deserialized by the client, rather than served from run 1's
        // in-session materialized cells. This pins that the client's full serialize -> durable ->
        // recover -> deserialize path carries a non-object document.
        //
        // The "an envelope-coupled codec would reject a bare 42" guarantee is CORE-owned and cannot be
        // reached from this client: the erased C# vend path always resolves the cell codec from the
        // payload type (<BinaryPayload as ErasedStateCodec>::Codec = JsonPassthroughStateCodec,
        // verified in prosody core src/consumer/event_context/erased.rs and src/codec/mod.rs),
        // independent of the ffi/src/config.rs registration, and core pins the non-object rejection
        // (codec/binary json_id_non_object_propagates_error). Swapping the config.rs codec arm is
        // therefore inert here.
        //
        // FALSIFICATION TARGET (client-observable): make DequeState<T>.Transform return `default!`
        // (or any wrong value) instead of deserializing the scan item. Run 2's cold scan then yields
        // the wrong items and Assert.Equal([42, 7, 13], got) fails (RED).
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var readBack = new MessageChannel<int[]>();

        var reader = await StartColdRecoveryReaderAsync(
            ctx,
            TopicGenerator.GenerateKey(),
            write: async (context, ct) =>
            {
                var scalars = context.State(StateTestSupport.ScalarDeque);
                await scalars.PushBackAsync(42, ct);
                await scalars.PushBackAsync(7, ct);
                await scalars.PushBackAsync(13, ct);
                await scalars.CommitAsync(ct);
            },
            read: async (context, ct) =>
            {
                var scalars = context.State(StateTestSupport.ScalarDeque);
                var items = new List<int>();
                await foreach (var item in scalars.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    items.Add(item);
                }

                readBack.Send([.. items]);
            }
        );

        try
        {
            var got = await readBack.ReceiveAsync(
                IntegrationTestFixture.DefaultTimeout,
                TestContext.Current.CancellationToken
            );
            Assert.Equal([42, 7, 13], got);
        }
        finally
        {
            await DisposeReaderAsync(reader);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Deque_TopLevelArrayItems_RoundTripViaColdRecovery()
    {
        // Bare array documents across a consumer restart; same cold-recovery rationale, core-owned
        // codec note, and client-observable FALSIFICATION TARGET (corrupt DequeState<T>.Transform)
        // as Deque_TopLevelScalarItems_RoundTripViaColdRecovery.
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var readBack = new MessageChannel<int[][]>();

        var reader = await StartColdRecoveryReaderAsync(
            ctx,
            TopicGenerator.GenerateKey(),
            write: async (context, ct) =>
            {
                var arrays = context.State(StateTestSupport.ArrayDeque);
                await arrays.PushBackAsync([1, 2, 3], ct);
                await arrays.PushBackAsync([4, 5], ct);
                await arrays.CommitAsync(ct);
            },
            read: async (context, ct) =>
            {
                var arrays = context.State(StateTestSupport.ArrayDeque);
                var items = new List<int[]>();
                await foreach (var item in arrays.EnumerateAsync(ScanDirection.Forward, ct))
                {
                    items.Add(item);
                }

                readBack.Send([.. items]);
            }
        );

        try
        {
            var got = await readBack.ReceiveAsync(
                IntegrationTestFixture.DefaultTimeout,
                TestContext.Current.CancellationToken
            );
            Assert.Multiple(() => Assert.Equal([1, 2, 3], got[0]), () => Assert.Equal([4, 5], got[1]));
        }
        finally
        {
            await DisposeReaderAsync(reader);
        }
    }
}
