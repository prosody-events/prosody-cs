namespace Prosody;

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
    Pipeline,

    /// <summary>
    /// Retry a few times, then send to a dead letter topic.
    /// Use when you need to keep moving and can reprocess failures later.
    /// Requires <see cref="ClientOptions.FailureTopic"/> to be set.
    /// </summary>
    LowLatency,

    /// <summary>
    /// Log failures and move on. No retries.
    /// Use for development or when message loss is acceptable.
    /// </summary>
    BestEffort,
}

/// <summary>
/// Consumer state.
/// </summary>
/// <remarks>
/// Represents the current lifecycle state of the consumer.
/// </remarks>
public enum ConsumerState
{
    /// <summary>
    /// Consumer has not been configured.
    /// </summary>
    Unconfigured,

    /// <summary>
    /// Consumer is configured but not running.
    /// </summary>
    Configured,

    /// <summary>
    /// Consumer is actively processing messages.
    /// </summary>
    Running,
}

/// <summary>
/// Configuration options for the Prosody client.
/// </summary>
/// <remarks>
/// <para>
/// All optional fields default to <c>null</c>, which means "use the
/// environment variable or library default". Use <c>with</c> expressions
/// to override only the fields you need.
/// </para>
/// <example>
/// <code>
/// var options = new ClientOptions() with
/// {
///     BootstrapServers = ["localhost:9092"],
///     GroupId = "my-app",
///     SubscribedTopics = ["my-topic"],
///     StallThreshold = TimeSpan.FromMinutes(5),
///     Mode = ClientMode.LowLatency,
///     FailureTopic = "dead-letters"
/// };
/// </code>
/// </example>
/// </remarks>
public record ClientOptions
{
    // ========================================================================
    // Core options
    // ========================================================================

    /// <summary>
    /// Kafka bootstrap servers to connect to.
    /// Falls back to <c>PROSODY_BOOTSTRAP_SERVERS</c> environment variable.
    /// </summary>
    /// <example><c>["localhost:9092"]</c> or <c>["broker1:9092", "broker2:9092"]</c></example>
    public IReadOnlyList<string>? BootstrapServers { get; init; }

    /// <summary>
    /// Consumer group ID. Should be set to your application name.
    /// Falls back to <c>PROSODY_GROUP_ID</c> environment variable.
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Topics to subscribe to.
    /// Falls back to <c>PROSODY_SUBSCRIBED_TOPICS</c> environment variable.
    /// </summary>
    /// <example><c>["my-topic"]</c> or <c>["topic1", "topic2"]</c></example>
    public IReadOnlyList<string>? SubscribedTopics { get; init; }

    /// <summary>
    /// Client operating mode. Default: <see cref="ClientMode.Pipeline"/>.
    /// </summary>
    public ClientMode? Mode { get; init; }

    /// <summary>
    /// Allowed event type prefixes. <c>null</c> = all events allowed.
    /// </summary>
    /// <example><c>["user.", "account."]</c> to only process events starting with those prefixes.</example>
    public IReadOnlyList<string>? AllowedEvents { get; init; }

    /// <summary>
    /// Source system identifier for outgoing messages.
    /// <c>null</c> = defaults to <see cref="GroupId"/>.
    /// </summary>
    /// <remarks>
    /// Set this to a different value than <see cref="GroupId"/> if you need to allow
    /// your application to consume its own produced messages (loopback).
    /// </remarks>
    public string? SourceSystem { get; init; }

    /// <summary>
    /// Use in-memory mock client for testing. Default: <c>false</c>.
    /// </summary>
    public bool? Mock { get; init; }

    // ========================================================================
    // Consumer options
    // ========================================================================

    /// <summary>
    /// Maximum number of messages being processed simultaneously.
    /// Default: 32.
    /// </summary>
    public uint? MaxConcurrency { get; init; }

    /// <summary>
    /// Maximum queued messages before pausing consumption.
    /// Default: 64.
    /// </summary>
    public uint? MaxUncommitted { get; init; }

    /// <summary>
    /// Maximum queued messages per key before pausing.
    /// Default: 8.
    /// </summary>
    public uint? MaxEnqueuedPerKey { get; init; }

    /// <summary>
    /// Size of LRU cache for message deduplication. Set to 0 to disable.
    /// Default: 4096.
    /// </summary>
    public uint? IdempotenceCacheSize { get; init; }

