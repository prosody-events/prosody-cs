using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.TestDefaults;
using NativeResult = Prosody.Native.HandlerResult;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EventHandlerBridge{TPayload}"/> and the shared static
/// <see cref="EventHandlerBridge"/> infrastructure.
/// </summary>
/// <remarks>
/// Tests use the internal <c>HandleMessageAsync</c> / <c>HandleTimerAsync</c> methods
/// which accept primitive metadata and a byte array instead of native context objects,
/// avoiding P/Invoke into the Rust FFI crate.
/// </remarks>
public sealed class EventHandlerBridgeTests
{
    private sealed record BridgePayload(string Name, int Count);

    // Placeholder payload for tests that don't care about message contents.
    private static readonly byte[] AnyJson = "null"u8.ToArray();

    private static readonly ProsodyContext AnyContext = new();
    private static readonly ProsodyTimer AnyTimer = new("test-key", default);

    // Invoke HandleMessageAsync with default test metadata, using AnyJson unless payload is specified.
    private static Task<NativeResult> HandleMsg<T>(
        EventHandlerBridge<T> bridge,
        byte[]? payload = null,
        Func<Task>? onCancel = null
    ) =>
        bridge.HandleMessageAsync(
            AnyContext,
            "test-topic",
            "test-key",
            partition: 1,
            offset: 2L,
            DateTimeOffset.UnixEpoch,
            payload ?? AnyJson,
            onCancel ?? NeverCancel,
            EmptyCarrier
        );

