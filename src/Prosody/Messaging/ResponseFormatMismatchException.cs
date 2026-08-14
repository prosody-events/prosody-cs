namespace Prosody.Messaging;

/// <summary>The responder used another response format.</summary>
public sealed class ResponseFormatMismatchException : ResponseException
{
    /// <inheritdoc />
    public ResponseFormatMismatchException() { }

    /// <inheritdoc />
    public ResponseFormatMismatchException(string message)
        : base(message) { }

    /// <inheritdoc />
    public ResponseFormatMismatchException(string message, Exception innerException)
        : base(message, innerException) { }
}
