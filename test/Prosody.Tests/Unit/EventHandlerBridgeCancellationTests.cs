using System.Text.Json;
using Prosody.Infrastructure;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.BridgeTestSupport;
using static Prosody.Tests.TestHelpers.TestDefaults;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for the cancellation bridge: <see cref="EventHandlerBridge.BridgeCancellationAsync"/>
/// and handler cancellation through <see cref="EventHandlerBridge{TPayload}"/>.
/// </summary>
public sealed class EventHandlerBridgeCancellationTests
{
    [Fact]
    public async Task BridgeCancellationCancelsCtsWhenOnCancelCompletes()
    {
        using var cts = new CancellationTokenSource();

        // onCancel completes immediately — cancelTask is already completed when WhenAny
        // evaluates, so the cancellation branch wins deterministically without needing
        // handlerDone to complete.
#pragma warning disable CA2025 // CTS outlives the monitor: awaited before using scope ends
        var monitor = EventHandlerBridge.BridgeCancellationAsync(
            () => Task.CompletedTask,
            cts,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task
        );
#pragma warning restore CA2025

        await monitor;

        Assert.True(cts.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleCancelsTokenWhileHandlerIsRunning(bool timer)
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = false;

        var handleTask = HandleAsync(
            timer,
            async ct =>
            {
                handlerStarted.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    observedCancellation = true;
                }
            },
            () => cancelTcs.Task
        );

        await handlerStarted.Task;
        cancelTcs.TrySetResult();

        var result = await handleTask;

        Assert.Multiple(
            () => Assert.True(observedCancellation, "Handler should have observed cancellation via CancellationToken"),
            () => Assert.Equal(NativeResultCode.Success, result.Code)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleReturnsTransientErrorWhenHandlerPropagatesCancellation(bool timer)
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The handler lets the OperationCanceledException propagate. The work is incomplete, so the
        // bridge classifies the error transient and Prosody delivers the event again.
        var handleTask = HandleAsync(
            timer,
            async ct =>
            {
                handlerStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            },
            () => cancelTcs.Task
        );

        await handlerStarted.Task;
        cancelTcs.TrySetResult();

        var result = await handleTask;

        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }

    [Fact]
    public async Task BridgeCancellationDoesNotCancelCtsWhenHandlerCompletesFirst()
    {
        using var cts = new CancellationTokenSource();
        var handlerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // onCancel never completes
#pragma warning disable CA2025 // CTS outlives the monitor: awaited before using scope ends
        var monitor = EventHandlerBridge.BridgeCancellationAsync(NeverCancel, cts, handlerDone.Task);
#pragma warning restore CA2025

        handlerDone.TrySetResult();
        await monitor;

        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task BridgeCancellationSwallowsSynchronousOnCancelFault()
    {
        using var cts = new CancellationTokenSource();
        var handlerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // onCancel throws synchronously — simulates native context torn down
#pragma warning disable CA2025 // CTS outlives the monitor: awaited before using scope ends
        var monitor = EventHandlerBridge.BridgeCancellationAsync(
            () => throw new InvalidOperationException("native context destroyed"),
            cts,
            handlerDone.Task
        );
#pragma warning restore CA2025

        handlerDone.TrySetResult();

        // Must not throw
        await monitor;

        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task BridgeCancellationSwallowsLateFaultWhenHandlerCompletesFirst()
    {
        using var cts = new CancellationTokenSource();
        var handlerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // onCancel returns a task that will fault after the handler completes
#pragma warning disable CA2025 // CTS outlives the monitor: awaited before using scope ends
        var monitor = EventHandlerBridge.BridgeCancellationAsync(() => cancelTcs.Task, cts, handlerDone.Task);
#pragma warning restore CA2025

        handlerDone.TrySetResult();
        await monitor;

        // Now fault the cancel task — should not trigger UnobservedTaskException
        cancelTcs.TrySetException(new InvalidOperationException("late native fault"));

        // Force GC + finalizers to flush any unobserved task exceptions
        var unobservedFaulted = false;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobservedFaulted = true;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(unobservedFaulted);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.False(cts.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleCompletesWhenOnCancelFaults(bool timer)
    {
        var handlerCalled = false;

        var result = await HandleAsync(
            timer,
            _ =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            },
            () => throw new InvalidOperationException("context torn down")
        );

        Assert.Multiple(() => Assert.True(handlerCalled), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }

    /// <summary>Runs <paramref name="body"/> as the message or timer handler of a new bridge.</summary>
    private static Task<Native.HandlerResult> HandleAsync(
        bool timer,
        Func<CancellationToken, Task> body,
        Func<Task> onCancel
    )
    {
        var handler = timer
            ? new LambdaHandler<JsonElement>(onTimer: (_, _, ct) => body(ct))
            : new LambdaHandler<JsonElement>(onMessage: (_, _, ct) => body(ct));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);
        return timer ? HandleTimerAsync(bridge, onCancel) : HandleMessageAsync(bridge, onCancel: onCancel);
    }
}
