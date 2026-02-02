namespace Prosody;

/// <summary>
/// Event handler interface for processing Kafka messages and timer events.
/// </summary>
/// <remarks>
/// Implement this interface to handle events from Prosody. The handler methods
/// receive a <see cref="CancellationToken"/> that is triggered when Prosody
/// requests cancellation (e.g., during shutdown or rebalance).
/// </remarks>
public interface IProsodyHandler
{
    /// <summary>
    /// Called when a Kafka message arrives.
    /// </summary>
    /// <param name="context">Event context for scheduling timers and checking cancellation.</param>
    /// <param name="message">The Kafka message data.</param>
    /// <param name="cancellationToken">
    /// Token that is cancelled when Prosody requests the handler to stop processing.
    /// Handlers should monitor this token and return promptly when cancelled.
    /// </param>
    /// <returns>
    /// A <see cref="HandlerResultCode"/> indicating how Prosody should handle the message.
    /// </returns>
    Task<HandlerResultCode> OnMessageAsync(
        Context context,
        Message message,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Called when a timer fires.
    /// </summary>
    /// <param name="context">Event context for scheduling timers and checking cancellation.</param>
    /// <param name="timer">The timer trigger data.</param>
    /// <param name="cancellationToken">
    /// Token that is cancelled when Prosody requests the handler to stop processing.
    /// Handlers should monitor this token and return promptly when cancelled.
    /// </param>
    /// <returns>
    /// A <see cref="HandlerResultCode"/> indicating how Prosody should handle the timer.
    /// </returns>
    Task<HandlerResultCode> OnTimerAsync(
        Context context,
        Timer timer,
        CancellationToken cancellationToken
    );
}
