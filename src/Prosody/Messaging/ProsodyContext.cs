using Prosody.Infrastructure;

namespace Prosody.Messaging;

/// <summary>
/// Event context for scheduling timers and checking cancellation. All times are in UTC.
/// </summary>
public sealed class ProsodyContext
{
    private readonly Native.Context _native;

    internal ProsodyContext(Native.Context native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    /// <summary>
    /// Gets a value indicating whether cancellation has been requested.
    /// </summary>
    public bool ShouldCancel => _native.ShouldCancel();

    /// <summary>
    /// Returns a task that completes when cancellation is requested.
    /// </summary>
    public Task OnCancelAsync() => _native.OnCancel();

    /// <summary>
    /// Schedule a new timer at the given time for the current message key.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task ScheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.Schedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule all existing timers, then schedule exactly one new timer.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task ClearAndScheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.ClearAndSchedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule a specific timer at the given time.
    /// </summary>
    /// <param name="time">The time of the timer to unschedule (UTC).</param>
    public Task UnscheduleAsync(DateTimeOffset time)
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.Unschedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule all timers for the current key.
    /// </summary>
    public Task ClearScheduledAsync()
    {
        Dictionary<string, string> carrier = CreateCarrier();
        return _native.ClearScheduled(carrier);
    }

    /// <summary>
    /// List all scheduled timer times for the current key.
    /// </summary>
    /// <returns>An array of scheduled times (UTC).</returns>
    public async Task<DateTimeOffset[]> ScheduledAsync()
    {
        Dictionary<string, string> carrier = CreateCarrier();
        DateTime[] times = await _native.Scheduled(carrier).ConfigureAwait(false);
        return Array.ConvertAll(times, t => new DateTimeOffset(t, TimeSpan.Zero));
    }

    private static Dictionary<string, string> CreateCarrier()
    {
        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);
        return carrier;
    }
}
