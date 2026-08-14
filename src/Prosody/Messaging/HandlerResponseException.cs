namespace Prosody.Messaging;

/// <summary>The handler returned a classified error.</summary>
public sealed class HandlerResponseException : ResponseException
{
    /// <inheritdoc />
    public HandlerResponseException()
    {
        HandlerMessage = string.Empty;
    }

    /// <inheritdoc />
    public HandlerResponseException(string message)
        : base(message)
    {
        HandlerMessage = message;
    }

    /// <inheritdoc />
    public HandlerResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
        HandlerMessage = message;
    }

    /// <summary>Creates an exception from all handler failure fields.</summary>
    public HandlerResponseException(ResponseErrorCategory category, string handlerMessage, string message)
        : base(message)
    {
        Category = category;
        HandlerMessage = handlerMessage;
    }

    /// <summary>Gets the handler error category.</summary>
    public ResponseErrorCategory Category { get; }

    /// <summary>Gets the original handler error text.</summary>
    public string HandlerMessage { get; }
}
