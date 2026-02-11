namespace Prosody;

/// <summary>
/// Fluent builder for configuring and creating a <see cref="ProsodyClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Prosody.CreateClient"/> to get a builder instance, then chain
/// configuration methods. Call <see cref="Build"/> when ready to create the client.
/// </para>
/// <para>
/// The builder is mutable - each <c>With*</c> method modifies the builder and returns
/// it for chaining. If you need to create multiple clients with different configurations
/// from a common base, call <see cref="Prosody.CreateClient"/> separately for each.
/// </para>
/// <example>
/// <code>
/// await using var client = Prosody.CreateClient()
///     .WithBootstrapServers("localhost:9092")
///     .WithGroupId("my-app")
///     .WithSubscribedTopics("orders", "payments")
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed class ProsodyClientBuilder
{
    private ClientOptions _options = new();

    internal ProsodyClientBuilder() { }

    // ========================================================================
    // Core options
    // ========================================================================

    /// <summary>
    /// Sets the Kafka bootstrap servers to connect to.
    /// </summary>
    /// <param name="servers">One or more bootstrap server addresses.</param>
    /// <returns>This builder for chaining.</returns>
    /// <example><c>WithBootstrapServers("localhost:9092")</c> or <c>WithBootstrapServers("broker1:9092", "broker2:9092")</c></example>
    public ProsodyClientBuilder WithBootstrapServers(params string[] servers)
    {
        _options = _options with { BootstrapServers = servers };
        return this;
    }

    /// <summary>
    /// Sets the consumer group ID. Should be set to your application name.
    /// </summary>
    /// <param name="groupId">The consumer group identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithGroupId(string groupId)
    {
        _options = _options with { GroupId = groupId };
        return this;
    }

    /// <summary>
    /// Sets the topics to subscribe to.
    /// </summary>
    /// <param name="topics">One or more topic names.</param>
    /// <returns>This builder for chaining.</returns>
    /// <example><c>WithSubscribedTopics("my-topic")</c> or <c>WithSubscribedTopics("topic1", "topic2")</c></example>
    public ProsodyClientBuilder WithSubscribedTopics(params string[] topics)
    {
        _options = _options with { SubscribedTopics = topics };
        return this;
    }

    /// <summary>
    /// Sets the client operating mode.
    /// </summary>
    /// <param name="mode">The client mode.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMode(ClientMode mode)
    {
        _options = _options with { Mode = mode };
        return this;
    }

    /// <summary>
    /// Sets the allowed event type prefixes. Only events with these prefixes will be processed.
    /// </summary>
    /// <param name="prefixes">Event type prefixes to allow.</param>
    /// <returns>This builder for chaining.</returns>
    /// <example><c>WithAllowedEvents("user.", "account.")</c></example>
    public ProsodyClientBuilder WithAllowedEvents(params string[] prefixes)
    {
        _options = _options with { AllowedEvents = prefixes };
        return this;
    }

    /// <summary>
    /// Sets the source system identifier for outgoing messages.
    /// </summary>
    /// <param name="sourceSystem">The source system identifier.</param>
    /// <returns>This builder for chaining.</returns>
    /// <remarks>
    /// Set this to a different value than the group ID if you need to allow
    /// your application to consume its own produced messages (loopback).
    /// </remarks>
    public ProsodyClientBuilder WithSourceSystem(string sourceSystem)
    {
        _options = _options with { SourceSystem = sourceSystem };
        return this;
    }

    /// <summary>
    /// Enables or disables the in-memory mock client for testing.
    /// </summary>
    /// <param name="mock">True to use mock client, false for real Kafka.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMock(bool mock)
    {
        _options = _options with { Mock = mock };
        return this;
    }

    // ========================================================================
    // Consumer options
    // ========================================================================

    /// <summary>
    /// Sets the maximum number of messages being processed simultaneously.
    /// </summary>
    /// <param name="maxConcurrency">Maximum concurrent messages. Default: 32.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMaxConcurrency(uint maxConcurrency)
    {
        _options = _options with { MaxConcurrency = maxConcurrency };
        return this;
    }

    /// <summary>
    /// Sets the maximum queued messages before pausing consumption.
    /// </summary>
    /// <param name="maxUncommitted">Maximum uncommitted messages. Default: 64.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMaxUncommitted(uint maxUncommitted)
    {
        _options = _options with { MaxUncommitted = maxUncommitted };
        return this;
    }

    /// <summary>
    /// Sets the maximum queued messages per key before pausing.
    /// </summary>
    /// <param name="maxEnqueuedPerKey">Maximum enqueued per key. Default: 8.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMaxEnqueuedPerKey(uint maxEnqueuedPerKey)
    {
        _options = _options with { MaxEnqueuedPerKey = maxEnqueuedPerKey };
        return this;
    }

    /// <summary>
    /// Sets the size of the LRU cache for message deduplication.
    /// </summary>
    /// <param name="cacheSize">Cache size. Set to 0 to disable. Default: 4096.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithIdempotenceCacheSize(uint cacheSize)
    {
        _options = _options with { IdempotenceCacheSize = cacheSize };
        return this;
    }

    /// <summary>
    /// Sets the handler timeout. Handlers running longer than this are cancelled.
    /// </summary>
    /// <param name="timeout">Handler timeout. Default: 80% of stall threshold.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithTimeout(TimeSpan timeout)
    {
        _options = _options with { Timeout = timeout };
        return this;
    }

    /// <summary>
    /// Sets the stall threshold. Reports unhealthy if no progress for this long.
    /// </summary>
    /// <param name="threshold">Stall threshold. Default: 5 minutes.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithStallThreshold(TimeSpan threshold)
    {
        _options = _options with { StallThreshold = threshold };
        return this;
    }

    /// <summary>
    /// Sets the shutdown timeout. Waits this long for in-flight work before force-quit.
    /// </summary>
    /// <param name="timeout">Shutdown timeout. Default: 30 seconds.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithShutdownTimeout(TimeSpan timeout)
    {
        _options = _options with { ShutdownTimeout = timeout };
        return this;
    }

    /// <summary>
    /// Sets how often to fetch new messages from Kafka.
    /// </summary>
    /// <param name="interval">Poll interval. Default: 100ms.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithPollInterval(TimeSpan interval)
    {
        _options = _options with { PollInterval = interval };
        return this;
    }

    /// <summary>
    /// Sets how often to save progress (commit offsets) to Kafka.
    /// </summary>
    /// <param name="interval">Commit interval. Default: 1 second.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithCommitInterval(TimeSpan interval)
    {
        _options = _options with { CommitInterval = interval };
        return this;
    }

    /// <summary>
    /// Sets the HTTP port for health check probes (/livez, /readyz).
    /// </summary>
    /// <param name="port">Port number. Use 0 to disable the probe server.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithProbePort(ushort port)
    {
        _options = _options with { ProbePort = port };
        return this;
    }

    /// <summary>
    /// Sets the timer storage granularity.
    /// </summary>
    /// <param name="slabSize">Slab size. Default: 1 hour.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithSlabSize(TimeSpan slabSize)
    {
        _options = _options with { SlabSize = slabSize };
        return this;
    }

    // ========================================================================
    // Producer options
    // ========================================================================

    /// <summary>
    /// Sets the send timeout. Gives up sending after this long.
    /// </summary>
    /// <param name="timeout">Send timeout. Default: 1 second.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithSendTimeout(TimeSpan timeout)
    {
        _options = _options with { SendTimeout = timeout };
        return this;
    }

    // ========================================================================
    // Retry options
    // ========================================================================

    /// <summary>
    /// Sets the maximum retry attempts.
    /// </summary>
    /// <param name="maxRetries">Maximum retries. Set to 0 for unlimited. Default: 3.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMaxRetries(uint maxRetries)
    {
        _options = _options with { MaxRetries = maxRetries };
        return this;
    }

    /// <summary>
    /// Sets the initial retry delay (exponential backoff base).
    /// </summary>
    /// <param name="retryBase">Initial retry delay. Default: 20ms.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithRetryBase(TimeSpan retryBase)
    {
        _options = _options with { RetryBase = retryBase };
        return this;
    }

    /// <summary>
    /// Sets the maximum delay between retries.
    /// </summary>
    /// <param name="maxDelay">Maximum retry delay. Default: 5 minutes.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMaxRetryDelay(TimeSpan maxDelay)
    {
        _options = _options with { MaxRetryDelay = maxDelay };
        return this;
    }

    /// <summary>
    /// Sets the topic for unprocessable messages (dead letter queue).
    /// Required for <see cref="ClientMode.LowLatency"/> mode.
    /// </summary>
    /// <param name="topic">The failure/dead letter topic name.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithFailureTopic(string topic)
    {
        _options = _options with { FailureTopic = topic };
        return this;
    }

    // ========================================================================
    // Deferral options (Pipeline mode)
    // ========================================================================

    /// <summary>
    /// Enables or disables deferral for failing messages.
    /// </summary>
    /// <param name="enabled">True to enable deferral. Default: true.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferEnabled(bool enabled)
    {
        _options = _options with { DeferEnabled = enabled };
        return this;
    }

    /// <summary>
    /// Sets the initial delay before first deferred retry.
    /// </summary>
    /// <param name="deferBase">Initial defer delay. Default: 1 second.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferBase(TimeSpan deferBase)
    {
        _options = _options with { DeferBase = deferBase };
        return this;
    }

    /// <summary>
    /// Sets the maximum delay for deferred retries.
    /// </summary>
    /// <param name="maxDelay">Maximum defer delay. Default: 24 hours.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferMaxDelay(TimeSpan maxDelay)
    {
        _options = _options with { DeferMaxDelay = maxDelay };
        return this;
    }

    /// <summary>
    /// Sets the failure rate threshold for disabling deferral.
    /// </summary>
    /// <param name="threshold">Threshold (0.0-1.0). Default: 0.9 (90%).</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferFailureThreshold(double threshold)
    {
        _options = _options with { DeferFailureThreshold = threshold };
        return this;
    }

    /// <summary>
    /// Sets the time window for measuring failure rate.
    /// </summary>
    /// <param name="window">Measurement window. Default: 5 minutes.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferFailureWindow(TimeSpan window)
    {
        _options = _options with { DeferFailureWindow = window };
        return this;
    }

    /// <summary>
    /// Sets the number of deferred keys to track in memory.
    /// </summary>
    /// <param name="cacheSize">Cache size. Default: 1024.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferCacheSize(uint cacheSize)
    {
        _options = _options with { DeferCacheSize = cacheSize };
        return this;
    }

    /// <summary>
    /// Sets the timeout when loading deferred messages from Kafka.
    /// </summary>
    /// <param name="timeout">Seek timeout. Default: 30 seconds.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferSeekTimeout(TimeSpan timeout)
    {
        _options = _options with { DeferSeekTimeout = timeout };
        return this;
    }

    /// <summary>
    /// Sets the read optimization threshold for deferral.
    /// </summary>
    /// <param name="threshold">Discard threshold. Default: 100.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithDeferDiscardThreshold(uint threshold)
    {
        _options = _options with { DeferDiscardThreshold = threshold };
        return this;
    }

    // ========================================================================
    // Monopolization detection options (Pipeline mode)
    // ========================================================================

    /// <summary>
    /// Enables or disables hot key protection.
    /// </summary>
    /// <param name="enabled">True to enable protection. Default: true.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMonopolizationEnabled(bool enabled)
    {
        _options = _options with { MonopolizationEnabled = enabled };
        return this;
    }

    /// <summary>
    /// Sets the threshold for rejecting monopolizing keys.
    /// </summary>
    /// <param name="threshold">Threshold (0.0-1.0). Default: 0.9 (90%).</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMonopolizationThreshold(double threshold)
    {
        _options = _options with { MonopolizationThreshold = threshold };
        return this;
    }

    /// <summary>
    /// Sets the measurement window for monopolization detection.
    /// </summary>
    /// <param name="window">Measurement window. Default: 5 minutes.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMonopolizationWindow(TimeSpan window)
    {
        _options = _options with { MonopolizationWindow = window };
        return this;
    }

    /// <summary>
    /// Sets the maximum distinct keys to track for monopolization.
    /// </summary>
    /// <param name="cacheSize">Cache size. Default: 8192.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMonopolizationCacheSize(uint cacheSize)
    {
        _options = _options with { MonopolizationCacheSize = cacheSize };
        return this;
    }

    // ========================================================================
    // Fair scheduling options (all modes)
    // ========================================================================

    /// <summary>
    /// Sets the fraction of processing time reserved for retries.
    /// </summary>
    /// <param name="weight">Weight (0.0-1.0). Default: 0.3 (30%).</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithSchedulerFailureWeight(double weight)
    {
        _options = _options with { SchedulerFailureWeight = weight };
        return this;
    }

    /// <summary>
    /// Sets the wait time at which messages get maximum priority boost.
    /// </summary>
    /// <param name="maxWait">Maximum wait time. Default: 2 minutes.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithSchedulerMaxWait(TimeSpan maxWait)
    {
        _options = _options with { SchedulerMaxWait = maxWait };
        return this;
    }

    /// <summary>
    /// Sets the priority boost multiplier for waiting messages.
    /// </summary>
    /// <param name="weight">Weight multiplier. Default: 200.0.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithSchedulerWaitWeight(double weight)
    {
        _options = _options with { SchedulerWaitWeight = weight };
        return this;
    }

    /// <summary>
    /// Sets the maximum distinct keys to track in the scheduler.
    /// </summary>
    /// <param name="cacheSize">Cache size. Default: 8192.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithSchedulerCacheSize(uint cacheSize)
    {
        _options = _options with { SchedulerCacheSize = cacheSize };
        return this;
    }

    // ========================================================================
    // Cassandra options (required for timers in non-mock mode)
    // ========================================================================

    /// <summary>
    /// Sets the Cassandra contact nodes.
    /// </summary>
    /// <param name="nodes">One or more Cassandra node addresses.</param>
    /// <returns>This builder for chaining.</returns>
    /// <example><c>WithCassandraNodes("localhost:9042")</c> or <c>WithCassandraNodes("cass1:9042", "cass2:9042")</c></example>
    public ProsodyClientBuilder WithCassandraNodes(params string[] nodes)
    {
        _options = _options with { CassandraNodes = nodes };
        return this;
    }

    /// <summary>
    /// Sets the Cassandra keyspace name.
    /// </summary>
    /// <param name="keyspace">Keyspace name. Default: "prosody".</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithCassandraKeyspace(string keyspace)
    {
        _options = _options with { CassandraKeyspace = keyspace };
        return this;
    }

    /// <summary>
    /// Sets the Cassandra datacenter for queries.
    /// </summary>
    /// <param name="datacenter">Datacenter name.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithCassandraDatacenter(string datacenter)
    {
        _options = _options with { CassandraDatacenter = datacenter };
        return this;
    }

    /// <summary>
    /// Sets the Cassandra rack for queries.
    /// </summary>
    /// <param name="rack">Rack name.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithCassandraRack(string rack)
    {
        _options = _options with { CassandraRack = rack };
        return this;
    }

    /// <summary>
    /// Sets the Cassandra username.
    /// </summary>
    /// <param name="user">Username.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithCassandraUser(string user)
    {
        _options = _options with { CassandraUser = user };
        return this;
    }

    /// <summary>
    /// Sets the Cassandra password.
    /// </summary>
    /// <param name="password">Password.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithCassandraPassword(string password)
    {
        _options = _options with { CassandraPassword = password };
        return this;
    }

    /// <summary>
    /// Sets the retention period for timer data in Cassandra.
    /// </summary>
    /// <param name="retention">Retention period. Default: 1 year.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithCassandraRetention(TimeSpan retention)
    {
        _options = _options with { CassandraRetention = retention };
        return this;
    }

    // ========================================================================
    // Build
    // ========================================================================

    /// <summary>
    /// Creates a new <see cref="ProsodyClient"/> with the configured options.
    /// </summary>
    /// <returns>A new <see cref="ProsodyClient"/> instance.</returns>
    /// <remarks>
    /// This method validates configuration, connects to Kafka, and allocates resources.
    /// The returned client should be disposed when no longer needed.
    /// </remarks>
    public ProsodyClient Build() => new(_options);
}
