using System.Text.Json;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.TestDefaults;
using NativeResult = Prosody.Native.HandlerResult;

namespace Prosody.Tests.Unit;

/// <summary>
/// Proves state exceptions rethrown from a handler classify correctly through the existing bridge,
/// with no state-specific bridge path.
/// </summary>
public sealed class StateExceptionBridgeTests
{
    private static readonly ProsodyContext AnyContext = new();
    private static readonly byte[] AnyJson = "null"u8.ToArray();

    private static Task<NativeResult> HandleMsg(EventHandlerBridge<JsonElement> bridge) =>
        bridge.HandleMessageAsync(
            AnyContext,
            "test-topic",
            "test-key",
            partition: 0,
            offset: 0L,
            DateTimeOffset.UnixEpoch,
            AnyJson,
            NeverCancel,
            EmptyCarrier
        );

    [Fact]
    public async Task PermanentStateException_FromHandler_ClassifiesPermanent()
    {
        var handler = new LambdaHandler((_, _, _) => throw new PermanentStateException("permanent state"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.IsType<NativeResult.PermanentError>(result);
    }

    [Fact]
    public async Task TransientStateException_FromHandler_ClassifiesTransient()
    {
        var handler = new LambdaHandler((_, _, _) => throw new TransientStateException("transient state"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.IsType<NativeResult.TransientError>(result);
    }

    [Fact]
    public async Task NullValueException_FromHandler_ClassifiesTransient()
    {
        var handler = new LambdaHandler((_, _, _) => throw new NullValueException("null write"));
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMsg(bridge);

        Assert.IsType<NativeResult.TransientError>(result);
    }

    private sealed class LambdaHandler(Func<ProsodyContext, Message<JsonElement>, CancellationToken, Task> onMessage)
        : IProsodyHandler<JsonElement>
    {
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
}
