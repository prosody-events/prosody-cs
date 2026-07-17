namespace Prosody.State;

/// <summary>
/// A typed handle over a single-value keyed-state collection, bound for the current handler
/// invocation. Operations are buffered against the current attempt and become durable at
/// <see cref="CommitAsync"/> or at the end of a successful handler.
/// </summary>
/// <typeparam name="T">The stored value type.</typeparam>
public interface IValueState<T>
{
    /// <summary>Reads the current value.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>The stored value, or an absent <see cref="StateValue{T}"/> when none is present.</returns>
    Task<StateValue<T>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Buffers a write of the value. Writing <see langword="null"/> is a caller mistake rejected
    /// with a <see cref="NullValueException"/> (transient) — use <see cref="ClearAsync"/> to delete.
    /// </summary>
    /// <param name="value">The value to store.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the write is buffered.</returns>
    Task SetAsync(T value, CancellationToken cancellationToken = default);

    /// <summary>Deletes the stored value.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the clear is buffered.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Durably commits the buffered operations mid-handler (at-least-once; the committed floor
    /// survives a later rollback or a failed event). Returns no value — the erased seam drops the
    /// applied/no-op outcome.
    /// </summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the commit is durable.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Discards buffered uncommitted operations back to the last committed floor.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the rollback is applied.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
