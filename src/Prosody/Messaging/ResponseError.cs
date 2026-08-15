namespace Prosody.Messaging;

/// <summary>Explains why one subsystem did not return a successful response.</summary>
public abstract record ResponseError
{
    private protected ResponseError(string message) => Message = message;

    /// <summary>Gets the failure text.</summary>
    public string Message { get; }
}
