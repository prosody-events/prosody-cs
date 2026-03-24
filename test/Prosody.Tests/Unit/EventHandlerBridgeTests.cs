using System.Diagnostics.CodeAnalysis;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using static Prosody.Tests.TestHelpers.TestDefaults;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EventHandlerBridge"/>.
/// </summary>
/// <remarks>
/// Tests use the internal <c>HandleMessageAsync</c> / <c>HandleTimerAsync</c> methods
/// which accept a <c>Func&lt;Task&gt;</c> for the cancel signal instead of a native context,
/// avoiding P/Invoke into the Rust FFI crate.
/// </remarks>
public sealed class EventHandlerBridgeTests
{
    #region Constructor Tests

    [Fact]
    public void ConstructorThrowsOnNullHandler()
    {
        Assert.Throws<ArgumentNullException>(() => new EventHandlerBridge(null!));
    }

    #endregion Constructor Tests

    #region OnMessage Tests

    [Fact]
    public async Task OnMessageReturnsSuccessWhenHandlerCompletes()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Null(result.ErrorMessage)
        );
    }

    [Fact]
    public async Task OnMessageReturnsTransientErrorForUnclassifiedException()
    {
        var handler = new LambdaHandler(
            onMessage: (_, _, _) => throw new InvalidOperationException("transient failure")
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("transient failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForIPermanentError()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => throw new PermanentException("permanent failure"));
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("permanent failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForCustomIPermanentError()
    {
        var handler = new LambdaHandler(onMessage: (_, _, _) => throw new CustomPermanentException("custom permanent"));
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("custom permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForAttributeMatchedType()
    {
        var handler = new AttributeOnMessageHandler(onMessage: (_, _, _) => throw new FormatException("bad format"));
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("bad format", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForAttributeSubtype()
    {
        var handler = new AttributeOnMessageHandler(
            onMessage: (_, _, _) => throw new ArgumentNullException("param", "subtype of ArgumentException")
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task OnMessageReturnsTransientErrorForAttributeUnmatchedType()
    {
        var handler = new AttributeOnMessageHandler(
            onMessage: (_, _, _) => throw new InvalidOperationException("not in attribute list")
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("not in attribute list", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    #endregion OnMessage Tests

    #region OnTimer Tests

    [Fact]
    public async Task OnTimerReturnsSuccessWhenHandlerCompletes()
    {
        var handler = new LambdaHandler(onTimer: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Null(result.ErrorMessage)
        );
    }

    [Fact]
    public async Task OnTimerReturnsTransientErrorForUnclassifiedException()
    {
        var handler = new LambdaHandler(
            onTimer: (_, _, _) => throw new InvalidOperationException("transient timer failure")
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("transient timer failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerReturnsPermanentErrorForIPermanentError()
    {
        var handler = new LambdaHandler(onTimer: (_, _, _) => throw new PermanentException("permanent timer failure"));
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("permanent timer failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerReturnsPermanentErrorForAttributeMatchedType()
    {
        var handler = new AttributeOnTimerHandler(onTimer: (_, _, _) => throw new FormatException("bad timer format"));
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("bad timer format", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerReturnsTransientErrorForAttributeUnmatchedType()
    {
        var handler = new AttributeOnTimerHandler(
            onTimer: (_, _, _) => throw new InvalidOperationException("not in timer attribute list")
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("not in timer attribute list", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    #endregion OnTimer Tests

    #region BridgeCancellationAsync Tests

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

        var handler = new LambdaHandler(
            onMessage: async (_, _, ct) =>
            {
                // Signal that the handler is running
                handlerStarted.TrySetResult();

                // Wait for cancellation — the core scenario this bridge exists for
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
        var bridge = new EventHandlerBridge(handler);

        var handleTask = bridge.HandleMessageAsync(null!, null!, () => cancelTcs.Task, EmptyCarrier);

        // Wait until the handler is actively running
        await handlerStarted.Task;

        // Fire the cancel signal while the handler is blocked
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

        var handler = new LambdaHandler(
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
        var bridge = new EventHandlerBridge(handler);

        var handleTask = bridge.HandleTimerAsync(null!, null!, () => cancelTcs.Task, EmptyCarrier);

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

        var handler = new LambdaHandler(
            onMessage: async (_, _, ct) =>
            {
                handlerStarted.TrySetResult();

                // Let the OperationCanceledException propagate — simulates a handler that does not
                // catch cancellation. This is classified as transient because the work is incomplete
                // and Prosody should redeliver the message.
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        );
        var bridge = new EventHandlerBridge(handler);

        var handleTask = bridge.HandleMessageAsync(null!, null!, () => cancelTcs.Task, EmptyCarrier);

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

        var handler = new LambdaHandler(
            onTimer: async (_, _, ct) =>
            {
                handlerStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        );
        var bridge = new EventHandlerBridge(handler);

        var handleTask = bridge.HandleTimerAsync(null!, null!, () => cancelTcs.Task, EmptyCarrier);

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

        // Handler completes first
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

        // Handler completes first
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
        var handler = new LambdaHandler(
            onMessage: (_, _, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(
            null!,
            null!,
            () => throw new InvalidOperationException("context torn down"),
            EmptyCarrier
        );

        Assert.Multiple(() => Assert.True(handlerCalled), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }

    [Fact]
    public async Task HandleTimerCompletesWhenOnCancelFaults()
    {
        var handlerCalled = false;
        var handler = new LambdaHandler(
            onTimer: (_, _, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleTimerAsync(
            null!,
            null!,
            () => throw new InvalidOperationException("context torn down"),
            EmptyCarrier
        );

        Assert.Multiple(() => Assert.True(handlerCalled), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }

    #endregion BridgeCancellationAsync Tests

    #region GetPermanentErrorAttribute Interface Map Fallback Tests

    [Fact]
    public async Task OnMessageDetectsAttributeOnExplicitInterfaceImplementation()
    {
        var handler = new ExplicitInterfaceHandler(onMessage: () => throw new FormatException("explicit permanent"));
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleMessageAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("explicit permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerDetectsAttributeOnExplicitInterfaceImplementation()
    {
        var handler = new ExplicitInterfaceHandler(
            onTimer: () => throw new FormatException("explicit timer permanent")
        );
        var bridge = new EventHandlerBridge(handler);

        var result = await bridge.HandleTimerAsync(null!, null!, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("explicit timer permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    #endregion GetPermanentErrorAttribute Interface Map Fallback Tests

    #region Test Handlers

    /// <summary>
    /// Simple handler that delegates to lambdas. No attributes.
    /// </summary>
    private sealed class LambdaHandler(
        Func<ProsodyContext, Message, CancellationToken, Task>? onMessage = null,
        Func<ProsodyContext, ProsodyTimer, CancellationToken, Task>? onTimer = null
    ) : IProsodyHandler
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message message,
            CancellationToken cancellationToken
        ) => onMessage?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => onTimer?.Invoke(prosodyContext, timer, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Handler with <see cref="PermanentErrorAttribute"/> on <see cref="IProsodyHandler.OnMessageAsync"/>.
    /// </summary>
    private sealed class AttributeOnMessageHandler(Func<ProsodyContext, Message, CancellationToken, Task> onMessage)
        : IProsodyHandler
    {
        [PermanentError(typeof(FormatException), typeof(ArgumentException))]
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message message,
            CancellationToken cancellationToken
        ) => onMessage(prosodyContext, message, cancellationToken);

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    /// <summary>
    /// Handler with <see cref="PermanentErrorAttribute"/> on <see cref="IProsodyHandler.OnTimerAsync"/>.
    /// </summary>
    private sealed class AttributeOnTimerHandler(Func<ProsodyContext, ProsodyTimer, CancellationToken, Task> onTimer)
        : IProsodyHandler
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        [PermanentError(typeof(FormatException), typeof(ArgumentException))]
        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => onTimer(prosodyContext, timer, cancellationToken);
    }

    /// <summary>
    /// Handler using explicit interface implementation with <see cref="PermanentErrorAttribute"/>.
    /// Tests the <c>GetPermanentErrorAttribute</c> interface-map fallback path.
    /// </summary>
    /// <remarks>
    /// These methods are intentionally non-async. When the <see cref="Action"/> delegate throws,
    /// the exception propagates synchronously out of the method — <c>return Task.CompletedTask</c>
    /// is never reached. The caller (<see cref="EventHandlerBridge.InvokeHandlerAsync"/>) receives
    /// the exception from <c>await handler(ct)</c> as a synchronous throw rather than a faulted
    /// <see cref="Task"/>, which exercises the same catch blocks either way.
    /// </remarks>
    private sealed class ExplicitInterfaceHandler(Action? onMessage = null, Action? onTimer = null) : IProsodyHandler
    {
        [PermanentError(typeof(FormatException))]
        Task IProsodyHandler.OnMessageAsync(
            ProsodyContext prosodyContext,
            Message message,
            CancellationToken cancellationToken
        )
        {
            onMessage?.Invoke();
            return Task.CompletedTask;
        }

        [PermanentError(typeof(FormatException))]
        Task IProsodyHandler.OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        )
        {
            onTimer?.Invoke();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Custom exception implementing <see cref="IPermanentError"/> for testing.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "Test-only exception; minimal constructors sufficient"
    )]
    private sealed class CustomPermanentException(string message) : Exception(message), IPermanentError;

    #endregion Test Handlers
}
