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

    /// <summary>
    /// Gets the timer key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the timer fire time (UTC).
    /// </summary>
    public DateTimeOffset Time { get; }
}
