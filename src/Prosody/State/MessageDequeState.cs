using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.State;

/// <summary>
/// Message-flavoured deque state handle backed by a native deque handle.
/// </summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
internal sealed class MessageDequeState<TPayload> : IDequeState<Message<TPayload>>
{
    private readonly Native.IMessageDequeStateHandle _handle;
    private readonly JsonTypeInfo<TPayload> _typeInfo;

    internal MessageDequeState(Native.IMessageDequeStateHandle handle, JsonTypeInfo<TPayload> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task PushBackAsync(Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(() => _handle.PushBack(native, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task PushFrontAsync(Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(() => _handle.PushFront(native, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task<StateValue<Message<TPayload>>> PopFrontAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                MessageInterop.MessageToValue(
                    await _handle.PopFront(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task<StateValue<Message<TPayload>>> PopBackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                MessageInterop.MessageToValue(
                    await _handle.PopBack(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Clear(StateInterop.CreateCarrier()), cancellationToken);

    public Task<StateValue<Message<TPayload>>> PeekFrontAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                MessageInterop.MessageToValue(
                    await _handle.PeekFront(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task<StateValue<Message<TPayload>>> PeekBackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () =>
                MessageInterop.MessageToValue(
                    await _handle.PeekBack(StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );

    public Task<StateValue<Message<TPayload>>> GetAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0)
        {
            throw new TransientStateException($"Deque index must be non-negative, got {index}.");
        }

        return StateInterop.RunAsync(
            async () =>
                MessageInterop.MessageToValue(
                    await _handle.Get((ulong)index, StateInterop.CreateCarrier()).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async () => checked((int)await _handle.Len(StateInterop.CreateCarrier()).ConfigureAwait(false)),
            cancellationToken
        );

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.IsEmpty(StateInterop.CreateCarrier()), cancellationToken);

    public IAsyncEnumerable<Message<TPayload>> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new StateScanSequence<Native.IMessageDequeCursor, Native.Message, Message<TPayload>>(
            () =>
                StateInterop.RunSync(() =>
                    _handle.Scan(StateInterop.ToNative(direction), StateInterop.CreateCarrier())
                ),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            message => MessageInterop.FromNative(message, _typeInfo),
            cancellationToken
        );
    }

    public IAsyncEnumerator<Message<TPayload>> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(ScanDirection.Forward, cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);
}
