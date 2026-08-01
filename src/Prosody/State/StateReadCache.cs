namespace Prosody.State;

/// <summary>Controls caching for published-state reads.</summary>
public readonly record struct StateReadCache
{
    private StateReadCache(TimeSpan? ttl, bool disabled) => (Ttl, IsDisabled) = (ttl, disabled);

    /// <summary>Bypasses the read cache.</summary>
    public static StateReadCache Disabled { get; } = new(ttl: null, disabled: true);

    /// <summary>Creates a time-based read-cache policy.</summary>
    /// <param name="ttl">A non-negative cache duration. Prosody rejects zero.</param>
    /// <returns>A time-based read-cache policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is negative.</exception>
    public static StateReadCache For(TimeSpan ttl)
    {
        if (ttl < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "Read cache TTL must not be negative.");
        }

        return new StateReadCache(ttl, disabled: false);
    }

    internal TimeSpan? Ttl { get; }

    internal bool IsDisabled { get; }
}
