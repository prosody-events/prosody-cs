namespace Prosody;

/// <summary>
/// Specifies exception types that should be treated as permanent errors for a handler method.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to handler methods to declare which exception types
/// represent permanent errors that should not be retried. This is similar to
/// Python's <c>@permanent</c> decorator or Ruby's <c>permanent :on_message, TypeError</c>.
/// </para>
/// <para>
/// Exception matching is inheritance-aware: if you specify <c>ArgumentException</c>,
/// then <c>ArgumentNullException</c> will also be treated as permanent.
/// </para>
/// <para>
/// The <see cref="IPermanentError"/> interface takes precedence over this attribute,
/// allowing runtime override of error classification.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class OrderHandler : IProsodyHandler
/// {
///     [PermanentError(typeof(JsonException), typeof(ValidationException))]
///     public async Task OnMessageAsync(Context context, Message message, CancellationToken ct)
///     {
///         // JsonException → permanent (no retry)
///         // ValidationException → permanent (no retry)
///         // All other exceptions → transient (will retry)
///         var order = JsonSerializer.Deserialize&lt;Order&gt;(message.Payload);
///         Validate(order);
///         await ProcessOrder(order, ct);
///     }
///
///     public Task OnTimerAsync(Context context, Timer timer, CancellationToken ct)
///         => Task.CompletedTask;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermanentErrorAttribute : Attribute
{
    /// <summary>
    /// Gets the exception types that should be treated as permanent errors.
    /// </summary>
    /// <value>
    /// An array of exception types. Subtypes of these types are also matched.
    /// </value>
    public Type[] ExceptionTypes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PermanentErrorAttribute"/> class
    /// with the specified exception types.
    /// </summary>
    /// <param name="exceptionTypes">
    /// The exception types to treat as permanent errors. Subtypes are also matched.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="exceptionTypes"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// One or more types in <paramref name="exceptionTypes"/> do not derive from <see cref="Exception"/>.
    /// </exception>
    public PermanentErrorAttribute(params Type[] exceptionTypes)
    {
        ArgumentNullException.ThrowIfNull(exceptionTypes);

        foreach (var type in exceptionTypes)
        {
            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    $"Type '{type.FullName}' does not derive from System.Exception.",
                    nameof(exceptionTypes));
            }
        }

        ExceptionTypes = exceptionTypes;
    }

    /// <summary>
    /// Determines whether the specified exception matches any of the configured
    /// permanent error types.
    /// </summary>
    /// <param name="exception">The exception to check.</param>
    /// <returns>
    /// <c>true</c> if the exception type matches any configured type (including subtypes);
    /// otherwise, <c>false</c>.
    /// </returns>
    internal bool IsMatch(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptionType = exception.GetType();
        foreach (var permanentType in ExceptionTypes)
        {
            if (permanentType.IsAssignableFrom(exceptionType))
            {
                return true;
            }
        }

        return false;
    }
}
