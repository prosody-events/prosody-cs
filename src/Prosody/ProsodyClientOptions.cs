namespace Prosody;

/// <summary>
/// Builder for Prosody client configuration.
/// Only explicitly set values are passed to the native layer; unset values
/// fall back to environment variables or system defaults.
/// </summary>
/// <remarks>
/// <para>
/// This configuration pattern matches the sibling wrapper APIs:
/// <list type="bullet">
/// <item>JavaScript: prosody-js/bindings.d.ts Configuration interface (40+ params)</item>
/// <item>Python: prosody-py/src/client/config.rs ConfigBuilder</item>
/// <item>Ruby: prosody-rb/lib/prosody/configuration.rb DSL</item>
/// </list>
/// </para>
/// <para>
/// Configuration is organized into 10 categories with 46 total parameters.
/// </para>
/// </remarks>
public sealed class ProsodyClientOptionsBuilder
{
    private readonly HashSet<string> _setProperties = new();

    // === Core Kafka Configuration (3 required, 3 optional) ===
    private string? _bootstrapServers;
    private string? _groupId;
    private string[]? _subscribedTopics;
    private string[]? _allowedEvents;
    private string? _sourceSystem;
    private bool? _mock;

    // === Operating Mode ===
    private ProsodyMode? _mode;

    // === Concurrency & Limits ===
    private int? _maxConcurrency;
    private int? _maxUncommitted;
    private int? _maxEnqueuedPerKey;
    private int? _idempotenceCacheSize;

    // === Timing Configuration ===
    private TimeSpan? _sendTimeout;
    private TimeSpan? _stallThreshold;
    private TimeSpan? _shutdownTimeout;
    private TimeSpan? _pollInterval;
    private TimeSpan? _commitInterval;
    private TimeSpan? _timeout;
    private TimeSpan? _slabSize;

    // === Retry Configuration ===
    private TimeSpan? _retryBase;
    private TimeSpan? _maxRetryDelay;
    private int? _maxRetries;
    private string? _failureTopic;

    // === Health Probe ===
    private int? _probePort;

    // === Cassandra Configuration ===
    private string? _cassandraNodes;
    private string? _cassandraKeyspace;
    private string? _cassandraDatacenter;
    private string? _cassandraRack;
    private string? _cassandraUser;
    private string? _cassandraPassword;
    private TimeSpan? _cassandraRetention;

    // === Scheduler Configuration ===
    private double? _schedulerFailureWeight;
    private TimeSpan? _schedulerMaxWait;
    private double? _schedulerWaitWeight;
    private int? _schedulerCacheSize;

    // === Monopolization Configuration ===
    private bool? _monopolizationEnabled;
    private double? _monopolizationThreshold;
    private TimeSpan? _monopolizationWindow;
    private int? _monopolizationCacheSize;

    // === Defer Configuration ===
    private bool? _deferEnabled;
    private TimeSpan? _deferBase;
    private TimeSpan? _deferMaxDelay;
    private double? _deferFailureThreshold;
    private TimeSpan? _deferFailureWindow;
    private int? _deferCacheSize;
    private TimeSpan? _deferSeekTimeout;
    private long? _deferDiscardThreshold;

    // === Core Kafka Configuration Methods ===

    /// <summary>
    /// Sets the Kafka bootstrap servers (comma-separated). REQUIRED.
    /// </summary>
    /// <param name="value">Bootstrap server addresses (e.g., "localhost:9092,broker2:9092").</param>
    /// <remarks>Can also be set via <c>PROSODY_BOOTSTRAP_SERVERS</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithBootstrapServers(string value)
    {
        _bootstrapServers = value ?? throw new ArgumentNullException(nameof(value));
        _setProperties.Add(nameof(BootstrapServers));
        return this;
    }

    /// <summary>
    /// Sets the consumer group ID. REQUIRED.
    /// </summary>
    /// <param name="value">Kafka consumer group identifier.</param>
    /// <remarks>Can also be set via <c>PROSODY_GROUP_ID</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithGroupId(string value)
    {
        _groupId = value ?? throw new ArgumentNullException(nameof(value));
        _setProperties.Add(nameof(GroupId));
        return this;
    }

    /// <summary>
    /// Sets the topics to subscribe to. REQUIRED.
    /// </summary>
    /// <param name="topics">One or more topic names.</param>
    /// <remarks>Can also be set via <c>PROSODY_SUBSCRIBED_TOPICS</c> environment variable (comma-separated).</remarks>
    public ProsodyClientOptionsBuilder WithSubscribedTopics(params string[] topics)
    {
        _subscribedTopics = topics ?? throw new ArgumentNullException(nameof(topics));
        _setProperties.Add(nameof(SubscribedTopics));
        return this;
    }

    /// <summary>
    /// Sets the allowed event type prefixes.
    /// </summary>
    /// <param name="prefixes">Event type prefixes to allow. Empty array allows all events.</param>
    /// <remarks>
    /// Prefix matching: event passes if event_type starts with any allowed prefix
    /// (e.g., "order." matches "order.created").
    /// </remarks>
    public ProsodyClientOptionsBuilder WithAllowedEvents(params string[] prefixes)
    {
        _allowedEvents = prefixes;
        _setProperties.Add(nameof(AllowedEvents));
        return this;
    }

