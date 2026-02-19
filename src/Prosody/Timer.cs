namespace Prosody;

/// <summary>
/// Timer trigger data.
/// </summary>
public sealed class Timer
{
    private readonly Native.Timer _native;

    internal Timer(Native.Timer native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    /// <summary>
    /// Gets the timer key.
    /// </summary>
    public string Key => _native.Key();

    /// <summary>
    /// Gets the timer fire time (UTC).
    /// </summary>
    public DateTimeOffset Time => new(_native.Time(), TimeSpan.Zero);
}
