using Prosody.Infrastructure;

namespace Prosody.State;

/// <summary>Controls caching for published-state reads.</summary>
public readonly record struct StateReadCache
{
    private StateReadCache(Native.ReadCache policy) => Policy = policy;

    /// <summary>Bypasses the read cache.</summary>
    public static StateReadCache Disabled { get; } = new(new Native.ReadCache.Disabled());

    /// <summary>Creates a time-based read-cache policy.</summary>
    /// <param name="ttl">A non-negative cache duration. Prosody rejects zero.</param>
    /// <returns>A time-based read-cache policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is negative.</exception>
    public static StateReadCache For(TimeSpan ttl) => new(new Native.ReadCache.Ttl(Durations.ToNative(ttl)));

    internal Native.ReadCache Policy { get; }
}
