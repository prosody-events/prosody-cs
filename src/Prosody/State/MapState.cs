using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>
/// JSON-flavoured ordered-map state handle backed by a native map handle.
/// </summary>
/// <typeparam name="TValue">The stored value type.</typeparam>
internal sealed class MapState<TValue> : IMapState<TValue>
    where TValue : notnull
{
    private readonly Native.IJsonMapStateHandle _handle;
    private readonly JsonTypeInfo<TValue> _typeInfo;

    internal MapState(Native.IJsonMapStateHandle handle, JsonTypeInfo<TValue> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task<StateValue<TValue>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(
            async carrier => StateInterop.JsonToValue(await _handle.Get(key, carrier).ConfigureAwait(false), _typeInfo),
            cancellationToken
        );
    }

    public Task<IReadOnlyList<StateValue<TValue>>> GetManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyArray = keys as string[] ?? [.. keys];
        return StateInterop.RunAsync<IReadOnlyList<StateValue<TValue>>>(
            async carrier =>
            {
                var items = await _handle.GetMany(keyArray, carrier).ConfigureAwait(false);
                return Array.ConvertAll(items, item => StateInterop.JsonToValue(item.Bytes, _typeInfo));
            },
            cancellationToken
        );
    }

    public Task SetAsync(string key, TValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bytes = StateInterop.SerializeJson(value, _typeInfo);
        return StateInterop.RunAsync(carrier => _handle.Set(key, bytes, carrier), cancellationToken);
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

    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(query, item => StateInterop.JsonMapEntry(item, _typeInfo), cancellationToken);

    public IAsyncEnumerable<TValue> EnumerateValuesAsync(
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(query, item => StateInterop.DeserializeJson(item.Bytes, _typeInfo), cancellationToken);

    public IAsyncEnumerator<KeyValuePair<string, TValue>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(new KeyQuery(), cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task<StoreOutcome> CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Commit, cancellationToken);

    public Task<StoreOutcome> RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Rollback, cancellationToken);

    private StateScanSequence<Native.IJsonMapCursor, Native.JsonMapEntry, TItem> Entries<TItem>(
        KeyQuery query,
        Func<Native.JsonMapEntry, TItem> transform,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IJsonMapCursor, Native.JsonMapEntry, TItem>(
            () => NativeErrors.Run(() => _handle.Entries(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            transform,
            cancellationToken
        );
    }
}
