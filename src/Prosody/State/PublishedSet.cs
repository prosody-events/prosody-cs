namespace Prosody.State;

/// <summary>Read-only access to a published set collection.</summary>
public sealed class PublishedSet
{
    private readonly Native.IPublishedSetHandle _handle;

    internal PublishedSet(Native.IPublishedSetHandle handle) => _handle = handle;

    /// <summary>Determines whether the set for a user key contains <paramref name="member"/>.</summary>
    public Task<bool> ContainsAsync(string key, string member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);
        return StateInterop.RunAsync(
            () => _handle.Contains(key, member, StateInterop.CreateCarrier()),
            cancellationToken
        );
    }

    /// <summary>Tests several members for a user key in one batch. <c>result[i]</c> answers the i-th member.</summary>
    public Task<IReadOnlyList<bool>> ContainsManyAsync(
        string key,
        IEnumerable<string> members,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(members);
        var memberArray = members as string[] ?? [.. members];
        return StateInterop.RunAsync<IReadOnlyList<bool>>(
            async () =>
                await _handle.ContainsMany(key, memberArray, StateInterop.CreateCarrier()).ConfigureAwait(false),
            cancellationToken
        );
    }

    /// <summary>Determines whether the set for a user key is empty.</summary>
    public Task<bool> IsEmptyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(() => _handle.IsEmpty(key, StateInterop.CreateCarrier()), cancellationToken);
    }

    /// <summary>Enumerates members in member order.</summary>
    public IAsyncEnumerable<string> EnumerateAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the members that <paramref name="query"/> selects.</summary>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<string> EnumerateAsync(
        string key,
        KeyQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var native = KeyQuery.ToNative(query);
        return new StateScanSequence<Native.IKeyCursor, string, string>(
            () => StateInterop.RunSync(() => _handle.Keys(key, native)),
            static (cursor, carrier) => cursor.NextChunk(carrier),
            static cursor => cursor.Close(),
            static member => member,
            cancellationToken
        );
    }
}
