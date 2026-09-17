using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Prosody.Messaging;
using Prosody.Native;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Integration;

/// <summary>
/// Verifies that per-event native resources are released once handlers complete.
/// Every handler invocation bridges <c>Context.OnCancel()</c> into a pending Rust
/// future whose continuation is tracked in <c>_UniFFIAsync._async_handle_map</c>;
/// those entries must not accumulate for the lifetime of the consumer.
/// </summary>
/// <remarks>
/// Runs in the sequential collection because it observes process-wide FFI state
/// that concurrent tests would pollute.
/// </remarks>
[Collection("Sequential")]
public sealed class CancellationBridgeLeakTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    // .NET 8 does not support UnsafeAccessor for generic types.
    private static readonly FieldInfo AsyncHandleMapField =
        typeof(ConcurrentHandleMap<TaskCompletionSource<byte>>).GetField(
            "_map",
            BindingFlags.Instance | BindingFlags.NonPublic
        ) ?? throw new InvalidOperationException("The generated async handle map field does not exist.");

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    [Fact(Timeout = 120_000)]
    public async Task ReleasesOnCancelFuturesAfterHandlersComplete()
    {
        const int messageCount = 10;

        await using var ctx = await CreateTestContextAsync();

        var messages = new MessageChannel<Message<TestPayload>>();
        var firstHandlerStarted = new EventNotifier();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TestProsodyHandler<TestPayload>(
            onMessage: async (_, msg, _) =>
            {
                firstHandlerStarted.Signal();
                await gate.Task;
                messages.Send(msg);
            }
        );

        await ctx.Client.SubscribeAsync(handler);

        // The consumer may still be registering long-lived async FFI calls right
        // after SubscribeAsync returns (initial rebalance, poll futures). Wait for
        // the pending-call count to settle so the baseline reflects steady state
        // rather than a transiently low snapshot.
        var baseline = await WaitForStablePendingAsyncCallCountAsync(TimeSpan.FromSeconds(5));

        var midFlight = 0;
        try
        {
            for (var i = 0; i < messageCount; i++)
            {
                await ctx.Client.SendAsync(
                    ctx.Topic,
                    $"key-{i}",
                    new TestPayload { Content = $"Message {i}", Sequence = i },
                    TestContext.Current.CancellationToken
                );
            }

            // Sanity-check the instrumentation: while a handler is in flight, its
            // Context.OnCancel() bridge must be visible as a pending continuation.
            await firstHandlerStarted.WaitAsync(TestContext.Current.CancellationToken);
            midFlight = PendingAsyncCallCount();
        }
        finally
        {
            // Ensure disposal cannot wait forever for a handler if a later send or
            // the in-flight wait fails before the normal release point.
            gate.TrySetResult();
        }

        await messages.ReceiveAsync(
            messageCount,
            IntegrationTestFixture.DefaultTimeout,
            TestContext.Current.CancellationToken
        );

        Assert.True(
            midFlight > baseline,
            "Instrumentation check failed: expected pending continuations while a handler "
                + $"is in flight (baseline={baseline}, midFlight={midFlight}). The measurement "
                + "point no longer observes Context.OnCancel() bridge futures."
        );

        // All handlers have completed. Every async FFI call made on their behalf —
        // including the Context.OnCancel() bridge started for each event — should
        // finish promptly and release its continuation entry.
        var final = await WaitForPendingAsyncCallsAsync(baseline, TimeSpan.FromSeconds(10));

        Assert.True(
            final <= baseline,
            "Pending async FFI continuations did not return to baseline after all handlers "
                + $"completed: baseline={baseline}, final={final}. Each handled event leaves one "
                + "abandoned Context.OnCancel() future that persists until consumer shutdown or "
                + "rebalance."
        );
    }

    private static int PendingAsyncCallCount()
    {
        var entries = AsyncHandleMapField.GetValue(_UniFFIAsync._async_handle_map);
        return Assert.IsType<ConcurrentDictionary<ulong, TaskCompletionSource<byte>>>(entries).Count;
    }

    // The two waits below keep separate loops on purpose. One waits for a count that
    // stops moving; the other waits for a count to fall to a target. A shared helper
    // needs a stateful predicate, which reads worse than either plain loop.
    private static async Task<int> WaitForStablePendingAsyncCallCountAsync(TimeSpan timeout)
    {
        const int requiredStableTicks = 3;
        using var timer = new PeriodicTimer(PollInterval);
        var start = Stopwatch.GetTimestamp();
        var count = PendingAsyncCallCount();
        var stableTicks = 0;
        while (
            stableTicks < requiredStableTicks
            && Stopwatch.GetElapsedTime(start) < timeout
            && await timer.WaitForNextTickAsync(TestContext.Current.CancellationToken)
        )
        {
            var next = PendingAsyncCallCount();
            stableTicks = next == count ? stableTicks + 1 : 0;
            count = next;
        }

        Assert.True(
            stableTicks >= requiredStableTicks,
            $"Pending async call count did not stabilize within {timeout}: last count={count}, "
                + $"stableTicks={stableTicks}/{requiredStableTicks}."
        );

        return count;
    }

    private static async Task<int> WaitForPendingAsyncCallsAsync(int target, TimeSpan timeout)
    {
        using var timer = new PeriodicTimer(PollInterval);
        var start = Stopwatch.GetTimestamp();
        var count = PendingAsyncCallCount();
        while (
            count > target
            && Stopwatch.GetElapsedTime(start) < timeout
            && await timer.WaitForNextTickAsync(TestContext.Current.CancellationToken)
        )
        {
            count = PendingAsyncCallCount();
        }
        return count;
    }
}
