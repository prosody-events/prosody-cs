namespace Prosody.Messaging;

/// <summary>The subsystem returned an error.</summary>
public sealed record Failure<T>(ResponseException Error) : RequestResult<T>;
