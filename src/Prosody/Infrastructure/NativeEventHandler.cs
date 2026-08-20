using NativeHandler = Prosody.Native.EventHandler;
using NativeResult = Prosody.Native.HandlerResult;

namespace Prosody.Infrastructure;

/// <summary>
/// Takes native event resources and delegates each event to the typed bridge.
/// </summary>
internal sealed class NativeEventHandler<TPayload> : NativeHandler
{
    private readonly Native.ProsodyClient _client;
    private readonly EventHandlerBridge<TPayload> _bridge;

    internal NativeEventHandler(Native.ProsodyClient client, EventHandlerBridge<TPayload> bridge)
    {
        _client = client;
        _bridge = bridge;
    }

    public async Task<NativeResult> OnMessage(ulong eventId, Dictionary<string, string> carrier)
    {
        using var nativeEvent = _client.TakeEvent(eventId);
        using var context = nativeEvent.TakeContext();
        using var message = nativeEvent.MessageValue();
        return await _bridge.OnMessage(context, message, carrier).ConfigureAwait(false);
    }

    public async Task<NativeResult> OnExcise(ulong eventId, Dictionary<string, string> carrier)
    {
        using var nativeEvent = _client.TakeEvent(eventId);
        using var context = nativeEvent.TakeContext();
        using var message = nativeEvent.ExciseValue();
        return await _bridge.OnExcise(context, message, carrier).ConfigureAwait(false);
    }

    public async Task<NativeResult> OnTimer(ulong eventId, Dictionary<string, string> carrier)
    {
        using var nativeEvent = _client.TakeEvent(eventId);
        using var context = nativeEvent.TakeContext();
        using var timer = nativeEvent.TimerValue();
        return await _bridge.OnTimer(context, timer, carrier).ConfigureAwait(false);
    }
}
