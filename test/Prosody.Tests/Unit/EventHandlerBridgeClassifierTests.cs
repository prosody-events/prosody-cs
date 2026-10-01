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
    /// <summary>
    /// An <see cref="IPermanentError"/> marker or a classifier decision of <see langword="true"/> gives a
    /// permanent result. Every other error is transient. A <see langword="null"/> decision uses the
    /// default classifier, and the classifier gives the opposite decision for the other path.
    /// </summary>
    [Theory]
    [InlineData("format", null, false, false)]
    [InlineData("marker", null, false, true)]
    [InlineData("custom", null, false, true)]
    [InlineData("format", null, true, false)]
    [InlineData("marker", null, true, true)]
    [InlineData("format", true, false, true)]
    [InlineData("format", false, false, false)]
    [InlineData("format", true, true, true)]
    [InlineData("format", false, true, false)]
    [InlineData("marker", false, false, true)]
    [InlineData("custom", false, false, true)]
    public async Task HandlerErrorClassification(string error, bool? decision, bool timer, bool expectPermanent)
    {
        Exception exception = error switch
        {
            "marker" => new PermanentException("handler failure"),
            "custom" => new CustomPermanentException("handler failure"),
            _ => new FormatException("handler failure"),
        };
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw exception,
            onTimer: (_, _, _) => throw exception
        );
        var bridge = decision is { } permanent
            ? new EventHandlerBridge<JsonElement>(
                handler,
                TestJson.Options,
                new LambdaClassifier(_ => permanent != timer, _ => permanent == timer)
            )
            : new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = timer ? await HandleTimerAsync(bridge) : await HandleMessageAsync(bridge);

        Assert.Multiple(
            () =>
                Assert.Equal(
                    expectPermanent ? NativeResultCode.PermanentError : NativeResultCode.TransientError,
                    result.Code
                ),
            () => Assert.Contains("handler failure", result.ErrorMessage, StringComparison.Ordinal)
        );
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
}
