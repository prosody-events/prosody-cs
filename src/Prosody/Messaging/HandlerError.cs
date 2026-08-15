namespace Prosody.Messaging;

/// <summary>The remote handler answered with an error.</summary>
public sealed record HandlerError(string Message) : ResponseError(Message);
