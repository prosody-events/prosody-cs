namespace Prosody.State;

/// <summary>
/// A typed handle over a deque keyed-state collection, bound for the current handler invocation.
/// </summary>
/// <remarks>
/// The handle is directly enumerable: <c>await foreach (var element in deque)</c> iterates the live
/// elements front-to-back — equivalent to <see cref="EnumerateAsync"/> with
/// <see cref="ScanDirection.Forward"/>. Each enumeration opens a fresh cursor.
/// </remarks>
/// <typeparam name="T">
/// The stored element type. Constrained to <c>notnull</c>: JSON <see langword="null"/> is not a
/// storable value.
/// </typeparam>
public interface IDequeState<T> : IAsyncEnumerable<T>
    where T : notnull
{
    /// <summary>
    /// Appends an element at the back. Writing <see langword="null"/> is a caller mistake rejected
    /// with a <see cref="NullValueException"/> (transient); a deque stores only concrete values.
    /// </summary>
    /// <param name="value">The element to append.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the write is buffered.</returns>
    Task PushBackAsync(T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepends an element at the front. Writing <see langword="null"/> is a caller mistake rejected
    /// with a <see cref="NullValueException"/> (transient); a deque stores only concrete values.
    /// </summary>
    /// <param name="value">The element to prepend.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the write is buffered.</returns>
    Task PushFrontAsync(T value, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns the front element.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>The removed element, or an absent <see cref="StateValue{T}"/> when the deque is empty.</returns>
    Task<StateValue<T>> PopFrontAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes and returns the back element.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>The removed element, or an absent <see cref="StateValue{T}"/> when the deque is empty.</returns>
    Task<StateValue<T>> PopBackAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the element at <paramref name="index"/> (front-relative, zero-based).</summary>
    /// <param name="index">The zero-based index from the front. A negative index is a caller mistake (transient).</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>The element, or an absent <see cref="StateValue{T}"/> when the index is out of range.</returns>
    Task<StateValue<T>> GetAsync(int index, CancellationToken cancellationToken = default);

    /// <summary>Counts the elements.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>The number of elements.</returns>
    /// <exception cref="OverflowException">
    /// Thrown in the practically-unreachable case that the deque holds more than
    /// <see cref="int.MaxValue"/> elements.
    /// </exception>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Determines whether the deque is empty.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns><see langword="true"/> when the deque has no elements.</returns>
    Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates the live elements in index order. Valid only within the handler invocation that
    /// opened it; early exit closes the underlying cursor.
    /// </summary>
    /// <param name="direction">The scan direction. Defaults to <see cref="ScanDirection.Forward"/>.</param>
    /// <param name="cancellationToken">A token observed at entry and between chunk pulls.</param>
    /// <returns>An async sequence of elements in the requested order.</returns>
    IAsyncEnumerable<T> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Durably commits the buffered operations mid-handler. Returns no value — the erased seam
    /// drops the applied/no-op outcome.
    /// </summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the commit is durable.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Discards buffered uncommitted operations back to the last committed floor.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the rollback is applied.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
