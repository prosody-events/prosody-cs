namespace Prosody.Messaging;

/// <summary>Handles events and returns a JSON response for subsystem requests.</summary>
public interface IProsodyRequestHandler<TPayload, TResponse>
{
    /// <summary>Handles one message and returns its response.</summary>
    Task<TResponse> OnMessageAsync(
        ProsodyContext prosodyContext,
        Message<TPayload> message,
        CancellationToken cancellationToken
    );

    /// <summary>Handles one excise record and returns its response.</summary>
    Task<TResponse> OnExciseAsync(
        ProsodyContext prosodyContext,
        Message<TPayload> message,
        CancellationToken cancellationToken
    );

    /// <summary>Handles one timer.</summary>
    Task OnTimerAsync(ProsodyContext prosodyContext, ProsodyTimer timer, CancellationToken cancellationToken);
}
