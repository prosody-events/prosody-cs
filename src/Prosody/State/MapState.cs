using System.Text.Json.Serialization.Metadata;

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
            async () =>
                StateInterop.JsonToValue(
                    await _handle.Get(key, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
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
            async () =>
            {
                var items = await _handle.GetMany(keyArray, StateInterop.CreateCarrier()).ConfigureAwait(false);
                var results = new StateValue<TValue>[items.Length];
                for (var i = 0; i < items.Length; i++)
                {
                    results[i] = StateInterop.JsonToValue(items[i].Bytes, _typeInfo);
                }

                return results;
            },
            cancellationToken
        );
    }

    public Task SetAsync(string key, TValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bytes = StateInterop.SerializeJsonOrThrowNull(value, _typeInfo, "Use RemoveAsync to delete instead.");
        return StateInterop.RunAsync(() => _handle.Set(key, bytes, StateInterop.CreateCarrier()), cancellationToken);
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

    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(new KeyQuery { Direction = direction }, cancellationToken);

    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(query, item => StateInterop.JsonMapEntry(item, _typeInfo), cancellationToken);

    public IAsyncEnumerable<TValue> EnumerateValuesAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateValuesAsync(new KeyQuery { Direction = direction }, cancellationToken);

    public IAsyncEnumerable<TValue> EnumerateValuesAsync(
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(query, item => StateInterop.DeserializeJson(item.Bytes, _typeInfo), cancellationToken);

    public IAsyncEnumerator<KeyValuePair<string, TValue>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(ScanDirection.Forward, cancellationToken).GetAsyncEnumerator(cancellationToken);

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
            () => StateInterop.RunSync(() => _handle.Entries(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            transform,
            cancellationToken
        );
    }
}
