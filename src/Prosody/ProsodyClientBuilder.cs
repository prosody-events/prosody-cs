namespace Prosody;

/// <summary>
/// Fluent builder for configuring and creating a <see cref="ProsodyClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Prosody.CreateClient"/> (or <see cref="Create"/>) to get a builder instance,
/// then chain configuration methods. Call <see cref="Build"/> when ready to create the client.
/// </para>
/// <para>
/// The builder exposes <c>With*</c> methods for commonly used options. For advanced tuning
/// (retry, deferral, monopolization, scheduler, Cassandra, etc.), use <see cref="Configure"/>
/// to set properties on the underlying <see cref="ClientOptions"/> directly.
/// </para>
/// <example>
/// <code>
/// await using var client = ProsodyClientBuilder.Create()
///     .WithBootstrapServers("localhost:9092")
///     .WithGroupId("my-app")
///     .WithSubscribedTopics("orders", "payments")
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed class ProsodyClientBuilder
{
    private readonly ClientOptions _options = new();

    /// <summary>
    /// Creates a new builder for configuring a <see cref="ProsodyClient"/>.
    /// </summary>
    /// <returns>A new <see cref="ProsodyClientBuilder"/> instance.</returns>
    public static ProsodyClientBuilder Create() => new();

    private ProsodyClientBuilder() { }

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
        _options.BootstrapServers = servers;
        return this;
    }

    /// <summary>
    /// Sets the consumer group ID. Should be set to your application name.
    /// </summary>
    /// <param name="groupId">The consumer group identifier.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithGroupId(string groupId)
    {
        _options.GroupId = groupId;
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
        _options.SubscribedTopics = topics;
        return this;
    }

    /// <summary>
    /// Sets the client operating mode.
    /// </summary>
    /// <param name="mode">The client mode.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMode(ClientMode mode)
    {
        _options.Mode = mode;
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
        _options.AllowedEvents = prefixes;
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
        _options.SourceSystem = sourceSystem;
        return this;
    }

    /// <summary>
    /// Enables or disables the in-memory mock client for testing.
    /// </summary>
    /// <param name="mock">True to use mock client, false for real Kafka.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMock(bool mock)
    {
        _options.Mock = mock;
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
        _options.MaxConcurrency = maxConcurrency;
        return this;
    }

    /// <summary>
    /// Sets the maximum retry attempts.
    /// </summary>
    /// <param name="maxRetries">Maximum retries. Set to 0 for unlimited. Default: 3.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithMaxRetries(uint maxRetries)
    {
        _options.MaxRetries = maxRetries;
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
        _options.FailureTopic = topic;
        return this;
    }

    /// <summary>
    /// Sets the HTTP port for health check probes (/livez, /readyz).
    /// </summary>
    /// <param name="port">Port number. Use 0 to disable the probe server.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithProbePort(ushort port)
    {
        _options.ProbePort = port;
        return this;
    }

    // ========================================================================
    // Producer options
    // ========================================================================

    /// <summary>
    /// Sets the maximum time to wait for message delivery acknowledgment.
    /// Messages not acknowledged within this duration are considered failed.
    /// </summary>
    /// <param name="timeout">The send timeout. Default: 1 second.</param>
    /// <returns>This builder for chaining.</returns>
    public ProsodyClientBuilder WithSendTimeout(TimeSpan timeout)
    {
        _options.SendTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Applies arbitrary configuration to the underlying <see cref="ClientOptions"/>.
    /// </summary>
    /// <param name="configure">An action that modifies the options directly.</param>
    /// <returns>This builder for chaining.</returns>
    /// <remarks>
    /// Use this for advanced tuning options not exposed as <c>With*</c> methods, such as
    /// retry, deferral, monopolization, scheduler, and Cassandra settings.
    /// </remarks>
    /// <example>
    /// <code>
    /// ProsodyClientBuilder.Create()
    ///     .WithBootstrapServers("localhost:9092")
    ///     .WithGroupId("my-app")
    ///     .Configure(options =>
    ///     {
    ///         options.MaxRetries = 5;
    ///         options.CassandraNodes = ["cass1:9042", "cass2:9042"];
    ///         options.CassandraKeyspace = "my_keyspace";
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public ProsodyClientBuilder Configure(Action<ClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_options);
        return this;
    }

    /// <summary>
    /// Creates a new <see cref="ProsodyClient"/> with the configured options.
    /// </summary>
    /// <returns>A new <see cref="ProsodyClient"/> instance.</returns>
    /// <remarks>
    /// This method validates configuration, connects to Kafka, and allocates resources.
    /// The returned client should be disposed when no longer needed.
    /// </remarks>
    public ProsodyClient Build()
    {
        _options.Validate();
        return ProsodyClient.FromValidatedOptions(_options.Clone());
    }
}
