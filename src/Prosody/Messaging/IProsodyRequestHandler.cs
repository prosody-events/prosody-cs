namespace Prosody.Messaging;

/// <summary>Handles events and returns a JSON response for requests.</summary>
public interface IProsodyRequestHandler<TPayload, TResponse>
{
    /// <summary>Handles one message and returns its response.</summary>
    Task<TResponse> OnMessageAsync(
        ProsodyContext prosodyContext,
        Message<TPayload> message,
        CancellationToken cancellationToken
    );

    /// <summary>Handles one timer. The result is not a request response.</summary>
    Task<TResponse> OnTimerAsync(
        ProsodyContext prosodyContext,
        ProsodyTimer timer,
        CancellationToken cancellationToken
    );
}
