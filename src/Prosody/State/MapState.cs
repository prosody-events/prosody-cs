using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Prosody.State;

/// <summary>
/// JSON-flavoured ordered-map state handle backed by a native map handle.
/// </summary>
/// <typeparam name="TValue">The stored value type.</typeparam>
internal sealed class MapState<TValue> : IMapState<TValue>
{
    private readonly Native.IMapStateHandle _handle;
    private readonly JsonTypeInfo<TValue> _typeInfo;

    internal MapState(Native.IMapStateHandle handle, JsonTypeInfo<TValue> typeInfo)
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
        IReadOnlyList<string> keys,
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
                    results[i] = StateInterop.JsonToValue(items[i], _typeInfo);
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
        return StateInterop.RunAsync(
            () => _handle.SetJson(key, bytes, StateInterop.CreateCarrier()),
            cancellationToken
        );
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(() => _handle.Remove(key, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Clear(StateInterop.CreateCarrier()), cancellationToken);

    public IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursor = StateInterop.RunSync(() =>
            _handle.Scan(StateInterop.ToNative(direction), StateInterop.CreateCarrier())
        );
        return new StateScanSequence<KeyValuePair<string, TValue>>(cursor, Transform, cancellationToken);
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);

    private KeyValuePair<string, TValue> Transform(Native.StateScanItem item) =>
        item is Native.StateScanItem.MapJson entry
            ? KeyValuePair.Create(entry.Key, JsonSerializer.Deserialize(entry.Bytes.AsSpan(), _typeInfo)!)
            : throw new TransientStateException("State scan item shape mismatch: expected a JSON map entry.");
}
