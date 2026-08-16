using System.Text.Json;
using Prosody.Messaging;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Shared fixture for integration tests providing AdminClient and configuration.
/// Initialization completes Cassandra schema migration before parallel test classes start.
/// </summary>
/// <remarks>
/// Configuration via environment variables:
/// PROSODY_BOOTSTRAP_SERVERS (required), PROSODY_CASSANDRA_NODES (required),
/// PROSODY_CASSANDRA_KEYSPACE (default: prosody_test).
/// </remarks>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>Kafka bootstrap servers (from PROSODY_BOOTSTRAP_SERVERS, default: localhost:9094).</summary>
    public static string BootstrapServers { get; } =
        Environment.GetEnvironmentVariable("PROSODY_BOOTSTRAP_SERVERS") ?? "localhost:9094";

    /// <summary>Cassandra contact points (from PROSODY_CASSANDRA_NODES, default: localhost:9042).</summary>
    public static string CassandraNodes { get; } =
        Environment.GetEnvironmentVariable("PROSODY_CASSANDRA_NODES") ?? "localhost:9042";

    /// <summary>Cassandra keyspace (from PROSODY_CASSANDRA_KEYSPACE, default: prosody_test).</summary>
    public static string CassandraKeyspace { get; } =
        Environment.GetEnvironmentVariable("PROSODY_CASSANDRA_KEYSPACE") ?? "prosody_test";

    /// <summary>Default timeout for async operations (30s).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Timer precision tolerance for assertions (1s).</summary>
    public static readonly TimeSpan TimerTolerance = TimeSpan.FromSeconds(1);

    /// <summary>Shared AdminClient instance, initialized during fixture setup.</summary>
    public AdminClient Admin { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        Admin = new AdminClient(BootstrapServers);

        await using var context = await IntegrationTestContext.CreateAsync(Admin);
        await context.Client.SubscribeAsync(new MigrationHandler());
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Admin?.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class MigrationHandler : IProsodyHandler<JsonElement>
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}

/// <summary>Collection for tests that must run sequentially due to shared global state.</summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
public sealed class SequentialTestGroup;
