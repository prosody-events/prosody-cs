namespace Prosody.Messaging;

/// <summary>The responder answered in another format.</summary>
public sealed record FormatMismatchError(string Message) : ResponseError(Message);
