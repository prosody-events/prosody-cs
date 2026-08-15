namespace Prosody.Messaging;

/// <summary>Contains one successful subsystem response.</summary>
public sealed record Success<T>(T Value) : Outcome<T>;
