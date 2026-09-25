using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for deque items that round-trip across a consumer restart. A bare scalar or
/// array stored as a deque item is read back by a fresh client, so the item travels the full
/// serialize -> durable -> recover -> deserialize path rather than being served from an in-session
/// materialized cell. The "an envelope-coupled codec rejects a bare <c>42</c> or <c>[1,2,3]</c>"
/// guarantee is core-owned (the erased C# vend path fixes the cell codec to the passthrough codec via
/// <c>ErasedStateCodec</c>); see the per-test remarks.
/// </summary>
public sealed class StateDequeRecoveryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
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
            Assert.Equal(
                (int[][])
                    [
                        [1, 2, 3],
                        [4, 5],
                    ],
                got
            );
        }
        finally
        {
            await DisposeReaderAsync(reader);
        }
    }
}
