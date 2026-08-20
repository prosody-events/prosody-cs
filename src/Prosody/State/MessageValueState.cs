using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.State;

/// <summary>
/// Message-flavoured single-value state handle backed by a native value handle.
/// </summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
internal sealed class MessageValueState<TPayload> : IValueState<Message<TPayload>>
{
    private readonly Native.MessageValueStateHandle _handle;
    private readonly JsonTypeInfo<TPayload> _typeInfo;

    internal MessageValueState(Native.MessageValueStateHandle handle, JsonTypeInfo<TPayload> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task<StateValue<Message<TPayload>>> GetAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                MessageInterop.MessageToValue(
                    await _handle.Get(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task SetAsync(Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(() => _handle.Set(native, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Clear(StateInterop.CreateCarrier()), cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);
}
