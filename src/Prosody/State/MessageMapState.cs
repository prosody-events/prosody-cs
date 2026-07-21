using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.State;

/// <summary>
/// Message-flavoured ordered-map state handle backed by a native map handle.
/// </summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
internal sealed class MessageMapState<TPayload> : IMapState<Message<TPayload>>
{
    private readonly Native.IMapStateHandle _handle;
    private readonly JsonTypeInfo<TPayload> _typeInfo;

    internal MessageMapState(Native.IMapStateHandle handle, JsonTypeInfo<TPayload> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task<StateValue<Message<TPayload>>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(
            async () =>
                MessageInterop.MessageToValue(
                    await _handle.Get(key, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    public Task<IReadOnlyList<StateValue<Message<TPayload>>>> GetManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyArray = keys as string[] ?? [.. keys];
        return StateInterop.RunAsync<IReadOnlyList<StateValue<Message<TPayload>>>>(
            async () =>
            {
                var items = await _handle.GetMany(keyArray, StateInterop.CreateCarrier()).ConfigureAwait(false);
                var results = new StateValue<Message<TPayload>>[items.Length];
                for (var i = 0; i < items.Length; i++)
                {
                    results[i] = MessageInterop.MessageToValue(items[i], _typeInfo);
                }

                return results;
            },
            cancellationToken
        );
    }

    public Task SetAsync(string key, Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(
            () => _handle.SetMessage(key, native, StateInterop.CreateCarrier()),
            cancellationToken
        );
    }

    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(() => _handle.ContainsKey(key, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(() => _handle.Remove(key, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Clear(StateInterop.CreateCarrier()), cancellationToken);

    public IAsyncEnumerable<string> EnumerateKeysAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new StateScanSequence<string>(
            () =>
                StateInterop.RunSync(() =>
                    _handle.ScanKeys(StateInterop.ToNative(direction), StateInterop.CreateCarrier())
                ),
            StateInterop.ItemKey,
            cancellationToken
        );
    }

    public IAsyncEnumerable<KeyValuePair<string, Message<TPayload>>> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new StateScanSequence<KeyValuePair<string, Message<TPayload>>>(
            () =>
                StateInterop.RunSync(() =>
                    _handle.Scan(StateInterop.ToNative(direction), StateInterop.CreateCarrier())
                ),
            Transform,
            cancellationToken
        );
    }

    public IAsyncEnumerator<KeyValuePair<string, Message<TPayload>>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(ScanDirection.Forward, cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);

    private KeyValuePair<string, Message<TPayload>> Transform(Native.StateScanItem item) =>
        item is Native.StateScanItem.MapMessage entry
            ? KeyValuePair.Create(entry.Key, MessageInterop.FromNative(entry.Message, _typeInfo))
            : throw new TransientStateException("State scan item shape mismatch: expected a message map entry.");
}
