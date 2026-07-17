namespace Prosody.State;

/// <summary>
/// A typed handle over a string-keyed ordered-map keyed-state collection, bound for the current
/// handler invocation. Map keys are always <see cref="string"/>.
/// </summary>
/// <remarks>
/// The handle is directly enumerable: <c>await foreach (var (key, value) in map)</c> iterates the
/// live entries forward, in key order — equivalent to <see cref="EnumerateAsync"/> with
/// <see cref="ScanDirection.Forward"/>. Each enumeration opens a fresh cursor.
/// </remarks>
/// <typeparam name="TValue">
/// The stored value type. Constrained to <c>notnull</c>: JSON <see langword="null"/> is not a
/// storable value.
/// </typeparam>
public interface IMapState<TValue> : IAsyncEnumerable<KeyValuePair<string, TValue>>
    where TValue : notnull
{
    /// <summary>Reads the value for <paramref name="key"/>.</summary>
    /// <param name="key">The map key.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>The value, or an absent <see cref="StateValue{T}"/> when the key is absent.</returns>
    Task<StateValue<TValue>> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads several keys in a single isolated batch. The result is positional:
    /// <c>result[i]</c> answers <c>keys[i]</c>, an absent key reads as an absent
    /// <see cref="StateValue{T}"/>, and a repeated key is answered at each position.
    /// </summary>
    /// <param name="keys">The keys to read. Enumerated once, synchronously, before the batch dispatches.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>
    /// One result per requested key, in the requested order: the count equals the number of keys,
    /// <c>result[i]</c> answers the i-th key, and an empty input yields an empty (non-null) list.
    /// </returns>
    Task<IReadOnlyList<StateValue<TValue>>> GetManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Inserts or overwrites <paramref name="key"/>. Writing <see langword="null"/> is a caller
    /// mistake rejected with a <see cref="NullValueException"/> (transient) — use
    /// <see cref="RemoveAsync"/> to delete an entry.
    /// </summary>
    /// <param name="key">The map key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the write is buffered.</returns>
    Task SetAsync(string key, TValue value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// Deliberate convention: this returns nothing, not a "was present" flag — surfacing that
    /// boolean would force a hidden read on every remove.
    /// </remarks>
    /// <param name="key">The map key.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the removal is buffered.</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes every entry.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the clear is buffered.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates the live entries in key order. Valid only within the handler invocation that
    /// opened it; early exit closes the underlying cursor.
    /// </summary>
    /// <param name="direction">The scan direction. Defaults to <see cref="ScanDirection.Forward"/>.</param>
    /// <param name="cancellationToken">A token observed at entry and between chunk pulls.</param>
    /// <returns>An async sequence of key/value pairs in the requested order.</returns>
    IAsyncEnumerable<KeyValuePair<string, TValue>> EnumerateAsync(
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
