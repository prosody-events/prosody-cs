namespace Prosody;

/// <summary>
/// Provides context for event handling, including cancellation and timer operations.
/// Valid only during handler callback - do not store references outside the handler scope.
/// </summary>
/// <remarks>
/// <para>
/// This interface matches the sibling wrapper APIs:
/// <list type="bullet">
/// <item>JavaScript: prosody-js/bindings.d.ts NativeContext</item>
/// <item>Python: prosody-py/python/prosody/context.pyi Context</item>
/// <item>Ruby: prosody-rb/lib/prosody/native_stubs.rb Context</item>
/// </list>
/// </para>
/// <para>
/// Timer operations automatically propagate OpenTelemetry context from <c>Activity.Current</c>.
/// </para>
/// </remarks>
public interface IEventContext
{
    // === Event Identity ===

    /// <summary>
    /// Gets the key for the current message or timer event.
    /// </summary>
    /// <remarks>
    /// For messages, this is the Kafka message key. For timers, this is the key
    /// that was used when scheduling the timer.
    /// </remarks>
    string Key { get; }

    // === Cancellation Members (FR-025, FR-026, FR-027) ===

    /// <summary>
    /// Gets a value indicating whether cancellation has been requested.
    /// </summary>
    /// <remarks>
    /// This is a synchronous, non-blocking check. Use <see cref="OnCancelAsync"/> to
    /// asynchronously wait for cancellation.
    /// </remarks>
    bool IsCancellationRequested { get; }

    /// <summary>
    /// Returns a task that completes when cancellation is requested.
    /// </summary>
    /// <returns>A task that completes when cancellation is signaled.</returns>
    /// <remarks>
    /// Use this method with <c>Task.WhenAny</c> to implement cooperative cancellation
    /// in long-running handlers.
    /// </remarks>
    Task OnCancelAsync();

    /// <summary>
    /// Registers a <see cref="CancellationToken"/> to trigger context cancellation.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation.</param>
    /// <returns>An <see cref="IDisposable"/> that unregisters the token when disposed.</returns>
    /// <remarks>
    /// Subsequent calls replace the previous registration. Only one token can be
    /// registered at a time.
    /// </remarks>
    IDisposable RegisterCancellation(CancellationToken cancellationToken);

    // === Timer Scheduling Members (FR-023) ===

    /// <summary>
    /// Schedules a timer to fire at the specified time.
    /// </summary>
    /// <param name="time">The UTC time when the timer should fire.</param>
    /// <returns>A task that completes when the timer is scheduled.</returns>
    Task ScheduleAsync(DateTimeOffset time);

    /// <summary>
    /// Atomically clears all scheduled timers and schedules a new one.
    /// </summary>
    /// <param name="time">The UTC time when the timer should fire.</param>
    /// <returns>A task that completes when the operation is complete.</returns>
    /// <remarks>
    /// This operation is atomic - either all existing timers are cleared and the new
    /// timer is scheduled, or the operation fails with no changes.
    /// </remarks>
    Task ClearAndScheduleAsync(DateTimeOffset time);

    /// <summary>
    /// Unschedules a timer at the specified time.
    /// </summary>
    /// <param name="time">The time of the timer to unschedule.</param>
    /// <returns>A task that completes when the timer is unscheduled.</returns>
    Task UnscheduleAsync(DateTimeOffset time);

    /// <summary>
    /// Clears all scheduled timers for the current key.
    /// </summary>
    /// <returns>A task that completes when all timers are cleared.</returns>
    Task ClearScheduledAsync();

    /// <summary>
    /// Gets all scheduled timer times for the current key.
    /// </summary>
    /// <returns>A task containing a read-only list of scheduled times.</returns>
    Task<IReadOnlyList<DateTimeOffset>> ScheduledAsync();
}
