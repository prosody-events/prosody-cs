namespace Prosody.Messaging;

/// <summary>The response payload did not decode.</summary>
public sealed class MalformedResponseException : ResponseException
{
    /// <inheritdoc />
    public MalformedResponseException() { }

    /// <inheritdoc />
    public MalformedResponseException(string message)
        : base(message) { }

    /// <inheritdoc />
    public MalformedResponseException(string message, Exception innerException)
        : base(message, innerException) { }
}
