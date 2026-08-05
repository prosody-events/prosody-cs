using Prosody.State;

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
    /// The native client resolves the effective value: this option, then the
    /// <c>PROSODY_MAX_CONCURRENCY</c> environment variable, then 32.
    /// The value must be from 1 through 10,000.
    /// </summary>
    public uint? MaxConcurrency { get; set; }

    /// <summary>
    /// Maximum queued messages before pausing consumption.
    /// Default: 64.
    /// </summary>
    public uint? MaxUncommitted { get; set; }

    /// <summary>
    /// Global shared cache capacity across all partitions for deduplication. Deduplication is always
    /// active; this value must be greater than 0. A value of 0 is rejected when the client is built.
    /// Falls back to <c>PROSODY_IDEMPOTENCE_CACHE_SIZE</c> environment variable.
    /// Default: 8192.
    /// </summary>
    public uint? IdempotenceCacheSize { get; set; }

    /// <summary>
    /// Version string for cache-busting deduplication hashes. Changing this value invalidates
    /// all previously recorded dedup entries, causing messages to be reprocessed.
    /// Falls back to <c>PROSODY_IDEMPOTENCE_VERSION</c> environment variable.
    /// Default: "1".
    /// </summary>
    public string? IdempotenceVersion { get; set; }

    /// <summary>
    /// TTL for deduplication records in Cassandra. Records expire automatically after this duration.
    /// Falls back to <c>PROSODY_IDEMPOTENCE_TTL</c> environment variable.
    /// Default: 7 days.
    /// </summary>
    public TimeSpan? IdempotenceTtl { get; set; }

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
    /// Shutdown budget; handlers complete freely before cancellation fires near the deadline.
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

    /// <summary>
    /// Span linking for message execution spans.
    /// Controls how the receive span connects to the OTel context propagated from the Kafka message producer.
    /// Default: <see cref="SpanRelation.Child"/>.
    /// </summary>
    public SpanRelation? MessageSpans { get; set; }

    /// <summary>
    /// Span linking for timer execution spans.
    /// Controls how timer spans connect to the OTel context stored when the timer was scheduled.
    /// Default: <see cref="SpanRelation.FollowsFrom"/>.
    /// </summary>
    public SpanRelation? TimerSpans { get; set; }

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

    // ========================================================================
    // Cassandra options for persistent features in non-mock mode
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
    /// Retention period for persistent timer and deferral data.
    /// Default: 1 year.
    /// </summary>
    public TimeSpan? CassandraRetention { get; set; }

    // ========================================================================
    // Telemetry options
    // ========================================================================

    /// <summary>
    /// Kafka topic to produce telemetry events to.
    /// Falls back to <c>PROSODY_TELEMETRY_TOPIC</c> environment variable.
    /// Default: <c>"prosody.telemetry-events"</c>.
    /// </summary>
    public string? TelemetryTopic { get; set; }

    /// <summary>
    /// Enables or disables the telemetry emitter.
    /// Falls back to <c>PROSODY_TELEMETRY_ENABLED</c> environment variable.
    /// Default: <c>true</c>.
    /// </summary>
    public bool? TelemetryEnabled { get; set; }

    // ========================================================================
    // Serialization options
    // ========================================================================

    /// <summary>
    /// Callback applied after library defaults to configure <see cref="System.Text.Json.JsonSerializerOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set programmatically only — not bindable from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
    /// Library defaults (<see cref="System.Text.Json.JsonSerializerDefaults.Web"/>, <c>JsonStringEnumConverter</c>,
    /// <c>WhenWritingNull</c>) are applied first; this callback runs after and can override them.
    /// </para>
    /// <para>
    /// To enable AOT/trim-safe serialization, set <c>TypeInfoResolver</c> to a source-generated
    /// <c>JsonSerializerContext</c> here.
    /// </para>
    /// </remarks>
    public Action<System.Text.Json.JsonSerializerOptions>? ConfigureJsonOptions { get; set; }

    // ========================================================================
    // Keyed-state options
    // ========================================================================

    /// <summary>
    /// The keyed-state collections to register, declared with <see cref="StateDefinition"/> factories.
    /// </summary>
    /// <remarks>
    /// Set programmatically only — not bindable from
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>. Prefer
    /// <see cref="ProsodyClientBuilder.WithStateCollections"/>. Prosody validates collection names,
    /// identities, and semantic limits when the client is built.
    /// </remarks>
    public StateDefinition[]? StateCollections { get; set; }

    /// <summary>
    /// Disk workspace for the local keyed-state cache. Each live client needs its own directory.
    /// Falls back to <c>PROSODY_STATE_CACHE_DIR</c>, then a per-client temporary directory.
    /// Must not be an empty string when set.
    /// </summary>
    public string? StateCacheDir { get; set; }

    /// <summary>
    /// Capacity of the owning keyed-state cache, such as <c>64 MiB</c>.
    /// Uses <c>PROSODY_STATE_OWNED_CACHE_SIZE</c> when omitted.
    /// Otherwise, the storage engine selects its default.
    /// </summary>
    public string? StateOwnedCacheSize { get; set; }

    /// <summary>
    /// Capacity of the published-state read cache, such as <c>1 MiB</c>.
    /// Uses <c>PROSODY_STATE_READ_CACHE_SIZE</c> when omitted.
    /// It then uses the owned cache size when set, or 1 MiB when both sizes are unset.
    /// </summary>
    public string? StateReadCacheSize { get; set; }

    /// <summary>
    /// Default cache policy for published-state reads.
    /// Uses <c>PROSODY_STATE_READ_CACHE_TTL</c> when omitted, then 5 seconds.
    /// </summary>
    public StateReadCache? StateReadCache { get; set; }

    /// <summary>
    /// Subsystem under which published JSON collections are advertised.
    /// Uses <c>PROSODY_SUBSYSTEM</c> when omitted. Published collections require it.
    /// </summary>
    public string? Subsystem { get; set; }

    /// <summary>
    /// Delay between staging a provisional keyed-state cell and the recovery sweep. Every registered
    /// TTL must strictly exceed this. Falls back to <c>PROSODY_STATE_RECOVERY_DELAY</c>, then to
    /// 30 seconds. Must be a whole number of seconds of at least one when set.
    /// </summary>
    public TimeSpan? StateRecoveryDelay { get; set; }

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
        clone.StateCollections = CloneArray(clone.StateCollections);
        return clone;
    }

    private static T[]? CloneArray<T>(T[]? source) => source is not null ? [.. source] : null;

    private static Native.SpanRelation? ToNativeSpanRelation(SpanRelation? relation) =>
        relation switch
        {
            SpanRelation.Child => Native.SpanRelation.Child,
            SpanRelation.FollowsFrom => Native.SpanRelation.FollowsFrom,
            null => null,
            _ => throw new InvalidOperationException($"Unknown span relation: {relation}"),
        };

    private Native.ClientMode? ToNativeMode() =>
        Mode switch
        {
            ClientMode.Pipeline => Native.ClientMode.Pipeline,
            ClientMode.LowLatency => Native.ClientMode.LowLatency,
            ClientMode.BestEffort => Native.ClientMode.BestEffort,
            null => null,
            _ => throw new InvalidOperationException($"Unknown client mode: {Mode}"),
        };

    /// <summary>
    /// Converts to the internal native options type.
    /// </summary>
    internal Native.ClientOptions ToNative() =>
        ToNativeBase() with
        {
            StateCollections = StateCollections is null
                ? null
                : Array.ConvertAll(StateCollections, definition => definition.ToNative()),
            StateCacheDir = StateCacheDir,
            StateOwnedCacheSize = StateOwnedCacheSize,
            StateReadCacheSize = StateReadCacheSize,
            StateReadCacheTtl = StateReadCache?.Ttl,
            StateReadCacheDisabled = StateReadCache?.IsDisabled,
            Subsystem = Subsystem,
            StateRecoveryDelay = StateRecoveryDelay,
        };

    private Native.ClientOptions ToNativeBase() =>
        new(
            BootstrapServers: BootstrapServers,
            GroupId: GroupId,
            SubscribedTopics: SubscribedTopics,
            Mode: ToNativeMode(),
            AllowedEvents: AllowedEvents,
            SourceSystem: SourceSystem,
            Mock: Mock,
            MaxConcurrency: MaxConcurrency,
            MaxUncommitted: MaxUncommitted,
            IdempotenceCacheSize: IdempotenceCacheSize,
            IdempotenceVersion: IdempotenceVersion,
            IdempotenceTtl: IdempotenceTtl,
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
            DeferStoreCacheSize: DeferStoreCacheSize,
            LoaderCacheSize: LoaderCacheSize,
            LoaderSeekTimeout: LoaderSeekTimeout,
            LoaderDiscardThreshold: LoaderDiscardThreshold,
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
            CassandraRetention: CassandraRetention,
            TelemetryTopic: TelemetryTopic,
            TelemetryEnabled: TelemetryEnabled,
            MessageSpans: ToNativeSpanRelation(MessageSpans),
            TimerSpans: ToNativeSpanRelation(TimerSpans)
        );
}
