using System.Text.Json.Serialization.Metadata;

namespace Prosody.State;

/// <summary>Read-only access to a published ordered-map collection.</summary>
public sealed class PublishedMap<TValue>
    where TValue : notnull
{
    private readonly Native.IPublishedMapHandle _handle;
    private readonly JsonTypeInfo<TValue> _typeInfo;

    internal PublishedMap(Native.IPublishedMapHandle handle, JsonTypeInfo<TValue> typeInfo) =>
        (_handle, _typeInfo) = (handle, typeInfo);

    /// <summary>Reads one entry for a user key.</summary>
    public Task<StateValue<TValue>> GetAsync(string key, string mapKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mapKey);
        return StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.Get(key, mapKey, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    /// <summary>Reads several entries for a user key in one batch.</summary>
    public Task<IReadOnlyList<StateValue<TValue>>> GetManyAsync(
        string key,
        IEnumerable<string> mapKeys,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mapKeys);
        var keys = mapKeys as string[] ?? [.. mapKeys];
        return StateInterop.RunAsync<IReadOnlyList<StateValue<TValue>>>(
            async () =>
            {
                var items = await _handle.GetMany(key, keys, StateInterop.CreateCarrier()).ConfigureAwait(false);
                return Array.ConvertAll(items, item => StateInterop.JsonToValue(item.Bytes, _typeInfo));
            },
            cancellationToken
        );
    }

    /// <summary>Reports whether one entry exists for a user key.</summary>
    public Task<bool> ContainsKeyAsync(string key, string mapKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mapKey);
        return StateInterop.RunAsync(
            () => _handle.ContainsKey(key, mapKey, StateInterop.CreateCarrier()),
            cancellationToken
        );
    }

    /// <summary>Tests several entries for a user key in one batch. <c>result[i]</c> answers the i-th map key.</summary>
    public Task<IReadOnlyList<bool>> ContainsManyAsync(
        string key,
        IEnumerable<string> mapKeys,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mapKeys);
        var keys = mapKeys as string[] ?? [.. mapKeys];
        return StateInterop.RunAsync<IReadOnlyList<bool>>(
            async () => await _handle.ContainsMany(key, keys, StateInterop.CreateCarrier()).ConfigureAwait(false),
            cancellationToken
        );
    }

    /// <summary>Determines whether the map for a user key is empty.</summary>
    public Task<bool> IsEmptyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(() => _handle.IsEmpty(key, StateInterop.CreateCarrier()), cancellationToken);
    }

    /// <summary>Enumerates keys without reading values.</summary>
    public IAsyncEnumerable<string> EnumerateKeysAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateKeysAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the keys that <paramref name="query"/> selects without reading values.</summary>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<string> EnumerateKeysAsync(
        string key,
        KeyQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IKeyCursor, string, string>(
            () => StateInterop.RunSync(() => _handle.Keys(key, native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            static item => item,
            cancellationToken
        );
    }

    /// <summary>Enumerates entries in key order.</summary>
    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the entries that <paramref name="query"/> selects.</summary>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        string key,
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(key, query, item => StateInterop.JsonMapEntry(item, _typeInfo), cancellationToken);

    /// <summary>Enumerates values in key order.</summary>
    public IAsyncEnumerable<TValue> EnumerateValuesAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateValuesAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the values of the entries that <paramref name="query"/> selects.</summary>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<TValue> EnumerateValuesAsync(
        string key,
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(key, query, item => StateInterop.DeserializeJson(item.Bytes, _typeInfo), cancellationToken);

    private StateScanSequence<Native.IJsonMapCursor, Native.JsonMapEntry, TItem> Entries<TItem>(
        string key,
        KeyQuery query,
        Func<Native.JsonMapEntry, TItem> transform,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IJsonMapCursor, Native.JsonMapEntry, TItem>(
            () => StateInterop.RunSync(() => _handle.Entries(key, native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            transform,
            cancellationToken
        );
    }
}
