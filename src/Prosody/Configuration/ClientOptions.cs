namespace Prosody.Configuration;

/// <summary>
/// Configuration options for the Prosody client.
/// </summary>
/// <remarks>
/// <para>
/// <b>Prefer using <see cref="ProsodyClientBuilder"/> via <see cref="Prosody.CreateClient"/>
/// (or <see cref="ProsodyClientBuilder.Create"/>) for a fluent configuration experience:</b>
/// </para>
/// <example>
/// <code>
/// await using var client = ProsodyClientBuilder.Create()
///     .WithBootstrapServers("localhost:9092")
///     .WithGroupId("my-app")
///     .WithSubscribedTopics("my-topic")
///     .WithMode(ClientMode.LowLatency)
///     .WithFailureTopic("dead-letters")
///     .Build();
/// </code>
/// </example>
/// <para>
/// Alternatively, you can use this class directly. All optional fields default to <c>null</c>,
/// which means "use the environment variable or library default". Use an object initializer
/// to set only the fields you need.
/// </para>
/// <example>
/// <code>
/// var options = new ClientOptions
/// {
///     BootstrapServers = ["localhost:9092"],
///     GroupId = "my-app",
///     SubscribedTopics = ["my-topic"],
///     StallThreshold = TimeSpan.FromMinutes(5),
///     Mode = ClientMode.LowLatency,
///     FailureTopic = "dead-letters"
/// };
/// await using var client = new ProsodyClient(options);
/// </code>
/// </example>
/// </remarks>
public sealed class ClientOptions
{
    // ========================================================================
    // Core options
    // ========================================================================

    /// <summary>
    /// Kafka bootstrap servers to connect to.
    /// Falls back to <c>PROSODY_BOOTSTRAP_SERVERS</c> environment variable.
    /// </summary>
    /// <example><c>["localhost:9092"]</c> or <c>["broker1:9092", "broker2:9092"]</c></example>
    public string[]? BootstrapServers { get; set; }

    /// <summary>
    /// Consumer group ID. Should be set to your application name.
    /// Falls back to <c>PROSODY_GROUP_ID</c> environment variable.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Topics to subscribe to.
    /// Falls back to <c>PROSODY_SUBSCRIBED_TOPICS</c> environment variable.
    /// </summary>
    /// <example><c>["my-topic"]</c> or <c>["topic1", "topic2"]</c></example>
    public string[]? SubscribedTopics { get; set; }

    /// <summary>
    /// Client operating mode. Default: <see cref="ClientMode.Pipeline"/>.
    /// </summary>
    public ClientMode? Mode { get; set; }

    /// <summary>
    /// Allowed event type prefixes. <c>null</c> = all events allowed.
    /// </summary>
    /// <example><c>["user.", "account."]</c> to only process events starting with those prefixes.</example>
    public string[]? AllowedEvents { get; set; }

    /// <summary>
    /// Source system identifier for outgoing messages.
    /// <c>null</c> = defaults to <see cref="GroupId"/>.
    /// </summary>
    /// <remarks>
    /// Set this to a different value than <see cref="GroupId"/> if you need to allow
    /// your application to consume its own produced messages (loopback).
    /// </remarks>
    public string? SourceSystem { get; set; }

    /// <summary>
    /// Use in-memory mock client for testing. Default: <c>false</c>.
    /// </summary>
    public bool? Mock { get; set; }

    // ========================================================================
    // Consumer options
    // ========================================================================

    /// <summary>
    /// Maximum number of messages being processed simultaneously.
    /// Default: 32.
    /// </summary>
    public uint? MaxConcurrency { get; set; }

    /// <summary>
    /// Maximum queued messages before pausing consumption.
    /// Default: 64.
    /// </summary>
    public uint? MaxUncommitted { get; set; }

    /// <summary>
    /// Size of LRU cache for message deduplication. Set to 0 to disable.
    /// Default: 4096.
    /// </summary>
    public uint? IdempotenceCacheSize { get; set; }

    /// <summary>
    /// Handler timeout. Handlers running longer than this are cancelled.
    /// Default: 80% of <see cref="StallThreshold"/>.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Report unhealthy if no progress for this long.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan? StallThreshold { get; set; }

    /// <summary>
    /// Wait this long for in-flight work before force-quit on shutdown.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan? ShutdownTimeout { get; set; }

    /// <summary>
    /// How often to fetch new messages from Kafka.
    /// Default: 100ms.
    /// </summary>
    public TimeSpan? PollInterval { get; set; }

