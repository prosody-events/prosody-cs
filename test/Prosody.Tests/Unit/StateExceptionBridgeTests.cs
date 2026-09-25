using System.Text.Json;
using Prosody.Infrastructure;
using Prosody.Messaging;
using Prosody.State;
using Prosody.Tests.TestHelpers;
using static Prosody.Tests.TestHelpers.BridgeTestSupport;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Tests.Unit;

/// <summary>
/// Proves state exceptions rethrown from a handler classify correctly through the existing bridge,
/// with no state-specific bridge path.
/// </summary>
public sealed class StateExceptionBridgeTests
{
    [Fact]
    public async Task PermanentStateException_FromHandler_ClassifiesPermanent()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new PermanentStateException("permanent state")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.PermanentError, result.Code);
    }

    [Fact]
    public async Task TransientStateException_FromHandler_ClassifiesTransient()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new TransientStateException("transient state")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }

    [Fact]
    public async Task NullValueException_FromHandler_ClassifiesTransient()
    {
        var handler = new LambdaHandler<JsonElement>(
            onMessage: (_, _, _) => throw new NullValueException("null write")
        );
        var bridge = new EventHandlerBridge<JsonElement>(handler, TestJson.Options);

        var result = await HandleMessageAsync(bridge);

        Assert.Equal(NativeResultCode.TransientError, result.Code);
    }
}
