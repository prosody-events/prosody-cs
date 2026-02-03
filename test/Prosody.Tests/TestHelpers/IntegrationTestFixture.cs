using System.Diagnostics.CodeAnalysis;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Shared fixture for integration tests that manages AdminClient lifecycle.
/// </summary>
/// <remarks>
/// This fixture is shared across all integration tests in a collection to avoid
/// creating multiple AdminClient instances. The AdminClient is expensive to create
/// and can be safely shared across tests.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires collection fixtures to be public"
)]
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Bootstrap servers for Kafka.
    /// </summary>
    public const string BootstrapServers = "localhost:9094";

    /// <summary>
    /// Cassandra nodes for timer storage.
    /// </summary>
    public const string CassandraNodes = "localhost:9042";

    /// <summary>
    /// Cassandra keyspace for tests.
    /// </summary>
    public const string CassandraKeyspace = "prosody_test";

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
    public Task InitializeAsync()
    {
        Admin = new AdminClient(BootstrapServers);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisposeAsync()
    {
        Admin.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Collection definition for integration tests.
/// </summary>
/// <remarks>
/// All integration test classes should use this collection to share the fixture.
/// </remarks>
[CollectionDefinition(Name)]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires collection definitions to be public"
)]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Collection suffix is required by xUnit convention"
)]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    public const string Name = "Integration";
}
