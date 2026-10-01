using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>
/// JSON-flavoured deque state handle backed by a native deque handle.
/// </summary>
/// <typeparam name="T">The stored element type.</typeparam>
internal sealed class DequeState<T> : IDequeState<T>
    where T : notnull
{
    private readonly Native.IJsonDequeStateHandle _handle;
    private readonly JsonTypeInfo<T> _typeInfo;

    internal DequeState(Native.IJsonDequeStateHandle handle, JsonTypeInfo<T> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task PushBackAsync(T value, CancellationToken cancellationToken = default)
    {
        var bytes = StateInterop.SerializeJson(value, _typeInfo);
        return StateInterop.RunAsync(carrier => _handle.PushBack(bytes, carrier), cancellationToken);
    }

    public Task PushFrontAsync(T value, CancellationToken cancellationToken = default)
    {
        var bytes = StateInterop.SerializeJson(value, _typeInfo);
        return StateInterop.RunAsync(carrier => _handle.PushFront(bytes, carrier), cancellationToken);
    }

    public Task<StateValue<T>> PopFrontAsync(CancellationToken cancellationToken = default) =>
        StateInterop.ReadJsonAsync(_handle.PopFront, _typeInfo, cancellationToken);

    public Task<StateValue<T>> PopBackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.ReadJsonAsync(_handle.PopBack, _typeInfo, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.Clear(carrier), cancellationToken);

    public Task<StateValue<T>> PeekFrontAsync(CancellationToken cancellationToken = default) =>
        StateInterop.ReadJsonAsync(_handle.PeekFront, _typeInfo, cancellationToken);

    public Task<StateValue<T>> PeekBackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.ReadJsonAsync(_handle.PeekBack, _typeInfo, cancellationToken);

    public Task<StateValue<T>> GetAsync(int index, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return StateInterop.ReadJsonAsync(carrier => _handle.Get((ulong)index, carrier), _typeInfo, cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async carrier => checked((int)await _handle.Len(carrier).ConfigureAwait(false)),
            cancellationToken
        );

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.IsEmpty(carrier), cancellationToken);

    public IAsyncEnumerable<T> EnumerateAsync(PositionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = PositionQuery.ToNative(query);
        return new StateScanSequence<Native.IJsonDequeCursor, byte[], T>(
            () => NativeErrors.Run(() => _handle.Values(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            bytes => StateInterop.DeserializeJson(bytes, _typeInfo),
            cancellationToken
        );
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(new PositionQuery(), cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task<StoreOutcome> CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Commit, cancellationToken);

    public Task<StoreOutcome> RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Rollback, cancellationToken);
}
