namespace Prosody;

/// <summary>
/// Interface for handling Kafka messages and timers.
/// </summary>
/// <remarks>
/// Implement this interface to process incoming messages and timer events.
/// The handler is responsible for classifying errors as permanent or transient
/// to control retry behavior.
/// </remarks>
public interface IEventHandler
{
    /// <summary>
    /// Handles an incoming Kafka message.
    /// </summary>
    /// <param name="context">The event context for timer operations and cancellation.</param>
    /// <param name="message">The incoming message.</param>
    /// <param name="cancellationToken">A token that is cancelled when the partition is revoked.</param>
    /// <returns>A task that completes when the message is processed.</returns>
    Task OnMessageAsync(IEventContext context, Message message, CancellationToken cancellationToken);

    /// <summary>
    /// Handles a timer trigger.
    /// </summary>
    /// <param name="context">The event context for timer operations and cancellation.</param>
    /// <param name="trigger">The timer trigger information.</param>
    /// <param name="cancellationToken">A token that is cancelled when the partition is revoked.</param>
    /// <returns>A task that completes when the timer is processed.</returns>
    Task OnTimerAsync(IEventContext context, Trigger trigger, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether an exception should be treated as permanent (non-retryable).
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><c>true</c> if the error is permanent and should not be retried; otherwise, <c>false</c>.</returns>
    bool IsPermanentError(Exception exception);
}
