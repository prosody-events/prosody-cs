using System.Text.Json.Serialization.Metadata;

namespace Prosody.State;

/// <summary>Read-only access to a published deque collection.</summary>
public sealed class PublishedDeque<T>
    where T : notnull
{
    private readonly Native.IPublishedDequeHandle _handle;
    private readonly JsonTypeInfo<T> _typeInfo;

    internal PublishedDeque(Native.IPublishedDequeHandle handle, JsonTypeInfo<T> typeInfo) =>
        (_handle, _typeInfo) = (handle, typeInfo);

    /// <summary>Reads one element for a user key.</summary>
    public Task<StateValue<T>> GetAsync(string key, int index, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (index < 0)
        {
            throw new TransientStateException($"Deque index must be non-negative, got {index}.");
        }
        return StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.Get(key, (ulong)index, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    /// <summary>Counts the elements for a user key.</summary>
    public Task<int> CountAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(
            async () => checked((int)await _handle.Len(key, StateInterop.CreateCarrier()).ConfigureAwait(false)),
            cancellationToken
        );
    }

    /// <summary>Determines whether the deque for a user key is empty.</summary>
    public Task<bool> IsEmptyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(() => _handle.IsEmpty(key, StateInterop.CreateCarrier()), cancellationToken);
    }

    /// <summary>Reads the front element for a user key without removing it.</summary>
    public Task<StateValue<T>> PeekFrontAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.PeekFront(key, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    /// <summary>Reads the back element for a user key without removing it.</summary>
    public Task<StateValue<T>> PeekBackAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.PeekBack(key, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    /// <summary>Enumerates elements in position order.</summary>
    public IAsyncEnumerable<T> EnumerateAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(key, new PositionQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the elements that <paramref name="query"/> selects.</summary>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<T> EnumerateAsync(
        string key,
        PositionQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = PositionQuery.ToNative(query);
        return new StateScanSequence<Native.IJsonDequeCursor, byte[], T>(
            () => StateInterop.RunSync(() => _handle.Values(key, native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            bytes => StateInterop.DeserializeJson(bytes, _typeInfo),
            cancellationToken
        );
    }
}
