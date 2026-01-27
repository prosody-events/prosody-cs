using Prosody.Tests.TestHelpers;
using Xunit;

namespace Prosody.Tests;

/// <summary>
/// Base class for integration tests with proper setup/teardown.
/// Provides unique topic/group per test and test timeout management.
/// </summary>
/// <remarks>
/// Reference: ../prosody-rb/spec/client_spec.rb, ../prosody-py/tests/test_prosody.py
///
/// Note: AdminClient-based topic creation will be added in Phase 9 (T070-T075).
/// For now, tests should use pre-existing topics or rely on auto-creation.
/// </remarks>
[Collection("Integration")]  // Prevents parallel execution of integration tests
public abstract class IntegrationTestBase : IAsyncLifetime
{
    /// <summary>
    /// Gets the unique topic name for this test.
    /// </summary>
    protected string TopicName { get; private set; } = null!;

    /// <summary>
    /// Gets the unique group ID for this test.
    /// </summary>
    protected string GroupId { get; private set; } = null!;

    /// <summary>
    /// Gets the cancellation token for test timeout.
    /// </summary>
    protected CancellationToken TestTimeout { get; private set; }

    private CancellationTokenSource _cts = null!;

    /// <inheritdoc />
    public virtual Task InitializeAsync()
    {
        // Create timeout token (30s default per sibling patterns)
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(TestConfiguration.DefaultTimeoutSeconds));
        TestTimeout = _cts.Token;

        // Generate unique topic/group per test
        TopicName = TopicGenerator.GenerateTopicName();
        GroupId = TopicGenerator.GenerateGroupId();

        // Note: Topic creation via AdminClient will be added in Phase 9
        // For now, tests may need to handle auto-topic-creation or use existing topics

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task DisposeAsync()
    {
        // Note: Topic cleanup via AdminClient will be added in Phase 9
        _cts?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a configured client options for this test.
    /// </summary>
    /// <returns>Client options configured with test topic and group.</returns>
    protected ProsodyClientOptions CreateClientOptions()
        => TestConfiguration.CreateConfiguration(TopicName, GroupId);

    /// <summary>
    /// Creates a configured client options builder for this test.
    /// Use this when you need to customize options beyond the defaults.
    /// </summary>
    /// <returns>A builder pre-configured with test topic and group.</returns>
    protected ProsodyClientOptionsBuilder CreateClientOptionsBuilder()
    {
        return ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers(TestConfiguration.BootstrapServers)
            .WithGroupId(GroupId)
            .WithSubscribedTopics(TopicName)
            .WithCassandraNodes(TestConfiguration.CassandraNodes);
    }
}
