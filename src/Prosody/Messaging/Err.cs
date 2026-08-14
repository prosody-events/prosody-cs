namespace Prosody.Messaging;

/// <summary>The subsystem returned an error.</summary>
public sealed record Err<T>(ResponseException Error) : RequestResult<T>;
