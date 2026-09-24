namespace Prosody.State;

/// <summary>
/// A handle over a set keyed-state collection, bound for the current handler invocation. A set
/// stores ordered <see cref="string"/> members and no values.
/// </summary>
/// <remarks>
/// The handle is directly enumerable: <c>await foreach (var member in set)</c> iterates the
/// members forward, in member order. Each enumeration opens a fresh cursor.
/// </remarks>
public interface ISetState : IAsyncEnumerable<string>
{
    /// <summary>Adds <paramref name="member"/> to the set.</summary>
    /// <param name="member">The member to add.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the write is buffered.</returns>
    Task AddAsync(string member, CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="member"/> from the set. An absent member is a no-op.</summary>
    /// <param name="member">The member to remove.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the removal is buffered.</returns>
    Task RemoveAsync(string member, CancellationToken cancellationToken = default);

    /// <summary>Determines whether <paramref name="member"/> is in the set.</summary>
    /// <param name="member">The member to test.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns><see langword="true"/> when the set contains the member.</returns>
    Task<bool> ContainsAsync(string member, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests several members in one batch. <c>result[i]</c> answers the i-th member.
    /// </summary>
    /// <param name="members">The members to test. Enumerated once, before the batch dispatches.</param>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>One result per requested member, in the requested order.</returns>
    Task<IReadOnlyList<bool>> ContainsManyAsync(
        IEnumerable<string> members,
        CancellationToken cancellationToken = default
    );

    /// <summary>Determines whether the set has no members.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns><see langword="true"/> when the set is empty.</returns>
    Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes every member.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>A task that completes when the clear is buffered.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates the members in member order. Valid only within the handler invocation that opened
    /// it. Early exit closes the underlying cursor.
    /// </summary>
    /// <param name="direction">The scan direction. Defaults to <see cref="ScanDirection.Forward"/>.</param>
    /// <param name="cancellationToken">A token observed at entry and between chunk pulls.</param>
    /// <returns>An async sequence of members in the requested order.</returns>
    IAsyncEnumerable<string> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Enumerates the members that <paramref name="query"/> selects. Valid only within the handler
    /// invocation that opened it. Early exit closes the underlying cursor.
    /// </summary>
    /// <param name="query">The members to select and their order.</param>
    /// <param name="cancellationToken">A token observed at entry and between chunk pulls.</param>
    /// <returns>An async sequence of the selected members.</returns>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    IAsyncEnumerable<string> EnumerateAsync(KeyQuery query, CancellationToken cancellationToken = default);

    /// <summary>Durably commits the buffered operations mid-handler.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>
    /// <see cref="StoreOutcome.Applied"/> when buffered operations were written, or
    /// <see cref="StoreOutcome.NoOp"/> when nothing was buffered.
    /// </returns>
    Task<StoreOutcome> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Discards buffered uncommitted operations back to the last committed floor.</summary>
    /// <param name="cancellationToken">A token to observe before dispatching the operation.</param>
    /// <returns>
    /// <see cref="StoreOutcome.Applied"/> when buffered operations were discarded, or
    /// <see cref="StoreOutcome.NoOp"/> when nothing was buffered.
    /// </returns>
    Task<StoreOutcome> RollbackAsync(CancellationToken cancellationToken = default);
}
