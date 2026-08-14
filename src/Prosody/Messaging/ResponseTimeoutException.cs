namespace Prosody.Messaging;

/// <summary>No response arrived before the deadline.</summary>
public sealed class ResponseTimeoutException : ResponseException
{
    /// <inheritdoc />
    public ResponseTimeoutException() { }

    /// <inheritdoc />
    public ResponseTimeoutException(string message)
        : base(message) { }

    /// <inheritdoc />
    public ResponseTimeoutException(string message, Exception innerException)
        : base(message, innerException) { }
}
