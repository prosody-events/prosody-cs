using Prosody.Errors;

namespace Prosody.State;

/// <summary>
/// A transient keyed-state failure that may succeed on retry.
/// </summary>
/// <remarks>
/// Every caller or input mistake at the keyed-state boundary folds into this category so a
/// data-dependent handler bug retries rather than committing the offset and silently losing the
/// message. It does not implement <see cref="IPermanentError"/>, so rethrowing it from a handler
/// classifies the event transient through the existing handler bridge.
/// </remarks>
public class TransientStateException : StateException
{
    /// <summary>Initializes a new instance of the <see cref="TransientStateException"/> class.</summary>
    public TransientStateException() { }

    /// <summary>Initializes a new instance of the <see cref="TransientStateException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public TransientStateException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="TransientStateException"/> class with a message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public TransientStateException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <inheritdoc/>
    public override StateErrorCategory Category => StateErrorCategory.Transient;
}
