using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.State;

/// <summary>
/// Message-flavoured ordered-map state handle backed by a native map handle.
/// </summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
internal sealed class MessageMapState<TPayload> : IMapState<Message<TPayload>>
{
    private readonly Native.IMessageMapStateHandle _handle;
    private readonly JsonTypeInfo<TPayload> _typeInfo;

    internal MessageMapState(Native.IMessageMapStateHandle handle, JsonTypeInfo<TPayload> typeInfo)
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
        return StateInterop.RunAsync(() => _handle.Set(key, native, StateInterop.CreateCarrier()), cancellationToken);
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

    public Task<IReadOnlyList<bool>> ContainsManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyArray = keys as string[] ?? [.. keys];
        return StateInterop.RunAsync<IReadOnlyList<bool>>(
            async () => await _handle.ContainsMany(keyArray, StateInterop.CreateCarrier()).ConfigureAwait(false),
            cancellationToken
        );
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.IsEmpty(StateInterop.CreateCarrier()), cancellationToken);

    public IAsyncEnumerable<string> EnumerateKeysAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateKeysAsync(new KeyQuery { Direction = direction }, cancellationToken);

    public IAsyncEnumerable<string> EnumerateKeysAsync(KeyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IKeyCursor, string, string>(
            () => StateInterop.RunSync(() => _handle.Keys(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            static key => key,
            cancellationToken
        );
    }

    public IAsyncEnumerable<KeyValuePair<string, Message<TPayload>>> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(new KeyQuery { Direction = direction }, cancellationToken);

    public IAsyncEnumerable<KeyValuePair<string, Message<TPayload>>> EnumerateAsync(
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) =>
        Entries(
            query,
            entry => KeyValuePair.Create(entry.Key, MessageInterop.FromNative(entry.Message, _typeInfo)),
            cancellationToken
        );

    public IAsyncEnumerable<Message<TPayload>> EnumerateValuesAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateValuesAsync(new KeyQuery { Direction = direction }, cancellationToken);

    public IAsyncEnumerable<Message<TPayload>> EnumerateValuesAsync(
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(query, entry => MessageInterop.FromNative(entry.Message, _typeInfo), cancellationToken);

    public IAsyncEnumerator<KeyValuePair<string, Message<TPayload>>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(ScanDirection.Forward, cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);
}
