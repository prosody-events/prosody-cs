using System.Text.Json;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.BridgeTestSupport;
using NativeResult = Prosody.Native.HandlerResult;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EventHandlerBridge{TPayload}"/>: construction, payload decoding,
/// and the default transient and <see cref="Prosody.Errors.IPermanentError"/> classification.
/// </summary>
/// <remarks>
/// Tests use the internal <c>HandleMessageAsync</c> / <c>HandleTimerAsync</c> methods
/// which accept primitive metadata and a byte array instead of native context objects,
/// avoiding P/Invoke into the Rust FFI crate.
/// </remarks>
public sealed class EventHandlerBridgeTests
{
    private static byte[] ToJson<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, TestJson.Options);

    [Fact]
    public void ConstructorThrowsOnNullHandler()
    {
        Assert.Throws<ArgumentNullException>(() => new EventHandlerBridge<BridgePayload>(null!, TestJson.Options));
    }

    [Fact]
    public void ConstructorThrowsOnNullOptions()
    {
        var handler = new LambdaHandler<BridgePayload>();
        Assert.Throws<ArgumentNullException>(() => new EventHandlerBridge<BridgePayload>(handler, null!));
    }

    [Fact]
    public void ClassifierConstructorThrowsOnNullClassifier()
    {
        var handler = new LambdaHandler<BridgePayload>();
        Assert.Throws<ArgumentNullException>(() =>
            new EventHandlerBridge<BridgePayload>(handler, TestJson.Options, (IPermanentErrorClassifier)null!)
        );
    }

    [Fact]
    public async Task OnMessageReturnsSuccessWhenHandlerCompletes()
    {
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);
        NativeResult result = await HandleMessageAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Null(result.ErrorMessage)
        );
    }

    [Fact]
    public async Task OnMessageReturnsTransientErrorForUnclassifiedException()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new InvalidOperationException("transient failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("transient failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForIPermanentError()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new PermanentException("permanent failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("permanent failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageReturnsPermanentErrorForCustomIPermanentError()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new CustomPermanentException("custom permanent")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("custom permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ResponseEncodingFailureIsPermanent()
    {
        var response = new CyclicResponse();
        response.Next = response;
        var bridge = EventHandlerBridge<JsonElement>.Responding(
            new RequestHandler(response),
            TestJson.Options,
            new HashSet<StateDefinition>()
        );

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task TypedOnMessageDeserializesPayloadBeforeInvokingHandler()
    {
        BridgePayload? observedPayload = null;
        Message<BridgePayload>? observedMessage = null;
        var handler = new LambdaHandler<BridgePayload>(
            onMessage: (_, message, _) =>
            {
                observedMessage = message;
                observedPayload = message.Payload;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge, payload: ToJson(new BridgePayload("typed", 3)));

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Equal(new BridgePayload("typed", 3), observedPayload),
            () => Assert.Equal("test-topic", observedMessage?.Topic),
            () => Assert.Equal("test-key", observedMessage?.Key)
        );
    }

    [Fact]
    public async Task NullJsonPayload_ReferenceType_HandlerObservesNullPayload()
    {
        BridgePayload? observedPayload = new BridgePayload("sentinel", 0);
        var handler = new LambdaHandler<BridgePayload>(
            onMessage: (_, msg, _) =>
            {
                observedPayload = msg.Payload;
                return Task.CompletedTask;
            }
        );
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge, payload: "null"u8.ToArray());

        Assert.Multiple(() => Assert.Equal(NativeResultCode.Success, result.Code), () => Assert.Null(observedPayload));
    }

    [Fact]
    public async Task JsonShapeMismatchIsPermanent()
    {
        var handler = new LambdaHandler<BridgePayload>(onMessage: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        // Valid JSON but wrong shape (array instead of object) → JsonException during deserialization
        var result = await HandleMessageAsync(bridge, payload: "[1,2,3]"u8.ToArray());

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task OnTimerReturnsSuccessWhenHandlerCompletes()
    {
        var handler = new LambdaHandler<JsonElement>(onTimer: (_, _, _) => Task.CompletedTask);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleTimerAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.Success, result.Code),
            () => Assert.Null(result.ErrorMessage)
        );
    }

    [Fact]
    public async Task OnTimerReturnsTransientErrorForUnclassifiedException()
    {
        var handler = new LambdaHandler<JsonElement>(
            onTimer: (_, _, _) => throw new InvalidOperationException("transient timer failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleTimerAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("transient timer failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnTimerReturnsPermanentErrorForIPermanentError()
    {
        var handler = new LambdaHandler<JsonElement>(
            onTimer: (_, _, _) => throw new PermanentException("permanent timer failure")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleTimerAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("permanent timer failure", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    private sealed class RequestHandler(CyclicResponse response) : IProsodyRequestHandler<JsonElement, CyclicResponse>
    {
        public Task<CyclicResponse> OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        ) => Task.FromResult(response);

        public Task<CyclicResponse> OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
            CancellationToken cancellationToken
        ) => Task.FromResult(response);

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class CyclicResponse
    {
        public CyclicResponse? Next { get; set; }
    }
}