    /// <summary>
    /// Sets the source system identifier.
    /// </summary>
    /// <param name="value">Source system name for message tracing.</param>
    /// <remarks>Defaults to <see cref="GroupId"/> if not set.</remarks>
    public ProsodyClientOptionsBuilder WithSourceSystem(string value)
    {
        _sourceSystem = value;
        _setProperties.Add(nameof(SourceSystem));
        return this;
    }

    /// <summary>
    /// Enables or disables mock mode for testing.
    /// </summary>
    /// <param name="value">True to use mock client.</param>
    /// <remarks>Can also be set via <c>PROSODY_MOCK</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMock(bool value)
    {
        _mock = value;
        _setProperties.Add(nameof(Mock));
        return this;
    }

    // === Operating Mode Methods ===

    /// <summary>
    /// Sets the processing mode.
    /// </summary>
    /// <param name="mode">The processing mode to use.</param>
    /// <remarks>Can also be set via <c>PROSODY_MODE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMode(ProsodyMode mode)
    {
        _mode = mode;
        _setProperties.Add(nameof(Mode));
        return this;
    }

    // === Concurrency & Limits Methods ===

    /// <summary>
    /// Sets the maximum global concurrency limit.
    /// </summary>
    /// <param name="value">Maximum concurrent handler invocations.</param>
    /// <remarks>Can also be set via <c>PROSODY_MAX_CONCURRENCY</c> environment variable. Default: 32.</remarks>
    public ProsodyClientOptionsBuilder WithMaxConcurrency(int value)
    {
        _maxConcurrency = value;
        _setProperties.Add(nameof(MaxConcurrency));
        return this;
    }

    /// <summary>
    /// Sets the maximum uncommitted messages.
    /// </summary>
    /// <param name="value">Maximum messages before forcing commit.</param>
    /// <remarks>Can also be set via <c>PROSODY_MAX_UNCOMMITTED</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMaxUncommitted(int value)
    {
        _maxUncommitted = value;
        _setProperties.Add(nameof(MaxUncommitted));
        return this;
    }

    /// <summary>
    /// Sets the maximum enqueued messages per key.
    /// </summary>
    /// <param name="value">Maximum messages queued for a single key.</param>
    /// <remarks>Can also be set via <c>PROSODY_MAX_ENQUEUED_PER_KEY</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMaxEnqueuedPerKey(int value)
    {
        _maxEnqueuedPerKey = value;
        _setProperties.Add(nameof(MaxEnqueuedPerKey));
        return this;
    }

    /// <summary>
    /// Sets the idempotence cache size.
    /// </summary>
    /// <param name="value">Cache size. Use 0 to disable idempotence checking.</param>
    /// <remarks>Can also be set via <c>PROSODY_IDEMPOTENCE_CACHE_SIZE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithIdempotenceCacheSize(int value)
    {
        _idempotenceCacheSize = value;
        _setProperties.Add(nameof(IdempotenceCacheSize));
        return this;
    }

    // === Timing Configuration Methods ===

    /// <summary>
    /// Sets the send timeout for the producer.
    /// </summary>
    /// <param name="value">Timeout duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_SEND_TIMEOUT</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithSendTimeout(TimeSpan value)
    {
        _sendTimeout = value;
        _setProperties.Add(nameof(SendTimeout));
        return this;
    }

    /// <summary>
    /// Sets the stall threshold for detecting stuck handlers.
    /// </summary>
    /// <param name="value">Threshold duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_STALL_THRESHOLD</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithStallThreshold(TimeSpan value)
    {
        _stallThreshold = value;
        _setProperties.Add(nameof(StallThreshold));
        return this;
    }

    /// <summary>
    /// Sets the graceful shutdown timeout.
    /// </summary>
    /// <param name="value">Timeout duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_SHUTDOWN_TIMEOUT</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithShutdownTimeout(TimeSpan value)
    {
        _shutdownTimeout = value;
        _setProperties.Add(nameof(ShutdownTimeout));
        return this;
    }

    /// <summary>
    /// Sets the Kafka poll interval.
    /// </summary>
    /// <param name="value">Poll interval duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_POLL_INTERVAL</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithPollInterval(TimeSpan value)
    {
        _pollInterval = value;
        _setProperties.Add(nameof(PollInterval));
        return this;
    }

    /// <summary>
    /// Sets the commit interval.
    /// </summary>
    /// <param name="value">Commit interval duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_COMMIT_INTERVAL</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithCommitInterval(TimeSpan value)
    {
        _commitInterval = value;
        _setProperties.Add(nameof(CommitInterval));
        return this;
    }

    /// <summary>
    /// Sets the handler timeout.
    /// </summary>
    /// <param name="value">Timeout duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_TIMEOUT</c> environment variable. Default: 80% of stall threshold.</remarks>
    public ProsodyClientOptionsBuilder WithTimeout(TimeSpan value)
    {
        _timeout = value;
        _setProperties.Add(nameof(Timeout));
        return this;
    }

    /// <summary>
    /// Sets the timer slab size.
    /// </summary>
    /// <param name="value">Slab size duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_SLAB_SIZE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithSlabSize(TimeSpan value)
    {
        _slabSize = value;
        _setProperties.Add(nameof(SlabSize));
        return this;
    }

