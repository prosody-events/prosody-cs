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
        return StateInterop.RunAsync(() => _handle.Insert(member, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task RemoveAsync(string member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return StateInterop.RunAsync(() => _handle.Remove(member, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task<bool> ContainsAsync(string member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return StateInterop.RunAsync(() => _handle.Contains(member, StateInterop.CreateCarrier()), cancellationToken);
    }

    public Task<IReadOnlyList<bool>> ContainsManyAsync(
        IEnumerable<string> members,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(members);
        var memberArray = members as string[] ?? [.. members];
        return StateInterop.RunAsync<IReadOnlyList<bool>>(
            async () => await _handle.ContainsMany(memberArray, StateInterop.CreateCarrier()).ConfigureAwait(false),
            cancellationToken
        );
    }

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.IsEmpty(StateInterop.CreateCarrier()), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunAsync(() => _handle.Clear(StateInterop.CreateCarrier()), cancellationToken);

    public IAsyncEnumerable<string> EnumerateAsync(
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(new KeyQuery { Direction = direction }, cancellationToken);

    public IAsyncEnumerable<string> EnumerateAsync(KeyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IKeyCursor, string, string>(
            () => StateInterop.RunSync(() => _handle.Keys(native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            static member => member,
            cancellationToken
        );
    }

    public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(ScanDirection.Forward, cancellationToken).GetAsyncEnumerator(cancellationToken);

    public Task<StoreOutcome> CommitAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Commit, cancellationToken);

    public Task<StoreOutcome> RollbackAsync(CancellationToken cancellationToken = default) =>
        StateInterop.RunOutcomeAsync(_handle.Rollback, cancellationToken);
}
