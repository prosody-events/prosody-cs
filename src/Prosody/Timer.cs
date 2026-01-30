namespace Prosody;

/// <summary>
/// Timer trigger data.
/// </summary>
/// <remarks>
/// Wraps the native timer and exposes timer data via properties.
/// Properties are pass-through to native (no caching) - each access makes an FFI call.
/// </remarks>
public sealed class Timer
{
    private readonly Native.Timer _native;

    internal Timer(Native.Timer native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
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