    // === Retry Configuration Methods ===

    /// <summary>
    /// Sets the initial retry backoff.
    /// </summary>
    /// <param name="value">Initial backoff duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_RETRY_BASE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithRetryBase(TimeSpan value)
    {
        _retryBase = value;
        _setProperties.Add(nameof(RetryBase));
        return this;
    }

    /// <summary>
    /// Sets the maximum retry delay.
    /// </summary>
    /// <param name="value">Maximum delay duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_MAX_RETRY_DELAY</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMaxRetryDelay(TimeSpan value)
    {
        _maxRetryDelay = value;
        _setProperties.Add(nameof(MaxRetryDelay));
        return this;
    }

    /// <summary>
    /// Sets the maximum retry attempts.
    /// </summary>
    /// <param name="value">Maximum attempts. Use 0 for unlimited retries.</param>
    /// <remarks>Can also be set via <c>PROSODY_MAX_RETRIES</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMaxRetries(int value)
    {
        _maxRetries = value;
        _setProperties.Add(nameof(MaxRetries));
        return this;
    }

    /// <summary>
    /// Sets the failure topic for LowLatency mode.
    /// </summary>
    /// <param name="value">Topic name for failed messages.</param>
    /// <remarks>Only applicable when <see cref="Mode"/> is <see cref="ProsodyMode.LowLatency"/>.</remarks>
    public ProsodyClientOptionsBuilder WithFailureTopic(string value)
    {
        _failureTopic = value;
        _setProperties.Add(nameof(FailureTopic));
        return this;
    }

    // === Health Probe Methods ===

    /// <summary>
    /// Sets the health probe port.
    /// </summary>
    /// <param name="value">Port number. Use -1 to disable the probe.</param>
    /// <remarks>Can also be set via <c>PROSODY_PROBE_PORT</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithProbePort(int value)
    {
        _probePort = value;
        _setProperties.Add(nameof(ProbePort));
        return this;
    }

    // === Cassandra Configuration Methods ===

    /// <summary>
    /// Sets the Cassandra contact nodes.
    /// </summary>
    /// <param name="value">Comma-separated node addresses.</param>
    /// <remarks>Can also be set via <c>PROSODY_CASSANDRA_NODES</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithCassandraNodes(string value)
    {
        _cassandraNodes = value;
        _setProperties.Add(nameof(CassandraNodes));
        return this;
    }

    /// <summary>
    /// Sets the Cassandra keyspace.
    /// </summary>
    /// <param name="value">Keyspace name.</param>
    /// <remarks>Can also be set via <c>PROSODY_CASSANDRA_KEYSPACE</c> environment variable. Default: "prosody".</remarks>
    public ProsodyClientOptionsBuilder WithCassandraKeyspace(string value)
    {
        _cassandraKeyspace = value;
        _setProperties.Add(nameof(CassandraKeyspace));
        return this;
    }

    /// <summary>
    /// Sets the Cassandra datacenter.
    /// </summary>
    /// <param name="value">Datacenter name.</param>
    /// <remarks>Can also be set via <c>PROSODY_CASSANDRA_DATACENTER</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithCassandraDatacenter(string value)
    {
        _cassandraDatacenter = value;
        _setProperties.Add(nameof(CassandraDatacenter));
        return this;
    }

    /// <summary>
    /// Sets the Cassandra rack.
    /// </summary>
    /// <param name="value">Rack name.</param>
    /// <remarks>Can also be set via <c>PROSODY_CASSANDRA_RACK</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithCassandraRack(string value)
    {
        _cassandraRack = value;
        _setProperties.Add(nameof(CassandraRack));
        return this;
    }

    /// <summary>
    /// Sets the Cassandra username.
    /// </summary>
    /// <param name="value">Username for authentication.</param>
    /// <remarks>Can also be set via <c>PROSODY_CASSANDRA_USER</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithCassandraUser(string value)
    {
        _cassandraUser = value;
        _setProperties.Add(nameof(CassandraUser));
        return this;
    }

    /// <summary>
    /// Sets the Cassandra password.
    /// </summary>
    /// <param name="value">Password for authentication.</param>
    /// <remarks>Can also be set via <c>PROSODY_CASSANDRA_PASSWORD</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithCassandraPassword(string value)
    {
        _cassandraPassword = value;
        _setProperties.Add(nameof(CassandraPassword));
        return this;
    }

    /// <summary>
    /// Sets the Cassandra data retention period.
    /// </summary>
    /// <param name="value">Retention duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_CASSANDRA_RETENTION</c> environment variable. Default: 30 days.</remarks>
    public ProsodyClientOptionsBuilder WithCassandraRetention(TimeSpan value)
    {
        _cassandraRetention = value;
        _setProperties.Add(nameof(CassandraRetention));
        return this;
    }

    // === Scheduler Configuration Methods ===

    /// <summary>
    /// Sets the scheduler failure bandwidth weight.
    /// </summary>
    /// <param name="value">Weight between 0.0 and 1.0.</param>
    /// <remarks>Can also be set via <c>PROSODY_SCHEDULER_FAILURE_WEIGHT</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithSchedulerFailureWeight(double value)
    {
        _schedulerFailureWeight = value;
        _setProperties.Add(nameof(SchedulerFailureWeight));
        return this;
    }

