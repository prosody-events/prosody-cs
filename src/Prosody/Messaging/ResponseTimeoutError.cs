namespace Prosody.Messaging;

/// <summary>No response arrived before the deadline.</summary>
public sealed record ResponseTimeoutError : ResponseError;
