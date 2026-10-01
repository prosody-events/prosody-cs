using System.Text.Json;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.BridgeTestSupport;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="Prosody.Errors.PermanentErrorAttribute"/> classification path of
/// <see cref="EventHandlerBridge{TPayload}"/>, including explicit interface implementations.
/// </summary>
public sealed class EventHandlerBridgeAttributeTests
{
    [Fact]
    public async Task OnMessageReturnsPermanentErrorForAttributeMatchedType()
    {
        var handler = new AttributeOnMessageHandler(onMessage: (_, _, _) => throw new FormatException("bad format"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

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

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task OnMessageReturnsTransientErrorForAttributeUnmatchedType()
    {
        var handler = new AttributeOnMessageHandler(
            onMessage: (_, _, _) => throw new InvalidOperationException("not in attribute list")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("not in attribute list", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task TypedOnMessageClassifiesPayloadDeserializationFailuresWithMethodAttribute()
    {
        var handler = new TypedPermanentJsonHandler();
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge, payload: "{not valid json"u8.ToArray());

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains(nameof(JsonException), result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task EmptyPayload_ClassifiedByJsonException()
    {
        var handler = new TypedPermanentJsonHandler();
        var bridge = new EventHandlerBridge<BridgePayload>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge, payload: Array.Empty<byte>());

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task OnTimerReturnsPermanentErrorForAttributeMatchedType()
    {
        var handler = new AttributeOnTimerHandler(onTimer: (_, _, _) => throw new FormatException("bad timer format"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleTimerAsync(bridge);

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

        var result = await HandleTimerAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.TransientError, result.Code),
            () => Assert.Contains("not in timer attribute list", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task OnMessageDetectsAttributeOnExplicitInterfaceImplementation()
    {
        var handler = new ExplicitInterfaceHandler(onMessage: () => throw new FormatException("explicit permanent"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

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

        var result = await HandleTimerAsync(bridge);

        Assert.Multiple(
            () => Assert.Equal(NativeResultCode.PermanentError, result.Code),
            () => Assert.Contains("explicit timer permanent", result.ErrorMessage, StringComparison.Ordinal)
        );
    }

    private sealed class TypedPermanentJsonHandler : IProsodyHandler<BridgePayload>
    {
        [PermanentError(typeof(JsonException))]
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<BridgePayload> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
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

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

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

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
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
        Task IProsodyHandler<JsonElement>.OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

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
}