    /// <summary>
    /// Sets the scheduler maximum wait urgency ramp-up.
    /// </summary>
    /// <param name="value">Maximum wait duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_SCHEDULER_MAX_WAIT</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithSchedulerMaxWait(TimeSpan value)
    {
        _schedulerMaxWait = value;
        _setProperties.Add(nameof(SchedulerMaxWait));
        return this;
    }

    /// <summary>
    /// Sets the scheduler wait weight.
    /// </summary>
    /// <param name="value">Weight between 0.0 and 1.0.</param>
    /// <remarks>Can also be set via <c>PROSODY_SCHEDULER_WAIT_WEIGHT</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithSchedulerWaitWeight(double value)
    {
        _schedulerWaitWeight = value;
        _setProperties.Add(nameof(SchedulerWaitWeight));
        return this;
    }

    /// <summary>
    /// Sets the scheduler virtual time cache size.
    /// </summary>
    /// <param name="value">Cache size.</param>
    /// <remarks>Can also be set via <c>PROSODY_SCHEDULER_CACHE_SIZE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithSchedulerCacheSize(int value)
    {
        _schedulerCacheSize = value;
        _setProperties.Add(nameof(SchedulerCacheSize));
        return this;
    }

    // === Monopolization Configuration Methods ===

    /// <summary>
    /// Enables or disables monopolization detection.
    /// </summary>
    /// <param name="value">True to enable monopolization detection.</param>
    /// <remarks>Can also be set via <c>PROSODY_MONOPOLIZATION_ENABLED</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMonopolizationEnabled(bool value)
    {
        _monopolizationEnabled = value;
        _setProperties.Add(nameof(MonopolizationEnabled));
        return this;
    }

    /// <summary>
    /// Sets the monopolization threshold.
    /// </summary>
    /// <param name="value">Threshold between 0.0 and 1.0.</param>
    /// <remarks>Can also be set via <c>PROSODY_MONOPOLIZATION_THRESHOLD</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMonopolizationThreshold(double value)
    {
        _monopolizationThreshold = value;
        _setProperties.Add(nameof(MonopolizationThreshold));
        return this;
    }

    /// <summary>
    /// Sets the monopolization detection window.
    /// </summary>
    /// <param name="value">Window duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_MONOPOLIZATION_WINDOW</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMonopolizationWindow(TimeSpan value)
    {
        _monopolizationWindow = value;
        _setProperties.Add(nameof(MonopolizationWindow));
        return this;
    }

    /// <summary>
    /// Sets the monopolization cache size.
    /// </summary>
    /// <param name="value">Cache size.</param>
    /// <remarks>Can also be set via <c>PROSODY_MONOPOLIZATION_CACHE_SIZE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithMonopolizationCacheSize(int value)
    {
        _monopolizationCacheSize = value;
        _setProperties.Add(nameof(MonopolizationCacheSize));
        return this;
    }

    // === Defer Configuration Methods ===

    /// <summary>
    /// Enables or disables message deferral.
    /// </summary>
    /// <param name="value">True to enable deferral.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_ENABLED</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferEnabled(bool value)
    {
        _deferEnabled = value;
        _setProperties.Add(nameof(DeferEnabled));
        return this;
    }

    /// <summary>
    /// Sets the defer base backoff.
    /// </summary>
    /// <param name="value">Initial backoff duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_BASE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferBase(TimeSpan value)
    {
        _deferBase = value;
        _setProperties.Add(nameof(DeferBase));
        return this;
    }

    /// <summary>
    /// Sets the defer maximum delay.
    /// </summary>
    /// <param name="value">Maximum delay duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_MAX_DELAY</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferMaxDelay(TimeSpan value)
    {
        _deferMaxDelay = value;
        _setProperties.Add(nameof(DeferMaxDelay));
        return this;
    }

    /// <summary>
    /// Sets the defer failure threshold.
    /// </summary>
    /// <param name="value">Threshold between 0.0 and 1.0.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_FAILURE_THRESHOLD</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferFailureThreshold(double value)
    {
        _deferFailureThreshold = value;
        _setProperties.Add(nameof(DeferFailureThreshold));
        return this;
    }

    /// <summary>
    /// Sets the defer failure window.
    /// </summary>
    /// <param name="value">Window duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_FAILURE_WINDOW</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferFailureWindow(TimeSpan value)
    {
        _deferFailureWindow = value;
        _setProperties.Add(nameof(DeferFailureWindow));
        return this;
    }

    /// <summary>
    /// Sets the defer cache size.
    /// </summary>
    /// <param name="value">Cache size.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_CACHE_SIZE</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferCacheSize(int value)
    {
        _deferCacheSize = value;
        _setProperties.Add(nameof(DeferCacheSize));
        return this;
    }

    /// <summary>
    /// Sets the defer seek timeout.
    /// </summary>
    /// <param name="value">Timeout duration.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_SEEK_TIMEOUT</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferSeekTimeout(TimeSpan value)
    {
        _deferSeekTimeout = value;
        _setProperties.Add(nameof(DeferSeekTimeout));
        return this;
    }

