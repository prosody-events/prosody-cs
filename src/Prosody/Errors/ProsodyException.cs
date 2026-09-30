namespace Prosody.Errors;

/// <summary>
/// A broker or runtime failure in Prosody, such as a Kafka send that fails or a broker that is not
/// available.
/// </summary>
/// <remarks>
/// A caller mistake throws <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/>
/// instead, and a keyed-state failure throws a <see cref="State.StateException"/>. This exception does
/// not implement <see cref="IPermanentError"/>, so a handler that rethrows it retries the event.
/// </remarks>
public sealed class ProsodyException : Exception
{
    /// <summary>Initializes a new instance of <see cref="ProsodyException"/>.</summary>
    public ProsodyException() { }

    /// <summary>Initializes a new instance of <see cref="ProsodyException"/> with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public ProsodyException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of <see cref="ProsodyException"/> with a message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ProsodyException(string message, Exception innerException)
        : base(message, innerException) { }
}
