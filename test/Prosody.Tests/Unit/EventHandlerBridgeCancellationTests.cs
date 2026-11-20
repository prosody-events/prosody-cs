using System.Text.Json;
using Prosody.Infrastructure;
using Prosody.Messaging;
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
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("message", false, false)]
    [InlineData("excise", false, false)]
    [InlineData("timer", false, false)]
    [InlineData("message", false, true)]
    [InlineData("excise", false, true)]
    [InlineData("timer", false, true)]
    [InlineData("message", true, false)]
    [InlineData("excise", true, false)]
    [InlineData("timer", true, false)]
    [InlineData("message", true, true)]
    [InlineData("excise", true, true)]
    [InlineData("timer", true, true)]
    public async Task PreCancelledDeliveryDoesNotInvokeHandler(
        string eventType,
        bool nativeSignalPending,
        bool allErrorsPermanent
    )
    {
        var handlerCalled = false;
        var onCancelCalls = 0;
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!nativeSignalPending)
            cancelTcs.SetResult();

        var result = await HandleAsync(
                eventType,
                _ =>
                {
                    // This callback has a synchronous side effect and does not inspect the token.
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                () =>
                {
                    onCancelCalls++;
                    return cancelTcs.Task;
                },
                allErrorsPermanent,
                shouldCancel: () => nativeSignalPending
            )
            .WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.False(handlerCalled),
            () => Assert.Equal(1, onCancelCalls),
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.NotNull(result.ErrorMessage),
            () => Assert.Equal(EventHandlerBridge.JsonNull, result.Response)
        );
    }

    [Theory]
    [InlineData("message", false)]
    [InlineData("excise", false)]
    [InlineData("timer", false)]
    [InlineData("message", true)]
    [InlineData("excise", true)]
    [InlineData("timer", true)]
    public async Task SynchronousHandlerCompletionStopsCancellationMonitor(string eventType, bool throws)
    {
        var handlerCalled = false;

        var result = await HandleAsync(
                eventType,
                _ =>
                {
                    handlerCalled = true;
                    return throws ? throw new InvalidOperationException("handler failure") : Task.CompletedTask;
                },
                NeverCancel,
                shouldCancel: static () => false
            )
            .WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.True(handlerCalled),
            () => Assert.Equal(throws ? NativeResultCode.TransientError : NativeResultCode.Success, result.Code)
        );
    }

    [Fact]
    public void CancellationDuringNativeCheckIsObserved()
    {
        using var cts = new CancellationTokenSource();

        var cancelled = EventHandlerBridge.IsCancellationRequested(
            () =>
            {
                cts.Cancel();
                return false;
            },
            cts.Token
        );

        Assert.True(cancelled);
    }

    [Fact]
    public async Task InvocationWaitsForCancellationMonitorBeforeItReturns()
    {
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callbackStarted = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        CancellationTokenRegistration registration = default;
        var callbackReleased = false;
        var tokenCancelled = false;

        var handleTask = EventHandlerBridge.InvokeHandlerAsync(
            ct =>
            {
                registration = ct.Register(() =>
                {
                    callbackStarted.Set();
                    callbackReleased = releaseCallback.Wait(TestTimeout, TestContext.Current.CancellationToken);
                });
                cancelTcs.SetResult();

                // Keep this handler synchronous. Wait until cancellation callbacks start before it returns.
                tokenCancelled =
                    callbackStarted.Wait(TestTimeout, TestContext.Current.CancellationToken)
                    && ct.IsCancellationRequested;
                return Task.FromResult(EventHandlerBridge.JsonNull);
            },
            _ => false,
            () => cancelTcs.Task,
            EmptyCarrier,
            EventHandlerBridge.OnMessageActivityName
        );

        Native.HandlerResult result;
        try
        {
            Assert.True(tokenCancelled);
            Assert.False(handleTask.IsCompleted);
        }
        finally
        {
            releaseCallback.Set();
            result = await handleTask.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
            await registration.DisposeAsync();
        }

        Assert.Multiple(() => Assert.True(callbackReleased), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }

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
    [InlineData("message")]
    [InlineData("excise")]
    [InlineData("timer")]
    public async Task HandleCancelsTokenWhileHandlerIsRunning(string eventType)
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = false;

        var handleTask = HandleAsync(
            eventType,
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

        await handlerStarted.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        cancelTcs.TrySetResult();

        var result = await handleTask.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.True(observedCancellation, "Handler should have observed cancellation via CancellationToken"),
            () => Assert.Equal(NativeResultCode.Success, result.Code)
        );
    }

    [Theory]
    [InlineData("message")]
    [InlineData("excise")]
    [InlineData("timer")]
    public async Task HandleReturnsTransientErrorWhenHandlerPropagatesCancellation(string eventType)
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The handler lets the OperationCanceledException propagate. The work is incomplete, so the
        // bridge classifies the error transient and Prosody delivers the event again.
        var handleTask = HandleAsync(
            eventType,
            async ct =>
            {
                handlerStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            },
            () => cancelTcs.Task
        );

        await handlerStarted.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        cancelTcs.TrySetResult();

        var result = await handleTask.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

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
    [InlineData("message")]
    [InlineData("excise")]
    [InlineData("timer")]
    public async Task HandleCompletesWhenOnCancelFaults(string eventType)
    {
        var handlerCalled = false;

        var result = await HandleAsync(
            eventType,
            _ =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            },
            () => throw new InvalidOperationException("context torn down")
        );

        Assert.Multiple(() => Assert.True(handlerCalled), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }

    /// <summary>Runs <paramref name="body"/> as a handler of a new bridge.</summary>
    private static Task<Native.HandlerResult> HandleAsync(
        string eventType,
        Func<CancellationToken, Task> body,
        Func<Task> onCancel,
        bool allErrorsPermanent = false,
        Func<bool>? shouldCancel = null
    )
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, ct) => body(ct),
            onTimer: (_, _, ct) => body(ct),
            onExcise: (_, _, ct) => body(ct)
        );
        var bridge = allErrorsPermanent
            ? new EventHandlerBridge<JsonElement>(handler, TestJson.Options, new AllErrorsPermanentClassifier())
            : new EventHandlerBridge<JsonElement>(handler, TestJson.Options);
        return eventType switch
        {
            "message" => HandleMessageAsync(bridge, onCancel: onCancel, shouldCancel: shouldCancel),
            "excise" => HandleExciseAsync(bridge, onCancel, shouldCancel),
            "timer" => HandleTimerAsync(bridge, onCancel, shouldCancel),
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };
    }

    private sealed class AllErrorsPermanentClassifier : IPermanentErrorClassifier
    {
        public bool IsMessageErrorPermanent(Exception exception) => true;

        public bool IsTimerErrorPermanent(Exception exception) => true;
    }
}
