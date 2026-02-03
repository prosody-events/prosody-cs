namespace Prosody;

/// <summary>
/// Event handler interface for processing Kafka messages and timer events.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to handle events from Prosody. The handler methods
/// receive a <see cref="CancellationToken"/> that is triggered when Prosody
/// requests cancellation (e.g., during shutdown or rebalance).
/// </para>
/// <para>
/// <b>Error Classification:</b> By default, all exceptions are treated as transient
/// errors and will be retried. To mark an error as permanent (non-retryable):
/// </para>
/// <list type="bullet">
///   <item>Throw a <see cref="PermanentException"/> (or any exception implementing <see cref="IPermanentError"/>)</item>
///   <item>Apply <see cref="PermanentErrorAttribute"/> to declare exception types that are always permanent</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class OrderHandler : IProsodyHandler
/// {
///     // Attribute declares JsonException as permanent for this method
///     [PermanentError(typeof(JsonException))]
///     public async Task OnMessageAsync(Context context, Message message, CancellationToken ct)
///     {
///         var order = JsonSerializer.Deserialize&lt;Order&gt;(message.Payload);
///
///         if (!order.IsValid)
///         {
///             // Runtime decision: this specific error is permanent
///             throw new PermanentException("Invalid order data");
///         }
///
///         await ProcessOrder(order, ct);
///         // Success: no exception thrown
///         // Transient error: throw any other exception
///     }
///
///     public Task OnTimerAsync(Context context, Timer timer, CancellationToken ct)
///         => Task.CompletedTask;
/// }
/// </code>
/// </example>
public interface IProsodyHandler
{
    /// <summary>
    /// Called when a Kafka message arrives.
    /// </summary>
    /// <param name="context">Event context for scheduling timers and checking cancellation.</param>
    /// <param name="message">The Kafka message data.</param>
    /// <param name="cancellationToken">
    /// Token that is cancelled when Prosody requests the handler to stop processing.
    /// Handlers should monitor this token and exit promptly when cancelled.
    /// </param>
    /// <exception cref="PermanentException">
    /// Throw to indicate a permanent error that should not be retried.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled. Prosody will
    /// handle this as a cancellation, not an error.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Success:</b> Return normally (no exception).
    /// </para>
    /// <para>
    /// <b>Transient Error:</b> Throw any exception. Prosody will retry the message.
    /// </para>
    /// <para>
    /// <b>Permanent Error:</b> Throw <see cref="PermanentException"/> or an exception
    /// implementing <see cref="IPermanentError"/>, or throw an exception type declared
    /// in <see cref="PermanentErrorAttribute"/>. Prosody will not retry.
    /// </para>
    /// </remarks>
    Task OnMessageAsync(
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
    /// Handlers should monitor this token and exit promptly when cancelled.
    /// </param>
    /// <exception cref="PermanentException">
    /// Throw to indicate a permanent error that should not be retried.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled. Prosody will
    /// handle this as a cancellation, not an error.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Success:</b> Return normally (no exception).
    /// </para>
    /// <para>
    /// <b>Transient Error:</b> Throw any exception. Prosody will retry the timer.
    /// </para>
    /// <para>
    /// <b>Permanent Error:</b> Throw <see cref="PermanentException"/> or an exception
    /// implementing <see cref="IPermanentError"/>, or throw an exception type declared
    /// in <see cref="PermanentErrorAttribute"/>. Prosody will not retry.
    /// </para>
    /// </remarks>
    Task OnTimerAsync(
        Context context,
        Timer timer,
        CancellationToken cancellationToken
    );
}
