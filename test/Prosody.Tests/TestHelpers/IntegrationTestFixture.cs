using System.Diagnostics.CodeAnalysis;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Shared fixture for integration tests that manages AdminClient lifecycle.
/// </summary>
/// <remarks>
/// This fixture is shared across all integration tests in a collection to avoid
/// creating multiple AdminClient instances. The AdminClient is expensive to create
/// and can be safely shared across tests.
///
/// Configuration is read from environment variables (matching the prosody library):
/// - PROSODY_BOOTSTRAP_SERVERS: Kafka bootstrap servers (required)
/// - PROSODY_CASSANDRA_NODES: Cassandra contact points (required)
/// - PROSODY_CASSANDRA_KEYSPACE: Cassandra keyspace name (default: prosody_test)
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires collection fixtures to be public"
)]
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Bootstrap servers for Kafka. Read from PROSODY_BOOTSTRAP_SERVERS environment variable.
    /// </summary>
    public static string BootstrapServers { get; } =
        Environment.GetEnvironmentVariable("PROSODY_BOOTSTRAP_SERVERS")
        ?? throw new InvalidOperationException(
            "PROSODY_BOOTSTRAP_SERVERS environment variable is required. "
                + "Set it to your Kafka bootstrap servers (e.g., 'localhost:9094')."
        );

    /// <summary>
    /// Cassandra nodes for timer storage. Read from PROSODY_CASSANDRA_NODES environment variable.
    /// </summary>
    public static string CassandraNodes { get; } =
        Environment.GetEnvironmentVariable("PROSODY_CASSANDRA_NODES")
        ?? throw new InvalidOperationException(
            "PROSODY_CASSANDRA_NODES environment variable is required. "
                + "Set it to your Cassandra contact points (e.g., 'localhost:9042')."
        );

    /// <summary>
    /// Cassandra keyspace for tests. Read from PROSODY_CASSANDRA_KEYSPACE environment variable.
    /// </summary>
    public static string CassandraKeyspace { get; } =
        Environment.GetEnvironmentVariable("PROSODY_CASSANDRA_KEYSPACE") ?? "prosody_test";

    /// <summary>
    /// Default timeout for async operations.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timer precision tolerance for tests.
    /// </summary>
    public static readonly TimeSpan TimerTolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the shared AdminClient instance.
    /// </summary>
    public AdminClient Admin { get; private set; } = null!;

    /// <inheritdoc/>
    public ValueTask InitializeAsync()
    {
        Admin = new AdminClient(BootstrapServers);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Admin.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Collection for tests that must run sequentially (no parallelization).
/// Used for tests that contend over shared global state like logging.
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires collection definitions to be public"
)]
public sealed class SequentialTestGroup;
