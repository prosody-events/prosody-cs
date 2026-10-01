using System.Text.Json.Serialization.Metadata;
using Prosody.Infrastructure;
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
        return StateInterop.RunAsync(carrier => _handle.PushBack(native, carrier), cancellationToken);
    }

    public Task PushFrontAsync(Message<TPayload> value, CancellationToken cancellationToken = default)
    {
        var native = MessageInterop.ToNative(value);
        return StateInterop.RunAsync(carrier => _handle.PushFront(native, carrier), cancellationToken);
    }

    public Task<StateValue<Message<TPayload>>> PopFrontAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async carrier =>
                MessageInterop.MessageToValue(await _handle.PopFront(carrier).ConfigureAwait(false), _typeInfo),
            cancellationToken
        );

    public Task<StateValue<Message<TPayload>>> PopBackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async carrier =>
                MessageInterop.MessageToValue(await _handle.PopBack(carrier).ConfigureAwait(false), _typeInfo),
            cancellationToken
        );

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.Clear(carrier), cancellationToken);

    public Task<StateValue<Message<TPayload>>> PeekFrontAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async carrier =>
                MessageInterop.MessageToValue(await _handle.PeekFront(carrier).ConfigureAwait(false), _typeInfo),
            cancellationToken
        );

    public Task<StateValue<Message<TPayload>>> PeekBackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async carrier =>
                MessageInterop.MessageToValue(await _handle.PeekBack(carrier).ConfigureAwait(false), _typeInfo),
            cancellationToken
        );

    public Task<StateValue<Message<TPayload>>> GetAsync(int index, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return StateInterop.RunAsync(
            async carrier =>
                MessageInterop.MessageToValue(
                    await _handle.Get((ulong)index, carrier).ConfigureAwait(false),
                    _typeInfo
                ),
            cancellationToken
        );
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(
            async carrier => checked((int)await _handle.Len(carrier).ConfigureAwait(false)),
            cancellationToken
        );

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.IsEmpty(carrier), cancellationToken);

    public IAsyncEnumerable<Message<TPayload>> EnumerateAsync(
        PositionQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = PositionQuery.ToNative(query);
        return new StateScanSequence<Native.IMessageDequeCursor, Native.Message, Message<TPayload>>(
            () => NativeErrors.Run(() => _handle.Values(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            message => MessageInterop.FromNative(message, _typeInfo),
            cancellationToken
        );
    }

    public IAsyncEnumerator<Message<TPayload>> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(new PositionQuery(), cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task<StoreOutcome> CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Commit, cancellationToken);

    public Task<StoreOutcome> RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Rollback, cancellationToken);
}
