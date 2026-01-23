namespace Prosody;

/// <summary>
/// Base exception for Prosody errors.
/// </summary>
public class ProsodyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ProsodyException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ProsodyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base class for event handler errors that participate in the retry system.
/// </summary>
/// <remarks>
/// Extend this class and override <see cref="IsPermanent"/> to control whether
/// errors should be retried or sent to the dead letter queue.
/// </remarks>
public abstract class EventHandlerException : Exception
{
    /// <summary>
    /// Gets a value indicating whether this error is permanent and should not be retried.
    /// </summary>
    public abstract bool IsPermanent { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventHandlerException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    protected EventHandlerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventHandlerException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    protected EventHandlerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents a transient error that should be retried.
/// </summary>
public class TransientException : EventHandlerException
{
    /// <inheritdoc />
    public override bool IsPermanent => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransientException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TransientException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransientException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TransientException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents a permanent error that should not be retried.
/// </summary>
public class PermanentException : EventHandlerException
{
    /// <inheritdoc />
    public override bool IsPermanent => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="PermanentException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PermanentException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PermanentException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PermanentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
