namespace Prosody.Messaging;

/// <summary>Explains why one subsystem did not return a successful response.</summary>
public abstract class ResponseException : Exception
{
    /// <inheritdoc />
    protected ResponseException() { }

    /// <inheritdoc />
    protected ResponseException(string message)
        : base(message) { }

    /// <inheritdoc />
    protected ResponseException(string message, Exception innerException)
        : base(message, innerException) { }
}
