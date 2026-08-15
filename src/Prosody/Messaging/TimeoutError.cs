namespace Prosody.Messaging;

/// <summary>No response arrived before the deadline.</summary>
public sealed record TimeoutError(string Message) : ResponseError(Message);
