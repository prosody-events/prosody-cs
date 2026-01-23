namespace Prosody;

/// <summary>
/// Configuration options for <see cref="ProsodyClient"/>.
/// </summary>
public sealed class ProsodyClientOptions
{
    /// <summary>
    /// Gets or sets the Kafka bootstrap servers.
    /// </summary>
    /// <remarks>
    /// Can also be set via the <c>PROSODY_BOOTSTRAP_SERVERS</c> environment variable.
    /// </remarks>
    public required string BootstrapServers { get; set; }

    /// <summary>
    /// Gets or sets the consumer group ID.
    /// </summary>
    /// <remarks>
    /// Can also be set via the <c>PROSODY_GROUP_ID</c> environment variable.
    /// </remarks>
    public required string GroupId { get; set; }

    /// <summary>
    /// Gets or sets the topics to subscribe to.
    /// </summary>
    /// <remarks>
    /// Can also be set via the <c>PROSODY_SUBSCRIBED_TOPICS</c> environment variable.
    /// </remarks>
    public required IReadOnlyList<string> SubscribedTopics { get; set; }

    /// <summary>
    /// Gets or sets the source system identifier for message tracing.
    /// </summary>
    /// <remarks>
    /// Can also be set via the <c>PROSODY_SOURCE_SYSTEM</c> environment variable.
    /// </remarks>
    public string? SourceSystem { get; set; }

    /// <summary>
    /// Gets or sets the maximum concurrency for message processing.
    /// </summary>
    /// <remarks>
    /// Defaults to 32. Can also be set via the <c>PROSODY_MAX_CONCURRENCY</c> environment variable.
    /// </remarks>
    public int MaxConcurrency { get; set; } = 32;

    /// <summary>
    /// Gets or sets the number of worker threads for the Tokio runtime.
    /// </summary>
    /// <remarks>
    /// Defaults to the number of CPU cores if not specified.
    /// </remarks>
    public int? WorkerThreads { get; set; }

    /// <summary>
    /// Gets or sets the Cassandra configuration for timer persistence.
    /// </summary>
    /// <remarks>
    /// If not set, timers will use in-memory storage only.
    /// </remarks>
    public CassandraOptions? Cassandra { get; set; }
}

/// <summary>
/// Cassandra configuration options for timer persistence.
/// </summary>
public sealed class CassandraOptions
{
    /// <summary>
    /// Gets or sets the Cassandra node addresses.
    /// </summary>
    /// <remarks>
    /// Can also be set via the <c>PROSODY_CASSANDRA_NODES</c> environment variable.
    /// </remarks>
    public required IReadOnlyList<string> Nodes { get; set; }

    /// <summary>
    /// Gets or sets the keyspace name.
    /// </summary>
    /// <remarks>
    /// Defaults to "prosody". Can also be set via the <c>PROSODY_CASSANDRA_KEYSPACE</c> environment variable.
    /// </remarks>
    public string Keyspace { get; set; } = "prosody";
}
