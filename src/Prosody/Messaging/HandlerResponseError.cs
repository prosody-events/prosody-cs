namespace Prosody.Messaging;

/// <summary>The handler returned a classified error.</summary>
public sealed record HandlerResponseError(ResponseErrorCategory Category, string Message) : ResponseError;
