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

    [Fact]
    public async Task HandleMessageCancelsTokenWhileHandlerIsRunning()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = false;

        var handler = new LambdaHandler<JsonElement>(
            onMessage: async (_, _, ct) =>
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
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var handleTask = HandleMessageAsync(bridge, onCancel: () => cancelTcs.Task);

        await handlerStarted.Task;
        cancelTcs.TrySetResult();

        var result = await handleTask;

        Assert.Multiple(
            () => Assert.True(observedCancellation, "Handler should have observed cancellation via CancellationToken"),
            () => Assert.Equal(NativeResultCode.Success, result.Code)
        );
    }

    [Fact]
    public async Task HandleTimerCancelsTokenWhileHandlerIsRunning()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = false;

        var handler = new LambdaHandler<JsonElement>(
            onTimer: async (_, _, ct) =>
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
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var handleTask = HandleTimerAsync(bridge, onCancel: () => cancelTcs.Task);

        await handlerStarted.Task;
        cancelTcs.TrySetResult();

        var result = await handleTask;

        Assert.Multiple(
            () => Assert.True(observedCancellation, "Handler should have observed cancellation via CancellationToken"),
            () => Assert.Equal(NativeResultCode.Success, result.Code)
        );
    }

    [Fact]
    public async Task HandleMessageReturnsTransientErrorWhenHandlerPropagatesCancellation()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new LambdaHandler<JsonElement>(
            onMessage: async (_, _, ct) =>
            {
                handlerStarted.TrySetResult();

                // Let the OperationCanceledException propagate — simulates a handler that does not
                // catch cancellation. This is classified as transient because the work is incomplete
                // and Prosody should redeliver the message.
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var handleTask = HandleMessageAsync(bridge, onCancel: () => cancelTcs.Task);

        await handlerStarted.Task;
        cancelTcs.TrySetResult();

        var result = await handleTask;

        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }

    [Fact]
    public async Task HandleTimerReturnsTransientErrorWhenHandlerPropagatesCancellation()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new LambdaHandler<JsonElement>(
            onTimer: async (_, _, ct) =>
            {
                handlerStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var handleTask = HandleTimerAsync(bridge, onCancel: () => cancelTcs.Task);

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

    [Fact]
    public async Task HandleMessageCompletesWhenOnCancelFaults()
    {
        var handlerCalled = false;
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(
            bridge,
            onCancel: () => throw new InvalidOperationException("context torn down")
        );

        Assert.Multiple(() => Assert.True(handlerCalled), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }

    [Fact]
    public async Task HandleTimerCompletesWhenOnCancelFaults()
    {
        var handlerCalled = false;
        var handler = new LambdaHandler<JsonElement>(
            onTimer: (_, _, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleTimerAsync(
            bridge,
            onCancel: () => throw new InvalidOperationException("context torn down")
        );

        Assert.Multiple(() => Assert.True(handlerCalled), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }
}
