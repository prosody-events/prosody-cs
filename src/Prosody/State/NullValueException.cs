namespace Prosody.State;

/// <summary>
/// Raised when a handler writes a <see langword="null"/> (or otherwise unrepresentable) value to a
/// keyed-state collection.
/// </summary>
/// <remarks>
/// A <see langword="null"/> write is a caller mistake, not a stored-value concept: use
/// <c>ClearAsync</c> (value/deque collections) or <c>RemoveAsync</c> (map collections) to delete
/// instead. It is a <see cref="TransientStateException"/> — the message backs off and redelivers so
/// a corrected handler reprocesses it, rather than committing the offset and losing the message.
/// The distinct type only sharpens the diagnostic message; it never promotes the error into the
/// permanent family.
/// </remarks>
public sealed class NullValueException : TransientStateException
{
    /// <summary>Initializes a new instance of the <see cref="NullValueException"/> class.</summary>
    public NullValueException() { }

    /// <summary>Initializes a new instance of the <see cref="NullValueException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public NullValueException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="NullValueException"/> class with a message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public NullValueException(string message, Exception innerException)
        : base(message, innerException) { }
}
