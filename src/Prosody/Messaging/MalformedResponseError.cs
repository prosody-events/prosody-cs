namespace Prosody.Messaging;

/// <summary>The response did not decode.</summary>
public sealed record MalformedResponseError(string Message) : ResponseError(Message);
