namespace Prosody.Configuration;

/// <summary>
/// Client operating mode.
/// </summary>
/// <remarks>
/// Determines how the client handles message processing failures.
/// </remarks>
public enum ClientMode
{
    /// <summary>
    /// Retry failed messages indefinitely. Uses defer and monopolization
    /// detection. This is the default mode for production workloads.
    /// </summary>
    Pipeline = 0,

    /// <summary>
    /// Retry a few times, then send to a dead letter topic.
    /// Use when you need to keep moving and can reprocess failures later.
    /// Requires <see cref="ClientOptions.FailureTopic"/> to be set.
    /// </summary>
    LowLatency = 1,

    /// <summary>
    /// Log failures and move on. No retries.
    /// Use for development or when message loss is acceptable.
    /// </summary>
    BestEffort = 2,
}
