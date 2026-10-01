using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;
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
        return MessageInterop.ReadAsync(carrier => _handle.Get(key, carrier), _typeInfo, cancellationToken);
    }

    public Task<IReadOnlyList<StateValue<Message<TPayload>>>> GetManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyArray = keys as string[] ?? [.. keys];
        return StateInterop.RunAsync<IReadOnlyList<StateValue<Message<TPayload>>>>(
            async carrier =>
            {
                var items = await _handle.GetMany(keyArray, carrier).ConfigureAwait(false);
                return Array.ConvertAll(items, item => MessageInterop.MessageToValue(item, _typeInfo));
            },
            cancellationToken
        );
    }

    public Task SetAsync(string key, Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(carrier => _handle.Set(key, native, carrier), cancellationToken);
    }

    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(carrier => _handle.ContainsKey(key, carrier), cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(carrier => _handle.Remove(key, carrier), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.Clear(carrier), cancellationToken);

    public Task<IReadOnlyList<bool>> ContainsManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyArray = keys as string[] ?? [.. keys];
        return StateInterop.RunAsync<IReadOnlyList<bool>>(
            async carrier => await _handle.ContainsMany(keyArray, carrier).ConfigureAwait(false),
            cancellationToken
        );
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.IsEmpty(carrier), cancellationToken);

    public IAsyncEnumerable<string> EnumerateKeysAsync(KeyQuery query, CancellationToken cancellationToken = default) =>
        StateInterop.Keys(_handle.Keys, query, cancellationToken);

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
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(query, entry => MessageInterop.FromNative(entry.Message, _typeInfo), cancellationToken);

    public IAsyncEnumerator<KeyValuePair<string, Message<TPayload>>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(new KeyQuery(), cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task<StoreOutcome> CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Commit, cancellationToken);

    public Task<StoreOutcome> RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Rollback, cancellationToken);

    private StateScanSequence<Native.IMessageMapCursor, Native.MessageMapEntry, TItem> Entries<TItem>(
        KeyQuery query,
        Func<Native.MessageMapEntry, TItem> transform,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IMessageMapCursor, Native.MessageMapEntry, TItem>(
            () => NativeErrors.Run(() => _handle.Entries(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            transform,
            cancellationToken
        );
    }
}
