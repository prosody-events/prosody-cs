using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;

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
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="mapKey">The map key.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>The value, or an absent <see cref="StateValue{T}"/> when none is stored.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="TransientStateException">The read failed, and a retry can succeed.</exception>
    /// <exception cref="PermanentStateException">The read cannot succeed, for example after an identity mismatch.</exception>
    public Task<StateValue<TValue>> GetAsync(string key, string mapKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mapKey);
        return StateInterop.ReadJsonAsync(carrier => _handle.Get(key, mapKey, carrier), _typeInfo, cancellationToken);
    }

    /// <summary>Reads several entries for a user key in one batch.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="mapKeys">The map keys to read. The call enumerates them once, before it dispatches.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>One result for each map key, in the requested order.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
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
            async carrier =>
            {
                var items = await _handle.GetMany(key, keys, carrier).ConfigureAwait(false);
                return Array.ConvertAll(items, item => StateInterop.JsonToValue(item.Bytes, _typeInfo));
            },
            cancellationToken
        );
    }

    /// <summary>Reports whether one entry exists for a user key.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="mapKey">The map key.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns><see langword="true"/> when an entry exists for <paramref name="mapKey"/>.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
    public Task<bool> ContainsKeyAsync(string key, string mapKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mapKey);
        return StateInterop.RunAsync(carrier => _handle.ContainsKey(key, mapKey, carrier), cancellationToken);
    }

    /// <summary>Tests several entries for a user key in one batch. <c>result[i]</c> answers the i-th map key.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="mapKeys">The map keys to read. The call enumerates them once, before it dispatches.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>One result for each requested item, in the requested order.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
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
            async carrier => await _handle.ContainsMany(key, keys, carrier).ConfigureAwait(false),
            cancellationToken
        );
    }

    /// <summary>Determines whether the map for a user key is empty.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns><see langword="true"/> when the map for <paramref name="key"/> is empty.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
    public Task<bool> IsEmptyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(carrier => _handle.IsEmpty(key, carrier), cancellationToken);
    }

    /// <summary>Enumerates keys without reading values.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="direction">The scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
    public IAsyncEnumerable<string> EnumerateKeysAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateKeysAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the keys that <paramref name="query"/> selects without reading values.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="query">The keys or positions to select, and the scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<string> EnumerateKeysAsync(
        string key,
        KeyQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.Keys(native => _handle.Keys(key, native), query, cancellationToken);
    }

    /// <summary>Enumerates entries in key order.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="direction">The scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the entries that <paramref name="query"/> selects.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="query">The keys or positions to select, and the scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        string key,
        KeyQuery query,
        CancellationToken cancellationToken = default
    ) => Entries(key, query, item => StateInterop.JsonMapEntry(item, _typeInfo), cancellationToken);

    /// <summary>Enumerates values in key order.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="direction">The scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
    public IAsyncEnumerable<TValue> EnumerateValuesAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateValuesAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the values of the entries that <paramref name="query"/> selects.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="query">The keys or positions to select, and the scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="GetAsync(string, string, CancellationToken)" path="/exception"/>
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
            () => NativeErrors.Run(() => _handle.Entries(key, native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            transform,
            cancellationToken
        );
    }
}
