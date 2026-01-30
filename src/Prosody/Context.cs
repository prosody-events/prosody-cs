namespace Prosody;

/// <summary>
/// Event context for scheduling timers and checking cancellation.
/// </summary>
/// <remarks>
/// Wraps the native context and exposes scheduling and cancellation methods.
/// All times are in UTC.
/// </remarks>
public sealed class Context
{
    private readonly Native.Context _native;

    internal Context(Native.Context native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    /// <summary>
    /// Gets a value indicating whether cancellation has been requested.
    /// </summary>
    public bool ShouldCancel => _native.ShouldCancel();

    /// <summary>
    /// Returns a task that completes when cancellation is requested.
    /// </summary>
    public Task OnCancel() => _native.OnCancel();

    /// <summary>
    /// Schedule a new timer at the given time for the current message key.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task Schedule(DateTimeOffset time)
    {
        var carrier = CreateCarrier();
        return _native.Schedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule all existing timers, then schedule exactly one new timer.
    /// </summary>
    /// <param name="time">The time to schedule the timer (UTC).</param>
    public Task ClearAndSchedule(DateTimeOffset time)
    {
        var carrier = CreateCarrier();
        return _native.ClearAndSchedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule a specific timer at the given time.
    /// </summary>
    /// <param name="time">The time of the timer to unschedule (UTC).</param>
    public Task Unschedule(DateTimeOffset time)
    {
        var carrier = CreateCarrier();
        return _native.Unschedule(time.UtcDateTime, carrier);
    }

    /// <summary>
    /// Unschedule all timers for the current key.
    /// </summary>
    public Task ClearScheduled()
    {
        var carrier = CreateCarrier();
        return _native.ClearScheduled(carrier);
    }

    /// <summary>
    /// List all scheduled timer times for the current key.
    /// </summary>
    /// <returns>An array of scheduled times (UTC).</returns>
    public async Task<DateTimeOffset[]> Scheduled()
    {
        var carrier = CreateCarrier();
        var times = await _native.Scheduled(carrier).ConfigureAwait(false);
        return Array.ConvertAll(times, t => new DateTimeOffset(t, TimeSpan.Zero));
    }

    private static Dictionary<string, string> CreateCarrier()
    {
        var carrier = new Dictionary<string, string>();
        TracePropagation.Inject(carrier);
        return carrier;
    }
}
