namespace Prosody.Messaging;

/// <summary>Contains one subsystem failure.</summary>
public sealed record Failure<T>(ResponseError Error) : Outcome<T>;
