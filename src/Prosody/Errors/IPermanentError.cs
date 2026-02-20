namespace Prosody.Errors;

/// <summary>
/// Marker interface for exceptions that represent permanent errors.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface on custom exception types to indicate that
/// they should not be retried. When an exception implementing this interface
/// is thrown from a handler, Prosody will classify it as a permanent error
/// and will not retry the message.
/// </para>
/// <para>
/// This interface takes precedence over <see cref="PermanentErrorAttribute"/>
/// configuration, allowing runtime override of error classification.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class InvalidOrderException : Exception, IPermanentError
/// {
///     public InvalidOrderException(string message) : base(message) { }
/// }
///
/// // In your handler:
/// if (!order.IsValid)
///     throw new InvalidOrderException("Order failed validation");
/// </code>
/// </example>
#pragma warning disable CA1040 // Marker interface pattern: used for runtime type discrimination via 'is IPermanentError'
public interface IPermanentError;
#pragma warning restore CA1040
