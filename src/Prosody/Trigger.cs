namespace Prosody;

/// <summary>
/// Represents a timer trigger event.
/// </summary>
/// <remarks>
/// <para>
/// Only the key and scheduled time cross the FFI boundary. Timer type filtering
/// (Application vs DeferredMessage vs DeferredTimer) is handled on the Rust side
/// before events reach C# handlers.
/// </para>
/// <para>
/// This type matches the sibling wrapper APIs:
/// <list type="bullet">
/// <item>JavaScript: prosody-js/bindings.d.ts Timer</item>
/// <item>Python: prosody-py/python/prosody/timer.py Timer</item>
/// <item>Ruby: prosody-rb/lib/prosody/native_stubs.rb Timer</item>
/// </list>
/// </para>
/// </remarks>
public sealed class Trigger
{
    /// <summary>
    /// Gets the key associated with this timer.
    /// </summary>
    /// <remarks>
    /// This matches the key used when the timer was scheduled via
    /// <see cref="IEventContext.ScheduleAsync(DateTimeOffset)"/>.
    /// </remarks>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the scheduled time of the timer.
    /// </summary>
    /// <remarks>
    /// This is the time that was originally requested when scheduling the timer.
    /// Due to timer resolution and processing latency, the actual firing time
    /// may differ slightly.
    /// </remarks>
    public required DateTimeOffset Time { get; init; }
}