    /// <summary>
    /// Handler timeout. Handlers running longer than this are cancelled.
    /// Default: 80% of <see cref="StallThreshold"/>.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Report unhealthy if no progress for this long.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? StallThreshold { get; init; }

    /// <summary>
    /// Wait this long for in-flight work before force-quit on shutdown.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan? ShutdownTimeout { get; init; }

    /// <summary>
    /// How often to fetch new messages from Kafka.
    /// Default: 100ms.
    /// </summary>
    public TimeSpan? PollInterval { get; init; }

    /// <summary>
    /// How often to save progress (commit offsets) to Kafka.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan? CommitInterval { get; init; }

    /// <summary>
    /// HTTP port for health check probes (<c>/livez</c>, <c>/readyz</c>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>null</c>: use default (8000) or environment variable</item>
    /// <item><c>0</c>: explicitly disable the probe server</item>
    /// <item><c>1-65535</c>: use this port</item>
    /// </list>
    /// </remarks>
    public ushort? ProbePort { get; init; }

    /// <summary>
    /// Timer storage granularity. Rarely needs changing.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan? SlabSize { get; init; }

    // ========================================================================
    // Producer options
    // ========================================================================

    /// <summary>
    /// Give up sending after this long.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan? SendTimeout { get; init; }

    // ========================================================================
    // Retry options
    // ========================================================================

    /// <summary>
    /// Maximum retry attempts. Set to 0 for unlimited retries.
    /// Default: 3.
    /// </summary>
    public uint? MaxRetries { get; init; }

    /// <summary>
    /// Wait this long before first retry (exponential backoff base).
    /// Default: 20ms.
    /// </summary>
    public TimeSpan? RetryBase { get; init; }

    /// <summary>
    /// Never wait longer than this between retries.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? MaxRetryDelay { get; init; }

    /// <summary>
    /// Topic for unprocessable messages (dead letter queue).
    /// Required for <see cref="ClientMode.LowLatency"/> mode.
    /// </summary>
    public string? FailureTopic { get; init; }

    // ========================================================================
    // Deferral options (Pipeline mode)
    // ========================================================================

    /// <summary>
    /// Enable deferral for failing messages.
    /// Default: <c>true</c>.
    /// </summary>
    public bool? DeferEnabled { get; init; }

    /// <summary>
    /// Wait this long before first deferred retry.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan? DeferBase { get; init; }

    /// <summary>
    /// Never wait longer than this for deferred retries.
    /// Default: 24 hours.
    /// </summary>
    public TimeSpan? DeferMaxDelay { get; init; }

    /// <summary>
    /// Disable deferral when failure rate exceeds this threshold (0.0-1.0).
    /// Default: 0.9 (90%).
    /// </summary>
    public double? DeferFailureThreshold { get; init; }

    /// <summary>
    /// Measure failure rate over this time window.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? DeferFailureWindow { get; init; }

    /// <summary>
    /// Track this many deferred keys in memory.
    /// Default: 1024.
    /// </summary>
    public uint? DeferCacheSize { get; init; }

    /// <summary>
    /// Timeout when loading deferred messages from Kafka.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan? DeferSeekTimeout { get; init; }

    /// <summary>
    /// Read optimization threshold. Rarely needs changing.
    /// Default: 100.
    /// </summary>
    public uint? DeferDiscardThreshold { get; init; }

    // ========================================================================
    // Monopolization detection options (Pipeline mode)
    // ========================================================================

    /// <summary>
    /// Enable hot key protection.
    /// Default: <c>true</c>.
    /// </summary>
    public bool? MonopolizationEnabled { get; init; }

    /// <summary>
    /// Reject keys using more than this fraction of window time (0.0-1.0).
    /// Default: 0.9 (90%).
    /// </summary>
    public double? MonopolizationThreshold { get; init; }

    /// <summary>
    /// Measurement window for monopolization detection.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? MonopolizationWindow { get; init; }

    /// <summary>
    /// Maximum distinct keys to track for monopolization.
    /// Default: 8192.
    /// </summary>
    public uint? MonopolizationCacheSize { get; init; }

    // ========================================================================
    // Fair scheduling options (all modes)
    // ========================================================================

    /// <summary>
    /// Fraction of processing time reserved for retries (0.0-1.0).
    /// Default: 0.3 (30%).
    /// </summary>
    public double? SchedulerFailureWeight { get; init; }

