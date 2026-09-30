using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>Read-only access to a published set collection.</summary>
public sealed class PublishedSet
{
    private readonly Native.IPublishedSetHandle _handle;

    internal PublishedSet(Native.IPublishedSetHandle handle) => _handle = handle;

    /// <summary>Determines whether the set for a user key contains <paramref name="member"/>.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="member">The member to test.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns><see langword="true"/> when the set contains <paramref name="member"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="TransientStateException">The read failed, and a retry can succeed.</exception>
    /// <exception cref="PermanentStateException">The read cannot succeed, for example after an identity mismatch.</exception>
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
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="members">The members to test. The call enumerates them once, before it dispatches.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>One result for each requested item, in the requested order.</returns>
    /// <inheritdoc cref="ContainsAsync(string, string, CancellationToken)" path="/exception"/>
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
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns><see langword="true"/> when the set for <paramref name="key"/> is empty.</returns>
    /// <inheritdoc cref="ContainsAsync(string, string, CancellationToken)" path="/exception"/>
    public Task<bool> IsEmptyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.RunAsync(() => _handle.IsEmpty(key, StateInterop.CreateCarrier()), cancellationToken);
    }

    /// <summary>Enumerates members in member order.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="direction">The scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="ContainsAsync(string, string, CancellationToken)" path="/exception"/>
    public IAsyncEnumerable<string> EnumerateAsync(
        string key,
        ScanDirection direction = ScanDirection.Forward,
        CancellationToken cancellationToken = default
    ) => EnumerateAsync(key, new KeyQuery { Direction = direction }, cancellationToken);

    /// <summary>Enumerates the members that <paramref name="query"/> selects.</summary>
    /// <param name="key">The user key that owns the collection.</param>
    /// <param name="query">The keys or positions to select, and the scan order.</param>
    /// <param name="cancellationToken">A token to observe before the operation dispatches.</param>
    /// <returns>A sequence that opens a new cursor for each enumeration.</returns>
    /// <inheritdoc cref="ContainsAsync(string, string, CancellationToken)" path="/exception"/>
    /// <exception cref="ArgumentException">The query sets both edges of an inclusive and exclusive pair.</exception>
    public IAsyncEnumerable<string> EnumerateAsync(
        string key,
        KeyQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        return StateInterop.Keys(native => _handle.Keys(key, native), query, cancellationToken);
    }
}
