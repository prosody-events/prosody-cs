namespace Prosody;

/// <summary>
/// Represents a timer trigger event.
/// </summary>
public sealed class Trigger
{
    /// <summary>
    /// Gets the key associated with this timer.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the scheduled time of the timer.
    /// </summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>
    /// Gets the type of timer that triggered.
    /// </summary>
    public required TimerType TimerType { get; init; }
}
