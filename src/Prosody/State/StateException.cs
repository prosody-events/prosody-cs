namespace Prosody.State;

/// <summary>
/// Base type for keyed-state errors surfaced at the public C# API.
/// </summary>
/// <remarks>
/// The concrete subclass carries the <see cref="Category"/>; the category is recovered from the
/// generated native exception <b>type</b> at the boundary, never by parsing an error message.
/// The two categories are <see cref="PermanentStateException"/> and
/// <see cref="TransientStateException"/> — a state error is never terminal.
/// </remarks>
public abstract class StateException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="StateException"/> class.</summary>
    protected StateException() { }

    /// <summary>Initializes a new instance of the <see cref="StateException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    protected StateException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="StateException"/> class with a message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    protected StateException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Gets the error category.</summary>
    public abstract StateErrorCategory Category { get; }
}
