namespace Prosody.Tests;

/// <summary>
/// Centralized test configuration for Prosody integration tests.
/// Values can be overridden via environment variables for CI/CD environments.
/// </summary>
/// <remarks>
/// Reference: ../prosody-rb/spec/support/test_config.rb
/// </remarks>
public static class TestConfiguration
{
    /// <summary>
    /// Kafka bootstrap servers for tests.
    /// Override via PROSODY_BOOTSTRAP_SERVERS environment variable.
    /// Default: localhost:9094 (matches prosody-py docker-compose)
    /// </summary>
    public static string BootstrapServers =>
        Environment.GetEnvironmentVariable("PROSODY_BOOTSTRAP_SERVERS") ?? "localhost:9094";

    /// <summary>
    /// Cassandra contact nodes for tests.
    /// Override via PROSODY_CASSANDRA_NODES environment variable.
    /// Default: localhost:9042
    /// </summary>
    public static string CassandraNodes =>
        Environment.GetEnvironmentVariable("PROSODY_CASSANDRA_NODES") ?? "localhost:9042";

    /// <summary>
    /// Default timeout for integration tests in seconds.
    /// Matches prosody-js and prosody-py test timeouts.
    /// </summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// Timeout for individual message operations in seconds.
    /// Matches prosody-rb message timeout pattern.
    /// </summary>
    public const int MessageTimeoutSeconds = 5;

    /// <summary>
    /// Tolerance for timer drift in seconds.
    /// Timer events may fire slightly before or after the scheduled time.
    /// </summary>
    public const int TimerToleranceSeconds = 1;

    /// <summary>
    /// Creates a <see cref="ProsodyClientOptions"/> configured for testing.
    /// </summary>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <returns>Configured options for test use.</returns>
    public static ProsodyClientOptions CreateConfiguration(string topic, string groupId)
    {
        return ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers(BootstrapServers)
            .WithGroupId(groupId)
            .WithSubscribedTopics(topic)
            .WithCassandraNodes(CassandraNodes)
            .WithMaxConcurrency(1) // Sequential processing for test predictability
            .Build();
    }

    /// <summary>
    /// Creates a cancellation token that expires after the default test timeout.
    /// </summary>
    /// <returns>A cancellation token with configured timeout.</returns>
    public static CancellationToken CreateDefaultTimeoutToken()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
        return cts.Token;
    }

    /// <summary>
    /// Creates a cancellation token source with the default test timeout.
    /// </summary>
    /// <returns>A cancellation token source with configured timeout.</returns>
    public static CancellationTokenSource CreateDefaultTimeoutCts()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
    }
}