    /// <summary>
    /// How often to save progress (commit offsets) to Kafka.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan? CommitInterval { get; set; }

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
    public ushort? ProbePort { get; set; }

    /// <summary>
    /// Timer storage granularity. Rarely needs changing.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan? SlabSize { get; set; }

    // ========================================================================
    // Producer options
    // ========================================================================

    /// <summary>
    /// Give up sending after this long.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan? SendTimeout { get; set; }

    // ========================================================================
    // Retry options
    // ========================================================================

    /// <summary>
    /// Maximum retry attempts. Set to 0 for unlimited retries.
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
    /// Track this many deferred keys in memory.
    /// Default: 1024.
    /// </summary>
    public uint? DeferCacheSize { get; set; }

    /// <summary>
    /// Timeout when loading deferred messages from Kafka.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan? DeferSeekTimeout { get; set; }

    /// <summary>
    /// Read optimization threshold. Rarely needs changing.
    /// Default: 100.
    /// </summary>
    public uint? DeferDiscardThreshold { get; set; }

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

    // ========================================================================
    // Cassandra options (required for timers in non-mock mode)
    // ========================================================================

    /// <summary>
    /// Cassandra contact nodes.
    /// </summary>
    /// <example><c>["localhost:9042"]</c> or <c>["cass1:9042", "cass2:9042"]</c></example>
    public string[]? CassandraNodes { get; set; }

    /// <summary>
    /// Cassandra keyspace name.
    /// Default: "prosody".
    /// </summary>
    public string? CassandraKeyspace { get; set; }

    /// <summary>
    /// Cassandra datacenter for queries.
    /// </summary>
    public string? CassandraDatacenter { get; set; }

    /// <summary>
    /// Cassandra rack for queries.
    /// </summary>
    public string? CassandraRack { get; set; }

    /// <summary>
    /// Cassandra username.
    /// </summary>
    public string? CassandraUser { get; set; }

    /// <summary>
    /// Cassandra password.
    /// </summary>
    public string? CassandraPassword { get; set; }

    /// <summary>
    /// Delete timer data older than this.
    /// Default: 1 year.
    /// </summary>
    public TimeSpan? CassandraRetention { get; set; }

    /// <summary>
    /// Validates the configuration options and throws if any are invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the configuration is invalid.</exception>
    internal void Validate()
    {
        var result = Validator.Validate(name: null, this);

        if (result.Failed)
        {
            throw new InvalidOperationException(result.FailureMessage);
        }
    }

    private static ClientOptionsValidator Validator { get; } = new();

    /// <summary>
    /// Creates an independent copy of this <see cref="ClientOptions"/> instance,
    /// deep-copying all array properties so mutations to the original do not affect the clone.
    /// </summary>
    internal ClientOptions Clone()
    {
        var clone = (ClientOptions)MemberwiseClone();
        clone.BootstrapServers = CloneArray(clone.BootstrapServers);
        clone.SubscribedTopics = CloneArray(clone.SubscribedTopics);
        clone.AllowedEvents = CloneArray(clone.AllowedEvents);
        clone.CassandraNodes = CloneArray(clone.CassandraNodes);
        return clone;
    }

    private static T[]? CloneArray<T>(T[]? source) => source is not null ? [.. source] : null;

    /// <summary>
    /// Converts to the internal native options type.
    /// </summary>
    internal Native.ClientOptions ToNative() =>
        new(
            BootstrapServers: BootstrapServers,
            GroupId: GroupId,
            SubscribedTopics: SubscribedTopics,
            Mode: Mode switch
            {
                ClientMode.Pipeline => Native.ClientMode.Pipeline,
                ClientMode.LowLatency => Native.ClientMode.LowLatency,
                ClientMode.BestEffort => Native.ClientMode.BestEffort,
                null => null,
                _ => throw new InvalidOperationException($"Unknown client mode: {Mode}"),
            },
            AllowedEvents: AllowedEvents,
            SourceSystem: SourceSystem,
            Mock: Mock,
            MaxConcurrency: MaxConcurrency,
            MaxUncommitted: MaxUncommitted,
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
            CassandraNodes: CassandraNodes,
            CassandraKeyspace: CassandraKeyspace,
            CassandraDatacenter: CassandraDatacenter,
            CassandraRack: CassandraRack,
            CassandraUser: CassandraUser,
            CassandraPassword: CassandraPassword,
            CassandraRetention: CassandraRetention
        );
}