    /// <summary>
    /// Messages waiting this long get maximum priority boost.
    /// Default: 2 minutes.
    /// </summary>
    public TimeSpan? SchedulerMaxWait { get; init; }

    /// <summary>
    /// Priority boost multiplier for waiting messages. Higher = more aggressive.
    /// Default: 200.0.
    /// </summary>
    public double? SchedulerWaitWeight { get; init; }

    /// <summary>
    /// Maximum distinct keys to track in scheduler.
    /// Default: 8192.
    /// </summary>
    public uint? SchedulerCacheSize { get; init; }

    // ========================================================================
    // Cassandra options (required for timers in non-mock mode)
    // ========================================================================

    /// <summary>
    /// Cassandra contact nodes.
    /// </summary>
    /// <example><c>["localhost:9042"]</c> or <c>["cass1:9042", "cass2:9042"]</c></example>
    public IReadOnlyList<string>? CassandraNodes { get; init; }

    /// <summary>
    /// Cassandra keyspace name.
    /// Default: "prosody".
    /// </summary>
    public string? CassandraKeyspace { get; init; }

    /// <summary>
    /// Cassandra datacenter for queries.
    /// </summary>
    public string? CassandraDatacenter { get; init; }

    /// <summary>
    /// Cassandra rack for queries.
    /// </summary>
    public string? CassandraRack { get; init; }

    /// <summary>
    /// Cassandra username.
    /// </summary>
    public string? CassandraUser { get; init; }

    /// <summary>
    /// Cassandra password.
    /// </summary>
    public string? CassandraPassword { get; init; }

    /// <summary>
    /// Delete timer data older than this.
    /// Default: 1 year.
    /// </summary>
    public TimeSpan? CassandraRetention { get; init; }

    /// <summary>
    /// Converts to the internal native options type.
    /// </summary>
    internal Native.ClientOptions ToNative() =>
        new(
            BootstrapServers: BootstrapServers?.ToArray(),
            GroupId: GroupId,
            SubscribedTopics: SubscribedTopics?.ToArray(),
            Mode: Mode switch
            {
                ClientMode.Pipeline => Native.ClientMode.Pipeline,
                ClientMode.LowLatency => Native.ClientMode.LowLatency,
                ClientMode.BestEffort => Native.ClientMode.BestEffort,
                null => null,
                _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
            },
            AllowedEvents: AllowedEvents?.ToArray(),
            SourceSystem: SourceSystem,
            Mock: Mock,
            MaxConcurrency: MaxConcurrency,
            MaxUncommitted: MaxUncommitted,
            MaxEnqueuedPerKey: MaxEnqueuedPerKey,
            IdempotenceCacheSize: IdempotenceCacheSize,
            Timeout: Timeout,
            StallThreshold: StallThreshold,
            ShutdownTimeout: ShutdownTimeout,
            PollInterval: PollInterval,
            CommitInterval: CommitInterval,
            ProbePort: ProbePort,
            SlabSize: SlabSize,
            SendTimeout: SendTimeout,
            MaxRetries: MaxRetries,
            RetryBase: RetryBase,
            MaxRetryDelay: MaxRetryDelay,
            FailureTopic: FailureTopic,
            DeferEnabled: DeferEnabled,
            DeferBase: DeferBase,
            DeferMaxDelay: DeferMaxDelay,
            DeferFailureThreshold: DeferFailureThreshold,
            DeferFailureWindow: DeferFailureWindow,
            DeferCacheSize: DeferCacheSize,
            DeferSeekTimeout: DeferSeekTimeout,
            DeferDiscardThreshold: DeferDiscardThreshold,
            MonopolizationEnabled: MonopolizationEnabled,
            MonopolizationThreshold: MonopolizationThreshold,
            MonopolizationWindow: MonopolizationWindow,
            MonopolizationCacheSize: MonopolizationCacheSize,
            SchedulerFailureWeight: SchedulerFailureWeight,
            SchedulerMaxWait: SchedulerMaxWait,
            SchedulerWaitWeight: SchedulerWaitWeight,
            SchedulerCacheSize: SchedulerCacheSize,
            CassandraNodes: CassandraNodes?.ToArray(),
            CassandraKeyspace: CassandraKeyspace,
            CassandraDatacenter: CassandraDatacenter,
            CassandraRack: CassandraRack,
            CassandraUser: CassandraUser,
            CassandraPassword: CassandraPassword,
            CassandraRetention: CassandraRetention
        );
}