    /// <summary>
    /// Sets the defer discard threshold.
    /// </summary>
    /// <param name="value">Threshold value.</param>
    /// <remarks>Can also be set via <c>PROSODY_DEFER_DISCARD_THRESHOLD</c> environment variable.</remarks>
    public ProsodyClientOptionsBuilder WithDeferDiscardThreshold(long value)
    {
        _deferDiscardThreshold = value;
        _setProperties.Add(nameof(DeferDiscardThreshold));
        return this;
    }

    // === Query Methods ===

    /// <summary>
    /// Returns true if the specified property was explicitly set.
    /// </summary>
    /// <param name="propertyName">Name of the property to check.</param>
    /// <returns>True if the property was explicitly set via a With* method.</returns>
    public bool IsSet(string propertyName) => _setProperties.Contains(propertyName);

    // === Build ===

    /// <summary>
    /// Builds the configuration options.
    /// </summary>
    /// <returns>An immutable <see cref="ProsodyClientOptions"/> instance.</returns>
    /// <remarks>
    /// Validation is performed by prosody core when the client is created.
    /// Required properties (BootstrapServers, GroupId, SubscribedTopics) are validated at that time.
    /// </remarks>
    public ProsodyClientOptions Build() => new(this);

    // === Internal Accessors (used by ProsodyClientOptions) ===

    internal string? GetBootstrapServers() => _bootstrapServers;
    internal string? GetGroupId() => _groupId;
    internal string[]? GetSubscribedTopics() => _subscribedTopics;
    internal string[]? GetAllowedEvents() => _allowedEvents;
    internal string? GetSourceSystem() => _sourceSystem;
    internal bool? GetMock() => _mock;
    internal ProsodyMode? GetMode() => _mode;
    internal int? GetMaxConcurrency() => _maxConcurrency;
    internal int? GetMaxUncommitted() => _maxUncommitted;
    internal int? GetMaxEnqueuedPerKey() => _maxEnqueuedPerKey;
    internal int? GetIdempotenceCacheSize() => _idempotenceCacheSize;
    internal TimeSpan? GetSendTimeout() => _sendTimeout;
    internal TimeSpan? GetStallThreshold() => _stallThreshold;
    internal TimeSpan? GetShutdownTimeout() => _shutdownTimeout;
    internal TimeSpan? GetPollInterval() => _pollInterval;
    internal TimeSpan? GetCommitInterval() => _commitInterval;
    internal TimeSpan? GetTimeout() => _timeout;
    internal TimeSpan? GetSlabSize() => _slabSize;
    internal TimeSpan? GetRetryBase() => _retryBase;
    internal TimeSpan? GetMaxRetryDelay() => _maxRetryDelay;
    internal int? GetMaxRetries() => _maxRetries;
    internal string? GetFailureTopic() => _failureTopic;
    internal int? GetProbePort() => _probePort;
    internal string? GetCassandraNodes() => _cassandraNodes;
    internal string? GetCassandraKeyspace() => _cassandraKeyspace;
    internal string? GetCassandraDatacenter() => _cassandraDatacenter;
    internal string? GetCassandraRack() => _cassandraRack;
    internal string? GetCassandraUser() => _cassandraUser;
    internal string? GetCassandraPassword() => _cassandraPassword;
    internal TimeSpan? GetCassandraRetention() => _cassandraRetention;
    internal double? GetSchedulerFailureWeight() => _schedulerFailureWeight;
    internal TimeSpan? GetSchedulerMaxWait() => _schedulerMaxWait;
    internal double? GetSchedulerWaitWeight() => _schedulerWaitWeight;
    internal int? GetSchedulerCacheSize() => _schedulerCacheSize;
    internal bool? GetMonopolizationEnabled() => _monopolizationEnabled;
    internal double? GetMonopolizationThreshold() => _monopolizationThreshold;
    internal TimeSpan? GetMonopolizationWindow() => _monopolizationWindow;
    internal int? GetMonopolizationCacheSize() => _monopolizationCacheSize;
    internal bool? GetDeferEnabled() => _deferEnabled;
    internal TimeSpan? GetDeferBase() => _deferBase;
    internal TimeSpan? GetDeferMaxDelay() => _deferMaxDelay;
    internal double? GetDeferFailureThreshold() => _deferFailureThreshold;
    internal TimeSpan? GetDeferFailureWindow() => _deferFailureWindow;
    internal int? GetDeferCacheSize() => _deferCacheSize;
    internal TimeSpan? GetDeferSeekTimeout() => _deferSeekTimeout;
    internal long? GetDeferDiscardThreshold() => _deferDiscardThreshold;

