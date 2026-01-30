namespace Prosody;

/// <summary>
/// Result codes returned by event handlers to indicate how Prosody should process the event.
/// </summary>
public enum HandlerResultCode
{
    /// <summary>
    /// Handler completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// Transient error occurred. Prosody will retry the event.
    /// </summary>
    TransientError,

    /// <summary>
    /// Permanent error occurred. Prosody will not retry and may send to DLQ.
    /// </summary>
    PermanentError,

    /// <summary>
    /// Handler was cancelled (e.g., during shutdown or rebalance).
    /// </summary>
    Cancelled
}
