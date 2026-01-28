using Prosody.Native;

namespace Prosody;

/// <summary>
/// Base exception for Prosody errors.
/// </summary>
public class ProsodyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyException"/> class.
    /// </summary>
    public ProsodyException() : base()
    {
    }


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
/// Exception thrown when a connection to Kafka or Cassandra fails.
/// </summary>
/// <remarks>
/// Maps from <c>FFIErrorCode.ConnectionFailed</c> in the native layer.
/// </remarks>
public class ProsodyConnectionException : ProsodyException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyConnectionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ProsodyConnectionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyConnectionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ProsodyConnectionException(string message, Exception innerException) : base(message, innerException)
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

/// <summary>
/// Helper class to convert FFI error codes to appropriate C# exceptions.
/// </summary>
internal static class FFIErrorHelper
{
    /// <summary>
    /// Converts an <see cref="FFIErrorCode"/> to the appropriate C# exception.
    /// </summary>
    /// <param name="errorCode">The FFI error code.</param>
    /// <param name="context">Optional context message to include in the exception.</param>
    /// <returns>The appropriate exception for the error code.</returns>
    internal static Exception ToException(FFIErrorCode errorCode, string? context = null)
    {
        var message = context ?? GetDefaultMessage(errorCode);

        if (errorCode.IsNullPassed)
        {
            return new ArgumentNullException(null, message);
        }

        if (errorCode.IsInvalidArgument)
        {
            return new ArgumentException(message);
        }

        if (errorCode.IsConnectionFailed)
        {
            return new ProsodyConnectionException(message);
        }

        if (errorCode.IsCancelled)
        {
            return new OperationCanceledException(message);
        }

        if (errorCode.IsInvalidContext || errorCode.IsAlreadySubscribed || errorCode.IsNotSubscribed)
        {
            return new InvalidOperationException(message);
        }

        // Panic and Internal both map to ProsodyException
        return new ProsodyException(message);
    }

    /// <summary>
    /// Throws the appropriate exception for the given error code.
    /// </summary>
    /// <param name="errorCode">The FFI error code.</param>
    /// <param name="context">Optional context message to include in the exception.</param>
    /// <exception cref="ArgumentNullException">When error code is <c>NullPassed</c>.</exception>
    /// <exception cref="ArgumentException">When error code is <c>InvalidArgument</c>.</exception>
    /// <exception cref="ProsodyConnectionException">When error code is <c>ConnectionFailed</c>.</exception>
    /// <exception cref="OperationCanceledException">When error code is <c>Cancelled</c>.</exception>
    /// <exception cref="InvalidOperationException">When error code is <c>InvalidContext</c>, <c>AlreadySubscribed</c>, or <c>NotSubscribed</c>.</exception>
    /// <exception cref="ProsodyException">When error code is <c>Panic</c> or <c>Internal</c>.</exception>
    internal static void ThrowIfError(FFIErrorCode errorCode, string? context = null)
    {
        if (errorCode.IsOk)
        {
            return;
        }

        throw ToException(errorCode, context);
    }

    /// <summary>
    /// Gets the default error message for the given error code.
    /// </summary>
    private static string GetDefaultMessage(FFIErrorCode errorCode)
    {
        if (errorCode.IsNullPassed)
            return "A null value was passed where a valid value was expected.";
        if (errorCode.IsPanic)
            return "A panic occurred in the native library.";
        if (errorCode.IsInvalidArgument)
            return "An invalid argument was provided.";
        if (errorCode.IsConnectionFailed)
            return "Failed to connect to Kafka or Cassandra.";
        if (errorCode.IsCancelled)
            return "The operation was cancelled.";
        if (errorCode.IsInternal)
            return "An internal error occurred.";
        if (errorCode.IsInvalidContext)
            return "The context is invalid or has been disposed.";
        if (errorCode.IsAlreadySubscribed)
            return "The client is already subscribed.";
        if (errorCode.IsNotSubscribed)
            return "The client is not subscribed.";

        return "An unknown error occurred.";
    }
}
