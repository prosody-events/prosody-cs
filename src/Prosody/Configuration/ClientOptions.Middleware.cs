namespace Prosody.Configuration;

// This file owns the options of the consumer middleware: retry, deferral, the Kafka loader,
// monopolization detection, and fair scheduling.

public sealed partial class ClientOptions
{
    // ========================================================================
    // Retry options
    // ========================================================================

    /// <summary>
    /// Low-latency retries before routing to the failure topic. Set to 0 to route the initial
    /// failure without retrying. Pipeline mode uses deferral and does not use this limit.
    /// Default: 3.
    /// </summary>
    public uint? MaxRetries { get; set; }

    /// <summary>
    /// Wait this long before first retry (exponential backoff base).
    /// Default: 20ms.
    /// </summary>
    public TimeSpan? RetryBase { get; set; }

    /// <summary>
    /// Never wait longer than this between retries.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? MaxRetryDelay { get; set; }

    /// <summary>
    /// Topic for unprocessable messages (dead letter queue).
    /// Required for <see cref="ClientMode.LowLatency"/> mode.
    /// </summary>
    public string? FailureTopic { get; set; }

    // ========================================================================
    // Deferral options (Pipeline mode)
    // ========================================================================

    /// <summary>
    /// Enable deferral for failing messages.
    /// Default: <c>true</c>.
    /// </summary>
    public bool? DeferEnabled { get; set; }

    /// <summary>
    /// Wait this long before first deferred retry.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan? DeferBase { get; set; }

    /// <summary>
    /// Never wait longer than this for deferred retries.
    /// Default: 24 hours.
    /// </summary>
    public TimeSpan? DeferMaxDelay { get; set; }

    /// <summary>
    /// Disable deferral when failure rate exceeds this threshold (0.0-1.0).
    /// Default: 0.9 (90%).
    /// </summary>
    public double? DeferFailureThreshold { get; set; }

    /// <summary>
    /// Measure failure rate over this time window.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? DeferFailureWindow { get; set; }

    /// <summary>
    /// Maximum deferred store cache entries per Cassandra defer store.
    /// Default: 8192.
    /// </summary>
    /// <remarks>Environment variable: <c>PROSODY_DEFER_STORE_CACHE_SIZE</c></remarks>
    public uint? DeferStoreCacheSize { get; set; }

    // ========================================================================
    // Kafka message loader options (all modes)
    // ========================================================================

    /// <summary>
    /// Maximum messages retained by the shared Kafka loader.
    /// Default: 1024.
    /// </summary>
    /// <remarks>Environment variable: <c>PROSODY_LOADER_CACHE_SIZE</c></remarks>
    public uint? LoaderCacheSize { get; set; }

    /// <summary>
    /// Timeout for Kafka loader seek operations.
    /// Default: 30 seconds.
    /// </summary>
    /// <remarks>Environment variable: <c>PROSODY_LOADER_SEEK_TIMEOUT</c></remarks>
    public TimeSpan? LoaderSeekTimeout { get; set; }

    /// <summary>
    /// Sequential-read distance before the loader seeks. Rarely needs changing.
    /// Default: 100.
    /// </summary>
    /// <remarks>Environment variable: <c>PROSODY_LOADER_DISCARD_THRESHOLD</c></remarks>
    public uint? LoaderDiscardThreshold { get; set; }

    // ========================================================================
    // Monopolization detection options (Pipeline mode)
    // ========================================================================

    /// <summary>
    /// Enable hot key protection.
    /// Default: <c>true</c>.
    /// </summary>
    public bool? MonopolizationEnabled { get; set; }

    /// <summary>
    /// Reject keys using more than this fraction of window time (0.0-1.0).
    /// Default: 0.9 (90%).
    /// </summary>
    public double? MonopolizationThreshold { get; set; }

    /// <summary>
    /// Measurement window for monopolization detection.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? MonopolizationWindow { get; set; }

    /// <summary>
    /// Maximum distinct keys to track for monopolization.
    /// Default: 8192.
    /// </summary>
    public uint? MonopolizationCacheSize { get; set; }

    // ========================================================================
    // Fair scheduling options (all modes)
    // ========================================================================

    /// <summary>
    /// Fraction of processing time reserved for retries (0.0-1.0).
    /// Default: 0.3 (30%).
    /// </summary>
    public double? SchedulerFailureWeight { get; set; }

    /// <summary>
    /// Messages waiting this long get maximum priority boost.
    /// Default: 2 minutes.
    /// </summary>
    public TimeSpan? SchedulerMaxWait { get; set; }

    /// <summary>
    /// Priority boost multiplier for waiting messages. Higher = more aggressive.
    /// Default: 200.0.
    /// </summary>
    public double? SchedulerWaitWeight { get; set; }

    /// <summary>
    /// Maximum distinct keys to track in scheduler.
    /// Default: 8192.
    /// </summary>
    public uint? SchedulerCacheSize { get; set; }
}
