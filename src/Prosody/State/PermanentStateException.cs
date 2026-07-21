using Prosody.Errors;

namespace Prosody.State;

/// <summary>
/// A permanent keyed-state failure that can never succeed for this event.
/// </summary>
/// <remarks>
/// Reserved for configuration or deployment mistakes: binding an unregistered collection, a
/// registered-identity mismatch, or a duplicate name. Because it implements
/// <see cref="IPermanentError"/>, rethrowing it from a handler classifies the event permanent
/// through the existing handler bridge, with no state-specific bridge changes.
/// </remarks>
public sealed class PermanentStateException : StateException, IPermanentError
{
    /// <summary>Initializes a new instance of the <see cref="PermanentStateException"/> class.</summary>
    public PermanentStateException() { }

    /// <summary>Initializes a new instance of the <see cref="PermanentStateException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public PermanentStateException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="PermanentStateException"/> class with a message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PermanentStateException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <inheritdoc/>
    public override StateErrorCategory Category => StateErrorCategory.Permanent;
}
