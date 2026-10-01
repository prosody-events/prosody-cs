using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>
/// Set state handle backed by a native set handle.
/// </summary>
internal sealed class SetState : ISetState
{
    private readonly Native.ISetStateHandle _handle;

    internal SetState(Native.ISetStateHandle handle) => _handle = handle;

    public Task AddAsync(string member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return StateInterop.RunAsync(carrier => _handle.Insert(member, carrier), cancellationToken);
    }

    public Task RemoveAsync(string member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return StateInterop.RunAsync(carrier => _handle.Remove(member, carrier), cancellationToken);
    }

    public Task<bool> ContainsAsync(string member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return StateInterop.RunAsync(carrier => _handle.Contains(member, carrier), cancellationToken);
    }

    public Task<IReadOnlyList<bool>> ContainsManyAsync(
        IEnumerable<string> members,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(members);
        var memberArray = members as string[] ?? [.. members];
        return StateInterop.RunAsync<IReadOnlyList<bool>>(
            async carrier => await _handle.ContainsMany(memberArray, carrier).ConfigureAwait(false),
            cancellationToken
        );
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.IsEmpty(carrier), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(carrier => _handle.Clear(carrier), cancellationToken);

    public IAsyncEnumerable<string> EnumerateAsync(KeyQuery query, CancellationToken cancellationToken = default) =>
        StateInterop.Keys(_handle.Keys, query, cancellationToken);

    public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(new KeyQuery(), cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task<StoreOutcome> CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Commit, cancellationToken);

    public Task<StoreOutcome> RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Rollback, cancellationToken);
}