    private static byte[] ToJson<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, TestJson.Options);

    #region Constructor Tests

    [Fact]
    public void ConstructorThrowsOnNullHandler()
    {
        Assert.Throws<ArgumentNullException>(() => new EventHandlerBridge<BridgePayload>(null!, TestJson.Options));
    }

    [Fact]
    public void ConstructorThrowsOnNullOptions()
    {
        var handler = new TypedLambdaHandler<BridgePayload>();
        Assert.Throws<ArgumentNullException>(() => new EventHandlerBridge<BridgePayload>(handler, null!));
    }

    [Fact]
    public void ClassifierConstructorThrowsOnNullClassifier()
    {
        var handler = new TypedLambdaHandler<BridgePayload>();
        Assert.Throws<ArgumentNullException>(() =>
            new EventHandlerBridge<BridgePayload>(handler, TestJson.Options, (IPermanentErrorClassifier)null!)
        );
    }

    #endregion Constructor Tests

    #region OnMessage Tests

    [Fact]
    public async Task OnMessageReturnsSuccessWhenHandlerCompletes()
    {
        var handler = new TypedLambdaHandler<JsonElement>(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);
        NativeResult result = await HandleMsg(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Null(result.ErrorMessage)
        );
    }

    [Fact]
    public async Task OnMessageReturnsTransientErrorForUnclassifiedException()
    {
        var handler = new TypedLambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new InvalidOperationException("transient failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("transient failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForIPermanentError()
    {
        var handler = new TypedLambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new PermanentException("permanent failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("permanent failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForCustomIPermanentError()
    {
        var handler = new TypedLambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new CustomPermanentException("custom permanent")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("custom permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForAttributeMatchedType()
    {
        var handler = new AttributeOnMessageHandler(onMessage: (_, _, _) => throw new FormatException("bad format"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

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
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task TypedOnMessageDeserializesPayloadBeforeInvokingHandler()
    {
        BridgePayload? observedPayload = null;
        Message<BridgePayload>? observedMessage = null;
        var handler = new TypedLambdaHandler<BridgePayload>(
            onMessage: (_, message, _) =>
            {
                observedMessage = message;
                observedPayload = message.Payload;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMsg(bridge, payload: ToJson(new BridgePayload("typed", 3)));

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Equal(new BridgePayload("typed", 3), observedPayload),
            () => Assert.Equal("test-topic", observedMessage?.Topic),
            () => Assert.Equal("test-key", observedMessage?.Key)
        );
    }

    [Fact]
    public async Task TypedOnMessageClassifiesPayloadDeserializationFailuresWithMethodAttribute()
    {
        var handler = new TypedPermanentJsonHandler();
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMsg(bridge, payload: "{not valid json"u8.ToArray());

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains(nameof(JsonException), result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsTransientErrorForAttributeUnmatchedType()
    {
        var handler = new AttributeOnMessageHandler(
            onMessage: (_, _, _) => throw new InvalidOperationException("not in attribute list")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("not in attribute list", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task NullJsonPayload_ReferenceType_HandlerObservesNullPayload()
    {
        BridgePayload? observedPayload = new BridgePayload("sentinel", 0);
        var handler = new TypedLambdaHandler<BridgePayload>(
            onMessage: (_, msg, _) =>
            {
                observedPayload = msg.Payload;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMsg(bridge, payload: "null"u8.ToArray());

        Assert.Multiple(() => Assert.Equal(NativeResultCode.Success, result.Code), () => Assert.Null(observedPayload));
    }

    [Fact]
    public async Task EmptyPayload_ClassifiedByJsonException()
    {
        var handler = new TypedPermanentJsonHandler();
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMsg(bridge, payload: Array.Empty<byte>());

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task JsonShapeMismatch_ClassifiedAsTransientByDefault()
    {
        var handler = new TypedLambdaHandler<BridgePayload>(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        // Valid JSON but wrong shape (array instead of object) → JsonException during deserialization
        var result = await HandleMsg(bridge, payload: "[1,2,3]"u8.ToArray());

        // No [PermanentError] attribute on handler, so JsonException is transient
        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }

    #endregion OnMessage Tests

    #region OnTimer Tests

    [Fact]
    public async Task OnTimerReturnsSuccessWhenHandlerCompletes()
    {
        var handler = new TypedLambdaHandler<JsonElement>(onTimer: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await bridge.HandleTimerAsync(AnyContext, AnyTimer, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Null(result.ErrorMessage)
        );
    }

    [Fact]
    public async Task OnTimerReturnsTransientErrorForUnclassifiedException()
    {
        var handler = new TypedLambdaHandler<JsonElement>(
            onTimer: (_, _, _) => throw new InvalidOperationException("transient timer failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await bridge.HandleTimerAsync(AnyContext, AnyTimer, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("transient timer failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerReturnsPermanentErrorForIPermanentError()
    {
        var handler = new TypedLambdaHandler<JsonElement>(
            onTimer: (_, _, _) => throw new PermanentException("permanent timer failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await bridge.HandleTimerAsync(AnyContext, AnyTimer, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("permanent timer failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerReturnsPermanentErrorForAttributeMatchedType()
    {
        var handler = new AttributeOnTimerHandler(onTimer: (_, _, _) => throw new FormatException("bad timer format"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await bridge.HandleTimerAsync(AnyContext, AnyTimer, NeverCancel, EmptyCarrier);

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
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await bridge.HandleTimerAsync(AnyContext, AnyTimer, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("not in timer attribute list", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    #endregion OnTimer Tests

    #region IPermanentErrorClassifier Tests

    [Fact]
    public async Task ClassifierOverload_ReturnsPermanentWhenClassifierReturnsTrue()
    {
        var handler = new TypedLambdaHandler<JsonElement>(onMessage: (_, _, _) => throw new JsonException("bad json"));
        var classifier = new LambdaClassifier(isMessagePermanent: _ => true, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMsg(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_ReturnsTransientWhenClassifierReturnsFalse()
    {
        var handler = new TypedLambdaHandler<JsonElement>(onMessage: (_, _, _) => throw new JsonException("bad json"));
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMsg(bridge);

        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_BypassesAttributeReflection()
    {
        // Handler has no [PermanentError] attribute anywhere; classifier decides.
        // If attribute-path reflection ran, it would not classify FormatException as permanent.
        // Classifier returns true for FormatException, so the result must be PermanentError.
        var handler = new TypedLambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new FormatException("format error")
        );
        var classifier = new LambdaClassifier(
            isMessagePermanent: ex => ex is FormatException,
            isTimerPermanent: _ => false
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMsg(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_TimerPermanentWhenClassifierReturnsTrue()
    {
        var handler = new TypedLambdaHandler<JsonElement>(
            onTimer: (_, _, _) => throw new InvalidOperationException("timer boom")
        );
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => true);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await bridge.HandleTimerAsync(AnyContext, AnyTimer, NeverCancel, EmptyCarrier);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_HonorsIPermanentErrorMarker_EvenWhenClassifierReturnsFalse()
    {
        // PermanentException implements IPermanentError; classifier returns false for everything.
        // The bridge must still classify as permanent — IPermanentError takes precedence.
        var handler = new TypedLambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new PermanentException("permanent via marker")
        );
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMsg(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_HonorsCustomIPermanentErrorMarker()
    {
        // CustomPermanentException implements IPermanentError; classifier returns false for everything.
        var handler = new TypedLambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new CustomPermanentException("custom permanent via marker")
        );
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMsg(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    #endregion IPermanentErrorClassifier Tests

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

        var handler = new TypedLambdaHandler<JsonElement>(
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

        var handleTask = HandleMsg(bridge, onCancel: () => cancelTcs.Task);

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

        var handler = new TypedLambdaHandler<JsonElement>(
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

        var handleTask = bridge.HandleTimerAsync(AnyContext, AnyTimer, () => cancelTcs.Task, EmptyCarrier);

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

        var handler = new TypedLambdaHandler<JsonElement>(
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

        var handleTask = HandleMsg(bridge, onCancel: () => cancelTcs.Task);

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

        var handler = new TypedLambdaHandler<JsonElement>(
            onTimer: async (_, _, ct) =>
            {
                handlerStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var handleTask = bridge.HandleTimerAsync(AnyContext, AnyTimer, () => cancelTcs.Task, EmptyCarrier);

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
        var handler = new TypedLambdaHandler<JsonElement>(
            onMessage: (_, _, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge, onCancel: () => throw new InvalidOperationException("context torn down"));

        Assert.Multiple(() => Assert.True(handlerCalled), () => Assert.Equal(NativeResultCode.Success, result.Code));
    }

    [Fact]
    public async Task HandleTimerCompletesWhenOnCancelFaults()
    {
        var handlerCalled = false;
        var handler = new TypedLambdaHandler<JsonElement>(
            onTimer: (_, _, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await bridge.HandleTimerAsync(
            AnyContext,
            AnyTimer,
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
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("explicit permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerDetectsAttributeOnExplicitInterfaceImplementation()
    {
        var handler = new ExplicitInterfaceHandler(onTimer: () =>
            throw new FormatException("explicit timer permanent")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await bridge.HandleTimerAsync(AnyContext, AnyTimer, NeverCancel, EmptyCarrier);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("explicit timer permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    #endregion GetPermanentErrorAttribute Interface Map Fallback Tests

    #region Test Handlers

    /// <summary>
    /// Typed handler that delegates to lambdas. No attributes.
    /// </summary>
    private sealed class TypedLambdaHandler<T>(
        Func<ProsodyContext, Message<T>, CancellationToken, Task>? onMessage = null,
        Func<ProsodyContext, ProsodyTimer, CancellationToken, Task>? onTimer = null
    ) : IProsodyHandler<T>
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<T> message,
            CancellationToken cancellationToken
        ) => onMessage?.Invoke(prosodyContext, message, cancellationToken) ?? Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => onTimer?.Invoke(prosodyContext, timer, cancellationToken) ?? Task.CompletedTask;
    }

    private sealed class TypedPermanentJsonHandler : IProsodyHandler<BridgePayload>
    {
        [PermanentError(typeof(JsonException))]
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<BridgePayload> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    /// <summary>
    /// Handler with <see cref="PermanentErrorAttribute"/> on <see cref="IProsodyHandler{T}.OnMessageAsync"/>.
    /// </summary>
    private sealed class AttributeOnMessageHandler(
        Func<ProsodyContext, Message<JsonElement>, CancellationToken, Task> onMessage
    ) : IProsodyHandler<JsonElement>
    {
        [PermanentError(typeof(FormatException), typeof(ArgumentException))]
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        ) => onMessage(prosodyContext, message, cancellationToken);

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    /// <summary>
    /// Handler with <see cref="PermanentErrorAttribute"/> on <see cref="IProsodyHandler{T}.OnTimerAsync"/>.
    /// </summary>
    private sealed class AttributeOnTimerHandler(Func<ProsodyContext, ProsodyTimer, CancellationToken, Task> onTimer)
        : IProsodyHandler<JsonElement>
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
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
    private sealed class ExplicitInterfaceHandler(Action? onMessage = null, Action? onTimer = null)
        : IProsodyHandler<JsonElement>
    {
        [PermanentError(typeof(FormatException))]
        Task IProsodyHandler<JsonElement>.OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        )
        {
            onMessage?.Invoke();
            return Task.CompletedTask;
        }

        [PermanentError(typeof(FormatException))]
        Task IProsodyHandler<JsonElement>.OnTimerAsync(
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

    /// <summary>
    /// <see cref="IPermanentErrorClassifier"/> that delegates to lambdas.
    /// </summary>
    private sealed class LambdaClassifier(
        Func<Exception, bool> isMessagePermanent,
        Func<Exception, bool> isTimerPermanent
    ) : IPermanentErrorClassifier
    {
        public bool IsMessageErrorPermanent(Exception exception) => isMessagePermanent(exception);

        public bool IsTimerErrorPermanent(Exception exception) => isTimerPermanent(exception);
    }

    #endregion Test Handlers
}
