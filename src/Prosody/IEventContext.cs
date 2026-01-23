namespace Prosody;

/// <summary>
/// Provides context for event handling, including timer operations.
/// </summary>
public interface IEventContext
{
    /// <summary>
    /// Gets a value indicating whether the handler should cancel its operation.
    /// </summary>
    bool ShouldCancel { get; }

    /// <summary>
    /// Returns a task that completes when cancellation is requested.
    /// </summary>
    /// <returns>A task that completes on cancellation.</returns>
    Task OnCancelAsync();

    /// <summary>
    /// Schedules a timer to fire at the specified time.
    /// </summary>
    /// <param name="time">The UTC time when the timer should fire.</param>
    /// <param name="timerType">The type of timer to schedule.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the timer is scheduled.</returns>
    Task ScheduleAsync(DateTimeOffset time, TimerType timerType = TimerType.Application, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears existing timers of the specified type and schedules a new one.
    /// </summary>
    /// <param name="time">The UTC time when the timer should fire.</param>
    /// <param name="timerType">The type of timer to schedule.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the operation is complete.</returns>
    Task ClearAndScheduleAsync(DateTimeOffset time, TimerType timerType = TimerType.Application, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unschedules a specific timer.
    /// </summary>
    /// <param name="time">The time of the timer to unschedule.</param>
    /// <param name="timerType">The type of timer to unschedule.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the timer is unscheduled.</returns>
    Task UnscheduleAsync(DateTimeOffset time, TimerType timerType = TimerType.Application, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unschedules all timers of the specified type.
    /// </summary>
    /// <param name="timerType">The type of timers to unschedule.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when all timers are unscheduled.</returns>
    Task UnscheduleAllAsync(TimerType timerType = TimerType.Application, CancellationToken cancellationToken = default);
}

/// <summary>
/// The type of timer.
/// </summary>
public enum TimerType
{
    /// <summary>
    /// Application-defined timer.
    /// </summary>
    Application,

    /// <summary>
    /// System timer for deferred message retry.
    /// </summary>
    DeferredMessage,

    /// <summary>
    /// System timer for deferred timer retry.
    /// </summary>
    DeferredTimer
}
