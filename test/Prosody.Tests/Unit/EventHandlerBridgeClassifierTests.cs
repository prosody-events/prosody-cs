using System.Text.Json;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.BridgeTestSupport;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EventHandlerBridge{TPayload}"/> with an explicit
/// <see cref="Prosody.Messaging.IPermanentErrorClassifier"/>.
/// </summary>
public sealed class EventHandlerBridgeClassifierTests
{
    [Fact]
    public async Task ClassifierOverload_ReturnsPermanentWhenClassifierReturnsTrue()
    {
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => throw new JsonException("bad json"));
        var classifier = new LambdaClassifier(isMessagePermanent: _ => true, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_ReturnsTransientWhenClassifierReturnsFalse()
    {
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => throw new JsonException("bad json"));
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_BypassesAttributeReflection()
    {
        // Handler has no [PermanentError] attribute anywhere; classifier decides.
        // If attribute-path reflection ran, it would not classify FormatException as permanent.
        // Classifier returns true for FormatException, so the result must be PermanentError.
        var handler = new LambdaHandler<JsonElement>(onMessage: (_, _, _) => throw new FormatException("format error"));
        var classifier = new LambdaClassifier(
            isMessagePermanent: ex => ex is FormatException,
            isTimerPermanent: _ => false
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_TimerPermanentWhenClassifierReturnsTrue()
    {
        var handler = new LambdaHandler<JsonElement>(
            onTimer: (_, _, _) => throw new InvalidOperationException("timer boom")
        );
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => true);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleTimerAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public void ClassifierUsesMessageDecisionForExciseByDefault()
    {
        IPermanentErrorClassifier classifier = new LambdaClassifier(
            isMessagePermanent: _ => true,
            isTimerPermanent: _ => false
        );

        Assert.True(classifier.IsExciseErrorPermanent(new InvalidOperationException()));
    }

    [Fact]
    public void ClassifierCanSetAnIndependentExciseDecision()
    {
        var classifier = new ExciseClassifier();

        Assert.True(classifier.IsExciseErrorPermanent(new InvalidOperationException()));
    }

    [Fact]
    public async Task ClassifierOverload_HonorsIPermanentErrorMarker_EvenWhenClassifierReturnsFalse()
    {
        // PermanentException implements IPermanentError; classifier returns false for everything.
        // The bridge must still classify as permanent — IPermanentError takes precedence.
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new PermanentException("permanent via marker")
        );
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task ClassifierOverload_HonorsCustomIPermanentErrorMarker()
    {
        // CustomPermanentException implements IPermanentError; classifier returns false for everything.
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new CustomPermanentException("custom permanent via marker")
        );
        var classifier = new LambdaClassifier(isMessagePermanent: _ => false, isTimerPermanent: _ => false);
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options, classifier);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task RespondingClassifierControlsErrorClassification()
    {
        var bridge = EventHandlerBridge<JsonElement>.Responding(
            new ThrowingRequestHandler(),
            TestJson.Options,
            new HashSet<StateDefinition>(),
            new LambdaClassifier(ex => ex is FormatException, _ => false)
        );

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    private sealed class ThrowingRequestHandler : IProsodyRequestHandler<JsonElement, JsonElement>
    {
        public Task<JsonElement> OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        ) => throw new FormatException("invalid response");

        public Task<JsonElement> OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
            CancellationToken cancellationToken
        ) => Task.FromResult(default(JsonElement));

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

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

    private sealed class ExciseClassifier : IPermanentErrorClassifier
    {
        public bool IsMessageErrorPermanent(Exception exception) => false;

        public bool IsExciseErrorPermanent(Exception exception) => true;

        public bool IsTimerErrorPermanent(Exception exception) => false;
    }
}
