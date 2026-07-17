using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Prosody.State;

/// <summary>
/// JSON-flavoured deque state handle backed by a native deque handle.
/// </summary>
/// <typeparam name="T">The stored element type.</typeparam>
internal sealed class DequeState<T> : IDequeState<T>
{
    private readonly Native.IDequeStateHandle _handle;
    private readonly JsonTypeInfo<T> _typeInfo;

    internal DequeState(Native.IDequeStateHandle handle, JsonTypeInfo<T> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task PushBackAsync(T value, CancellationToken cancellationToken = default)
    {
        var bytes = StateInterop.SerializeJsonOrThrowNull(value, _typeInfo, "A deque stores only concrete values.");
        return StateInterop.RunAsync(
            () => _handle.PushBackJson(bytes, StateInterop.CreateCarrier()),
            cancellationToken
        );
    }

    public Task PushFrontAsync(T value, CancellationToken cancellationToken = default)
    {
        var bytes = StateInterop.SerializeJsonOrThrowNull(value, _typeInfo, "A deque stores only concrete values.");
        return StateInterop.RunAsync(
            () => _handle.PushFrontJson(bytes, StateInterop.CreateCarrier()),
            cancellationToken
        );
    }

    public Task<StateValue<T>> PopFrontAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.PopFront(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task<StateValue<T>> PopBackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.PopBack(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task<StateValue<T>> GetAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0)
        {
            throw new TransientStateException($"Deque index must be non-negative, got {index}.");
        }

        return StateInterop.RunAsync(
            async () =>
                StateInterop.JsonToValue(
                    await _handle.Get((ulong)index, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    public Task<long> CountAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () => (long)await _handle.Len(StateInterop.CreateCarrier()).ConfigureAwait(false),
            cancellationToken
        );

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.IsEmpty(StateInterop.CreateCarrier()), cancellationToken);

    public IAsyncEnumerable<T> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursor = StateInterop.RunSync(() =>
            _handle.Scan(StateInterop.ToNative(direction), StateInterop.CreateCarrier())
        );
        return new StateScanSequence<T>(cursor, Transform, cancellationToken);
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);

    private T Transform(Native.StateScanItem item) =>
        item is Native.StateScanItem.DequeJson element
            ? JsonSerializer.Deserialize(element.Bytes.AsSpan(), _typeInfo)!
            : throw new TransientStateException("State scan item shape mismatch: expected a JSON deque element.");
}
