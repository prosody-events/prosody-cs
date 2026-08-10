using Prosody.Errors;

namespace Prosody.Messaging;

/// <summary>
/// Event handler interface for processing Kafka messages with a strongly typed payload and timer events.
/// </summary>
/// <typeparam name="TPayload">The message payload type.</typeparam>
/// <remarks>
/// <para>
/// Implement this interface to handle events from Prosody. The handler methods
/// receive a <see cref="CancellationToken"/> that is triggered when Prosody
/// requests cancellation (e.g., during shutdown, rebalance, or timeout).
/// </para>
/// <para>
/// Prosody deserializes the payload once, using the client's configured JSON options, before invoking
/// <see cref="OnMessageAsync"/>. For topics with dynamic or mixed schemas, use
/// <c>TPayload = <see cref="System.Text.Json.JsonElement"/></c>.
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
/// public class OrderHandler : IProsodyHandler&lt;Order&gt;
/// {
///     // Attribute declares JsonException as permanent for this method (covers deserialization failures too)
///     [PermanentError(typeof(JsonException))]
///     public async Task OnMessageAsync(ProsodyContext prosodyContext, Message&lt;Order&gt; message, CancellationToken ct)
///     {
///         var order = message.Payload;
///
///         if (order is null || !order.IsValid)
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
///     public Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken ct)
///         => Task.CompletedTask;
/// }
/// </code>
/// </example>
public interface IProsodyHandler<TPayload>
{
    /// <summary>
    /// Called when a Kafka message arrives.
    /// </summary>
    /// <param name="prosodyContext">Event context for scheduling timers and checking cancellation.</param>
    /// <param name="message">The Kafka message data, including the deserialized payload.</param>
    /// <param name="cancellationToken">
    /// Token that is cancelled when Prosody requests the handler to stop processing
    /// (e.g., during rebalance or timeout). During shutdown, handlers run freely
    /// before this token is cancelled near the end of the shutdown timeout. Handlers
    /// should monitor this token and exit promptly when cancelled. Note: an
    /// <see cref="OperationCanceledException"/> thrown in response to this token is
    /// treated as a transient error like any other exception — Prosody does not
    /// distinguish cancellation from failure.
    /// </param>
    /// <exception cref="PermanentException">
    /// Throw to indicate a permanent error that should not be retried.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Success:</b> Return normally (no exception).
    /// </para>
    /// <para>
    /// <b>Transient Error:</b> Throw any exception (including
    /// <see cref="OperationCanceledException"/>). Prosody will retry the message.
    /// </para>
    /// <para>
    /// <b>Permanent Error:</b> Throw <see cref="PermanentException"/> or an exception
    /// implementing <see cref="IPermanentError"/>, or throw an exception type declared
    /// in <see cref="PermanentErrorAttribute"/>. Prosody will not retry.
    /// </para>
    /// <para>
    /// Payload deserialization failures are classified using <see cref="PermanentErrorAttribute"/>
    /// on this method, just like exceptions thrown by the method body.
    /// </para>
    /// </remarks>
    Task OnMessageAsync(ProsodyContext prosodyContext, Message<TPayload> message, CancellationToken cancellationToken);

    /// <summary>Handles an excise record.</summary>
    Task OnExciseAsync(ProsodyContext prosodyContext, Message<TPayload> message, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a timer fires.
    /// </summary>
    /// <param name="prosodyContext">Event context for scheduling timers and checking cancellation.</param>
    /// <param name="timer">The timer trigger data.</param>
    /// <param name="cancellationToken">
    /// Token that is cancelled when Prosody requests the handler to stop processing
    /// (e.g., during rebalance or timeout). During shutdown, handlers run freely
    /// before this token is cancelled near the end of the shutdown timeout. Handlers
    /// should monitor this token and exit promptly when cancelled. Note: an
    /// <see cref="OperationCanceledException"/> thrown in response to this token is
    /// treated as a transient error like any other exception — Prosody does not
    /// distinguish cancellation from failure.
    /// </param>
    /// <exception cref="PermanentException">
    /// Throw to indicate a permanent error that should not be retried.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Success:</b> Return normally (no exception).
    /// </para>
    /// <para>
    /// <b>Transient Error:</b> Throw any exception (including
    /// <see cref="OperationCanceledException"/>). Prosody will retry the timer.
    /// </para>
    /// <para>
    /// <b>Permanent Error:</b> Throw <see cref="PermanentException"/> or an exception
    /// implementing <see cref="IPermanentError"/>, or throw an exception type declared
    /// in <see cref="PermanentErrorAttribute"/>. Prosody will not retry.
    /// </para>
    /// </remarks>
    Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken);
}
