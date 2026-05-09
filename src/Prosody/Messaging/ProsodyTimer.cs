namespace Prosody.Messaging;

/// <summary>
/// Timer trigger data.
/// </summary>
public sealed class ProsodyTimer
{
    private readonly Native.Timer _native;

    internal ProsodyTimer(Native.Timer native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;

        Key = native.Key();
        Time = new(native.Time(), TimeSpan.Zero);
    }

    /// <summary>Creates a timer instance for unit tests without a native backing object.</summary>
    internal ProsodyTimer(string key, DateTimeOffset time)
    {
        _native = null!;
        Key = key;
        Time = time;
    }

    /// <summary>
    /// Gets the timer key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the timer fire time (UTC).
    /// </summary>
    public DateTimeOffset Time { get; }
}
