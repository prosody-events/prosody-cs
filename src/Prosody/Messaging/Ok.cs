namespace Prosody.Messaging;

/// <summary>The handler returned a response.</summary>
public sealed record Ok<T>(T? Value) : RequestResult<T>;
