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
                return Array.ConvertAll(items, item => StateInterop.JsonToValue(item, _typeInfo));
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

    /// <summary>Enumerates keys using the shared key-only chunked cursor.</summary>
    public IAsyncEnumerable<string> EnumerateKeysAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        return new StateScanSequence<string>(
            () =>
                StateInterop.RunAsync<Native.IStateCursor>(
                    async () =>
                        await _handle
                            .Keys(key, StateInterop.ToNative(direction), StateInterop.CreateCarrier())
                            .ConfigureAwait(false),
                    cancellationToken
                ),
            StateInterop.ItemKey,
            cancellationToken
        );
    }

    /// <summary>Enumerates entries using the shared chunked state cursor.</summary>
    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        return new StateScanSequence<KeyValuePair<string, TValue>>(
            () =>
                StateInterop.RunAsync<Native.IStateCursor>(
                    async () =>
                        await _handle
                            .Scan(key, StateInterop.ToNative(direction), StateInterop.CreateCarrier())
                            .ConfigureAwait(false),
                    cancellationToken
                ),
            item =>
                item is Native.StateScanItem.MapJson entry
                    ? KeyValuePair.Create(entry.Key, StateInterop.DeserializeJson(entry.Bytes, _typeInfo))
                    : throw new TransientStateException("State scan item shape mismatch: expected a JSON map entry."),
            cancellationToken
        );
    }
}
