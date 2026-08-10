namespace Prosody.Messaging;

/// <summary>One subsystem result from a peer request.</summary>
public abstract record RequestResult<T>
{
    private protected RequestResult() { }
}