    // Property name constants for use with IsSet
    internal const string BootstrapServers = nameof(BootstrapServers);
    internal const string GroupId = nameof(GroupId);
    internal const string SubscribedTopics = nameof(SubscribedTopics);
    internal const string AllowedEvents = nameof(AllowedEvents);
    internal const string SourceSystem = nameof(SourceSystem);
    internal const string Mock = nameof(Mock);
    internal const string Mode = nameof(Mode);
    internal const string MaxConcurrency = nameof(MaxConcurrency);
    internal const string MaxUncommitted = nameof(MaxUncommitted);
    internal const string MaxEnqueuedPerKey = nameof(MaxEnqueuedPerKey);
    internal const string IdempotenceCacheSize = nameof(IdempotenceCacheSize);
    internal const string SendTimeout = nameof(SendTimeout);
    internal const string StallThreshold = nameof(StallThreshold);
    internal const string ShutdownTimeout = nameof(ShutdownTimeout);
    internal const string PollInterval = nameof(PollInterval);
    internal const string CommitInterval = nameof(CommitInterval);
    internal const string Timeout = nameof(Timeout);
    internal const string SlabSize = nameof(SlabSize);
    internal const string RetryBase = nameof(RetryBase);
    internal const string MaxRetryDelay = nameof(MaxRetryDelay);
    internal const string MaxRetries = nameof(MaxRetries);
    internal const string FailureTopic = nameof(FailureTopic);
    internal const string ProbePort = nameof(ProbePort);
    internal const string CassandraNodes = nameof(CassandraNodes);
    internal const string CassandraKeyspace = nameof(CassandraKeyspace);
    internal const string CassandraDatacenter = nameof(CassandraDatacenter);
    internal const string CassandraRack = nameof(CassandraRack);
    internal const string CassandraUser = nameof(CassandraUser);
    internal const string CassandraPassword = nameof(CassandraPassword);
    internal const string CassandraRetention = nameof(CassandraRetention);
    internal const string SchedulerFailureWeight = nameof(SchedulerFailureWeight);
    internal const string SchedulerMaxWait = nameof(SchedulerMaxWait);
    internal const string SchedulerWaitWeight = nameof(SchedulerWaitWeight);
    internal const string SchedulerCacheSize = nameof(SchedulerCacheSize);
    internal const string MonopolizationEnabled = nameof(MonopolizationEnabled);
    internal const string MonopolizationThreshold = nameof(MonopolizationThreshold);
    internal const string MonopolizationWindow = nameof(MonopolizationWindow);
    internal const string MonopolizationCacheSize = nameof(MonopolizationCacheSize);
    internal const string DeferEnabled = nameof(DeferEnabled);
    internal const string DeferBase = nameof(DeferBase);
    internal const string DeferMaxDelay = nameof(DeferMaxDelay);
    internal const string DeferFailureThreshold = nameof(DeferFailureThreshold);
    internal const string DeferFailureWindow = nameof(DeferFailureWindow);
    internal const string DeferCacheSize = nameof(DeferCacheSize);
    internal const string DeferSeekTimeout = nameof(DeferSeekTimeout);
    internal const string DeferDiscardThreshold = nameof(DeferDiscardThreshold);
}

/// <summary>
/// Immutable configuration for the Prosody client.
/// </summary>
/// <remarks>
/// Create using <see cref="ProsodyClientOptionsBuilder"/> or <see cref="CreateBuilder"/>.
/// </remarks>
public sealed class ProsodyClientOptions
{
    private readonly ProsodyClientOptionsBuilder _builder;

    internal ProsodyClientOptions(ProsodyClientOptionsBuilder builder)
    {
        _builder = builder;
    }

    /// <summary>Creates a new builder for fluent configuration.</summary>
    public static ProsodyClientOptionsBuilder CreateBuilder() => new();

    // === Core Kafka Configuration ===

    /// <summary>Gets the Kafka bootstrap servers. Returns null if not set.</summary>
    public string? BootstrapServers => _builder.GetBootstrapServers();

    /// <summary>Gets the consumer group ID. Returns null if not set.</summary>
    public string? GroupId => _builder.GetGroupId();

    /// <summary>Gets the subscribed topics. Returns null if not set.</summary>
    public IReadOnlyList<string>? SubscribedTopics => _builder.GetSubscribedTopics();

    /// <summary>Gets the allowed event prefixes. Returns null if not set.</summary>
    public IReadOnlyList<string>? AllowedEvents => _builder.IsSet(ProsodyClientOptionsBuilder.AllowedEvents) ? _builder.GetAllowedEvents() : null;

    /// <summary>Gets the source system identifier. Returns null if not set.</summary>
    public string? SourceSystem => _builder.IsSet(ProsodyClientOptionsBuilder.SourceSystem) ? _builder.GetSourceSystem() : null;

    /// <summary>Gets whether mock mode is enabled. Returns null if not set.</summary>
    public bool? Mock => _builder.IsSet(ProsodyClientOptionsBuilder.Mock) ? _builder.GetMock() : null;

    // === Operating Mode ===

    /// <summary>Gets the processing mode. Returns null if not set.</summary>
    public ProsodyMode? Mode => _builder.IsSet(ProsodyClientOptionsBuilder.Mode) ? _builder.GetMode() : null;

    // === Concurrency & Limits ===

    /// <summary>Gets the maximum concurrency. Returns null if not set.</summary>
    public int? MaxConcurrency => _builder.IsSet(ProsodyClientOptionsBuilder.MaxConcurrency) ? _builder.GetMaxConcurrency() : null;

    /// <summary>Gets the maximum uncommitted messages. Returns null if not set.</summary>
    public int? MaxUncommitted => _builder.IsSet(ProsodyClientOptionsBuilder.MaxUncommitted) ? _builder.GetMaxUncommitted() : null;

    /// <summary>Gets the maximum enqueued per key. Returns null if not set.</summary>
    public int? MaxEnqueuedPerKey => _builder.IsSet(ProsodyClientOptionsBuilder.MaxEnqueuedPerKey) ? _builder.GetMaxEnqueuedPerKey() : null;

