using System.Text.Json.Serialization.Metadata;
using Prosody.Messaging;

namespace Prosody.State;

/// <summary>
/// Message-flavoured deque state handle backed by a native deque handle.
/// </summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
internal sealed class MessageDequeState<TPayload> : IDequeState<Message<TPayload>>
{
    private readonly Native.IDequeStateHandle _handle;
    private readonly JsonTypeInfo<TPayload> _typeInfo;

    internal MessageDequeState(Native.IDequeStateHandle handle, JsonTypeInfo<TPayload> typeInfo)
    {
        _handle = handle;
        _typeInfo = typeInfo;
    }

    public Task PushBackAsync(Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(
            () => _handle.PushBackMessage(native, StateInterop.CreateCarrier()),
            cancellationToken
        );
    }

    public Task PushFrontAsync(Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(
            () => _handle.PushFrontMessage(native, StateInterop.CreateCarrier()),
            cancellationToken
        );
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
        var cursor = StateInterop.RunSync(() =>
            _handle.Scan(StateInterop.ToNative(direction), StateInterop.CreateCarrier())
        );
        return new StateScanSequence<Message<TPayload>>(cursor, Transform, cancellationToken);
    }

    public IAsyncEnumerator<Message<TPayload>> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(ScanDirection.Forward, cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Commit(StateInterop.CreateCarrier()), cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Rollback(StateInterop.CreateCarrier()), cancellationToken);

    private Message<TPayload> Transform(Native.StateScanItem item) =>
        item is Native.StateScanItem.DequeMessage element
            ? MessageInterop.FromNative(element.Message, _typeInfo)
            : throw new TransientStateException("State scan item shape mismatch: expected a message deque element.");
}
