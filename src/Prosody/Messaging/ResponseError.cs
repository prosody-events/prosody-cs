namespace Prosody.Messaging;

/// <summary>Why one subsystem did not return a successful response.</summary>
public abstract record ResponseError
{
    private protected ResponseError() { }
}
