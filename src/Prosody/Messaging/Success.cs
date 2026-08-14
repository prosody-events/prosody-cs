namespace Prosody.Messaging;

/// <summary>The handler returned a response.</summary>
public sealed record Success<T>(T? Value) : RequestResult<T>;