    /// <summary>Gets the idempotence cache size. Returns null if not set.</summary>
    public int? IdempotenceCacheSize => _builder.IsSet(ProsodyClientOptionsBuilder.IdempotenceCacheSize) ? _builder.GetIdempotenceCacheSize() : null;

    // === Timing Configuration ===

    /// <summary>Gets the send timeout. Returns null if not set.</summary>
    public TimeSpan? SendTimeout => _builder.IsSet(ProsodyClientOptionsBuilder.SendTimeout) ? _builder.GetSendTimeout() : null;

    /// <summary>Gets the stall threshold. Returns null if not set.</summary>
    public TimeSpan? StallThreshold => _builder.IsSet(ProsodyClientOptionsBuilder.StallThreshold) ? _builder.GetStallThreshold() : null;

    /// <summary>Gets the shutdown timeout. Returns null if not set.</summary>
    public TimeSpan? ShutdownTimeout => _builder.IsSet(ProsodyClientOptionsBuilder.ShutdownTimeout) ? _builder.GetShutdownTimeout() : null;

    /// <summary>Gets the poll interval. Returns null if not set.</summary>
    public TimeSpan? PollInterval => _builder.IsSet(ProsodyClientOptionsBuilder.PollInterval) ? _builder.GetPollInterval() : null;

    /// <summary>Gets the commit interval. Returns null if not set.</summary>
    public TimeSpan? CommitInterval => _builder.IsSet(ProsodyClientOptionsBuilder.CommitInterval) ? _builder.GetCommitInterval() : null;

    /// <summary>Gets the handler timeout. Returns null if not set.</summary>
    public TimeSpan? Timeout => _builder.IsSet(ProsodyClientOptionsBuilder.Timeout) ? _builder.GetTimeout() : null;

    /// <summary>Gets the slab size. Returns null if not set.</summary>
    public TimeSpan? SlabSize => _builder.IsSet(ProsodyClientOptionsBuilder.SlabSize) ? _builder.GetSlabSize() : null;

    // === Retry Configuration ===

    /// <summary>Gets the retry base backoff. Returns null if not set.</summary>
    public TimeSpan? RetryBase => _builder.IsSet(ProsodyClientOptionsBuilder.RetryBase) ? _builder.GetRetryBase() : null;

    /// <summary>Gets the maximum retry delay. Returns null if not set.</summary>
    public TimeSpan? MaxRetryDelay => _builder.IsSet(ProsodyClientOptionsBuilder.MaxRetryDelay) ? _builder.GetMaxRetryDelay() : null;

    /// <summary>Gets the maximum retries. Returns null if not set.</summary>
    public int? MaxRetries => _builder.IsSet(ProsodyClientOptionsBuilder.MaxRetries) ? _builder.GetMaxRetries() : null;

    /// <summary>Gets the failure topic. Returns null if not set.</summary>
    public string? FailureTopic => _builder.IsSet(ProsodyClientOptionsBuilder.FailureTopic) ? _builder.GetFailureTopic() : null;

    // === Health Probe ===

    /// <summary>Gets the probe port. Returns null if not set.</summary>
    public int? ProbePort => _builder.IsSet(ProsodyClientOptionsBuilder.ProbePort) ? _builder.GetProbePort() : null;

    // === Cassandra Configuration ===

    /// <summary>Gets the Cassandra nodes. Returns null if not set.</summary>
    public string? CassandraNodes => _builder.IsSet(ProsodyClientOptionsBuilder.CassandraNodes) ? _builder.GetCassandraNodes() : null;

    /// <summary>Gets the Cassandra keyspace. Returns null if not set.</summary>
    public string? CassandraKeyspace => _builder.IsSet(ProsodyClientOptionsBuilder.CassandraKeyspace) ? _builder.GetCassandraKeyspace() : null;

    /// <summary>Gets the Cassandra datacenter. Returns null if not set.</summary>
    public string? CassandraDatacenter => _builder.IsSet(ProsodyClientOptionsBuilder.CassandraDatacenter) ? _builder.GetCassandraDatacenter() : null;

    /// <summary>Gets the Cassandra rack. Returns null if not set.</summary>
    public string? CassandraRack => _builder.IsSet(ProsodyClientOptionsBuilder.CassandraRack) ? _builder.GetCassandraRack() : null;

    /// <summary>Gets the Cassandra username. Returns null if not set.</summary>
    public string? CassandraUser => _builder.IsSet(ProsodyClientOptionsBuilder.CassandraUser) ? _builder.GetCassandraUser() : null;

    /// <summary>Gets the Cassandra password. Returns null if not set.</summary>
    public string? CassandraPassword => _builder.IsSet(ProsodyClientOptionsBuilder.CassandraPassword) ? _builder.GetCassandraPassword() : null;

    /// <summary>Gets the Cassandra retention. Returns null if not set.</summary>
    public TimeSpan? CassandraRetention => _builder.IsSet(ProsodyClientOptionsBuilder.CassandraRetention) ? _builder.GetCassandraRetention() : null;

    // === Scheduler Configuration ===

