namespace Prosody;

/// <summary>
/// Exception that indicates a permanent error which should not be retried.
/// </summary>
/// <remarks>
/// <para>
/// Throw this exception from a handler when you encounter an error that
/// will not be resolved by retrying. Common examples include:
/// </para>
/// <list type="bullet">
///   <item>Invalid message format or schema</item>
///   <item>Business rule violations</item>
///   <item>Missing required data that won't appear on retry</item>
///   <item>Authentication/authorization failures</item>
/// </list>
/// <para>
/// This is a convenience class that implements <see cref="IPermanentError"/>.
/// Use it to wrap other exceptions or throw directly when a permanent
/// error is detected at runtime.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task OnMessageAsync(Context context, Message message, CancellationToken ct)
/// {
///     try
///     {
///         var order = JsonSerializer.Deserialize&lt;Order&gt;(message.Payload);
///         await ProcessOrder(order, ct);
///     }
///     catch (JsonException ex)
///     {
///         // Malformed JSON won't be fixed by retry
///         throw new PermanentException("Invalid message format", ex);
///     }
/// }
/// </code>
/// </example>
public class PermanentException : Exception, IPermanentError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PermanentException"/> class.
    /// </summary>
    public PermanentException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PermanentException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PermanentException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PermanentException"/> class
    /// with a specified error message and a reference to the inner exception
    /// that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or a null
    /// reference if no inner exception is specified.
    /// </param>
    public PermanentException(string message, Exception innerException)
        : base(message, innerException) { }
}
