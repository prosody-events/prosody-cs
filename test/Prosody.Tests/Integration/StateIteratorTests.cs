using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Integration tests for scan-iterator lifecycle against real Kafka and Cassandra:
/// early break and mid-scan cancellation both close the cursor so a follow-up op on the same
/// collection succeeds, and an enumeration leaked past the handler is terminated. The strong
/// close-exactly-once and blocked-pull assertions live in the fake-cursor unit suite
/// (<c>StateScanSequenceTests</c>).
/// </summary>
public sealed class StateIteratorTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record IteratorObservation
    {
        public bool FollowUpOk { get; init; }
        public bool Cancelled { get; init; }
        public string? Error { get; init; }
    }

    [Fact(Timeout = 60_000)]
    public async Task EarlyBreak_LeavesCollectionUsable()
    {
        // End-to-end usability smoke: breaking out of a real scan disposes the enumerator, which
        // closes the native cursor, and a follow-up op on the same collection still succeeds. Whether
        // the close ran *exactly once* is asserted against the fake-cursor spy in the unit suite
        // (StateScanSequenceTests.EarlyBreak_ClosesExactlyOnce): the core cursor never holds a
        // per-collection gate across a yield, so a silently-skipped native close has no effect a
        // follow-up op can observe here — only the spy's close-count catches that regression.
        //
        // FALSIFICATION TARGET: make CloseOrThrowAsync in StateScanSequence.DisposeAsync throw (e.g.
        // wrap _cursor.Close() to `throw new Native.FfiException.TransientState("x")`). The dispose
        // that runs at the break surfaces a StateException out of the await foreach, the handler's
        // catch sets obs.Error, and Assert.Null(obs.Error) fails (RED).
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<IteratorObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                try
                {
                    await map.SetAsync("a", 1, ct);
                    await map.SetAsync("b", 2, ct);
                    await map.SetAsync("c", 3, ct);
                    await foreach (var entry in map.EnumerateAsync(ScanDirection.Forward, ct))
                    {
                        Assert.NotNull(entry.Key);
                        break;
                    }

                    // A follow-up op wedges (times out) if the early break failed to release the gate.
                    await map.SetAsync("after", 99, ct);
                    var after = await map.GetAsync("after", ct);
                    observations.Send(new IteratorObservation { FollowUpOk = after.ValueOr(-1) == 99 });
                }
                catch (StateException ex)
                {
                    observations.Send(new IteratorObservation { Error = ex.Message });
                }
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

        Assert.Multiple(() => Assert.Null(obs.Error), () => Assert.True(obs.FollowUpOk));
    }

    [Fact(Timeout = 60_000)]
    public async Task CancellationDuringScan_LeavesCollectionUsable()
    {
        // End-to-end usability smoke: cancelling mid-scan unwinds the await foreach (disposing the
        // enumerator, which closes the native cursor) and a follow-up op still succeeds. As with
        // EarlyBreak_LeavesCollectionUsable, close-exactly-once is a spy assertion in the unit suite
        // (StateScanSequenceTests.CancellationBeforeMoveNext_ClosesOnDispose_ExactlyOnce), because a
        // skipped native close has no follow-up-observable effect against the real cursor.
        //
        // FALSIFICATION TARGET: make CloseOrThrowAsync in StateScanSequence.DisposeAsync throw. The
        // dispose in the await foreach's finally replaces the in-flight OperationCanceledException
        // with a StateException, so obs.Error is set (and obs.Cancelled stays false) and the
        // assertions fail (RED).
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        var observations = new MessageChannel<IteratorObservation>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, _, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                try
                {
                    await map.SetAsync("a", 1, ct);
                    await map.SetAsync("b", 2, ct);
                    await map.SetAsync("c", 3, ct);

                    using var scanCts = new CancellationTokenSource();
                    var cancelled = false;
                    try
                    {
                        await foreach (var entry in map.EnumerateAsync(ScanDirection.Forward, scanCts.Token))
                        {
                            Assert.NotNull(entry.Key);
                            // Cancel mid-scan; the next chunk pull observes the token and closes the cursor.
                            await scanCts.CancelAsync();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    // A follow-up op wedges if the cancelled scan failed to close the cursor.
                    await map.SetAsync("after", 99, ct);
                    var after = await map.GetAsync("after", ct);
                    observations.Send(
                        new IteratorObservation { Cancelled = cancelled, FollowUpOk = after.ValueOr(-1) == 99 }
                    );
                }
                catch (StateException ex)
                {
                    observations.Send(new IteratorObservation { Error = ex.Message });
                }
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
            () => Assert.Null(obs.Error),
            () => Assert.True(obs.Cancelled),
            () => Assert.True(obs.FollowUpOk)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task PostHandlerEnumeration_Terminated()
    {
        // FALSIFICATION TARGET: a MoveNextAsync leaked past the opening attempt must throw
        // TransientStateException. Make StateScanSequence.MoveNextAsync swallow the terminated-cursor
        // error (or have the cursor keep yielding past teardown) and the
        // Assert.ThrowsAsync<TransientStateException> at the end goes green-when-it-should-be-red.
        await using var ctx = await CreateTestContextAsync(StateTestSupport.WithAllCollections());
        IAsyncEnumerator<KeyValuePair<string, int>>? leaked = null;
        var events = new MessageChannel<string>();

        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (context, msg, ct) =>
            {
                var map = context.State(StateTestSupport.Totals);
                if (msg.Payload?.Sequence == 1)
                {
                    await map.SetAsync("a", 1, ct);
                    // Open the cursor and capture its enumerator; the first pull is deferred to
                    // MoveNextAsync, which we invoke from the test body after the handler returns.
                    leaked = map.EnumerateAsync(ScanDirection.Forward, ct).GetAsyncEnumerator(ct);
                    events.Send("captured");
                    return;
                }

                events.Send("sentinel-started");
            }
        );

        await ctx.Client.SubscribeAsync(handler);
        // Same key: per-key serialization guarantees step 1 fully tears down before step 2 begins.
        var key = TopicGenerator.GenerateKey();
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 1 },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            "captured",
            await events.ReceiveAsync(IntegrationTestFixture.DefaultTimeout, TestContext.Current.CancellationToken)
        );
        await ctx.Client.SendAsync(
            ctx.Topic,
            key,
            new TestPayload { Sequence = 2 },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            "sentinel-started",
            await events.ReceiveAsync(IntegrationTestFixture.DefaultTimeout, TestContext.Current.CancellationToken)
        );

        await Assert.ThrowsAsync<TransientStateException>(async () => await leaked!.MoveNextAsync());
    }
}