    /// <summary>Gets the scheduler failure weight. Returns null if not set.</summary>
    public double? SchedulerFailureWeight => _builder.IsSet(ProsodyClientOptionsBuilder.SchedulerFailureWeight) ? _builder.GetSchedulerFailureWeight() : null;

    /// <summary>Gets the scheduler max wait. Returns null if not set.</summary>
    public TimeSpan? SchedulerMaxWait => _builder.IsSet(ProsodyClientOptionsBuilder.SchedulerMaxWait) ? _builder.GetSchedulerMaxWait() : null;

    /// <summary>Gets the scheduler wait weight. Returns null if not set.</summary>
    public double? SchedulerWaitWeight => _builder.IsSet(ProsodyClientOptionsBuilder.SchedulerWaitWeight) ? _builder.GetSchedulerWaitWeight() : null;

    /// <summary>Gets the scheduler cache size. Returns null if not set.</summary>
    public int? SchedulerCacheSize => _builder.IsSet(ProsodyClientOptionsBuilder.SchedulerCacheSize) ? _builder.GetSchedulerCacheSize() : null;

    // === Monopolization Configuration ===

    /// <summary>Gets whether monopolization is enabled. Returns null if not set.</summary>
    public bool? MonopolizationEnabled => _builder.IsSet(ProsodyClientOptionsBuilder.MonopolizationEnabled) ? _builder.GetMonopolizationEnabled() : null;

    /// <summary>Gets the monopolization threshold. Returns null if not set.</summary>
    public double? MonopolizationThreshold => _builder.IsSet(ProsodyClientOptionsBuilder.MonopolizationThreshold) ? _builder.GetMonopolizationThreshold() : null;

    /// <summary>Gets the monopolization window. Returns null if not set.</summary>
    public TimeSpan? MonopolizationWindow => _builder.IsSet(ProsodyClientOptionsBuilder.MonopolizationWindow) ? _builder.GetMonopolizationWindow() : null;

    /// <summary>Gets the monopolization cache size. Returns null if not set.</summary>
    public int? MonopolizationCacheSize => _builder.IsSet(ProsodyClientOptionsBuilder.MonopolizationCacheSize) ? _builder.GetMonopolizationCacheSize() : null;

    // === Defer Configuration ===

    /// <summary>Gets whether deferral is enabled. Returns null if not set.</summary>
    public bool? DeferEnabled => _builder.IsSet(ProsodyClientOptionsBuilder.DeferEnabled) ? _builder.GetDeferEnabled() : null;

    /// <summary>Gets the defer base backoff. Returns null if not set.</summary>
    public TimeSpan? DeferBase => _builder.IsSet(ProsodyClientOptionsBuilder.DeferBase) ? _builder.GetDeferBase() : null;

    /// <summary>Gets the defer max delay. Returns null if not set.</summary>
    public TimeSpan? DeferMaxDelay => _builder.IsSet(ProsodyClientOptionsBuilder.DeferMaxDelay) ? _builder.GetDeferMaxDelay() : null;

    /// <summary>Gets the defer failure threshold. Returns null if not set.</summary>
    public double? DeferFailureThreshold => _builder.IsSet(ProsodyClientOptionsBuilder.DeferFailureThreshold) ? _builder.GetDeferFailureThreshold() : null;

    /// <summary>Gets the defer failure window. Returns null if not set.</summary>
    public TimeSpan? DeferFailureWindow => _builder.IsSet(ProsodyClientOptionsBuilder.DeferFailureWindow) ? _builder.GetDeferFailureWindow() : null;

    /// <summary>Gets the defer cache size. Returns null if not set.</summary>
    public int? DeferCacheSize => _builder.IsSet(ProsodyClientOptionsBuilder.DeferCacheSize) ? _builder.GetDeferCacheSize() : null;

    /// <summary>Gets the defer seek timeout. Returns null if not set.</summary>
    public TimeSpan? DeferSeekTimeout => _builder.IsSet(ProsodyClientOptionsBuilder.DeferSeekTimeout) ? _builder.GetDeferSeekTimeout() : null;

    /// <summary>Gets the defer discard threshold. Returns null if not set.</summary>
    public long? DeferDiscardThreshold => _builder.IsSet(ProsodyClientOptionsBuilder.DeferDiscardThreshold) ? _builder.GetDeferDiscardThreshold() : null;

    // === Query Methods ===

    /// <summary>
    /// Returns true if the specified property was explicitly set.
    /// </summary>
    /// <param name="propertyName">Name of the property to check.</param>
    /// <returns>True if the property was explicitly set via a builder method.</returns>
    public bool IsSet(string propertyName) => _builder.IsSet(propertyName);
}

/// <summary>
/// Prosody processing mode.
/// </summary>
public enum ProsodyMode
{
    /// <summary>
    /// Full ordering guarantees with retry blocking (default).
    /// Messages are processed in order with retries blocking subsequent messages.
    /// </summary>
    Pipeline = 0,

    /// <summary>
    /// Reduced latency with failure topic fallback.
    /// Failed messages are sent to a failure topic instead of blocking retries.
    /// Requires <see cref="ProsodyClientOptionsBuilder.WithFailureTopic"/> to be set.
    /// </summary>
    LowLatency = 1,

    /// <summary>
    /// At-most-once delivery, no retries.
    /// Messages that fail are discarded without retry.
    /// </summary>
    BestEffort = 2
}
