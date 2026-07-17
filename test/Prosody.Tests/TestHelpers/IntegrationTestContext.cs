using Prosody.Configuration;
using Prosody.Messaging;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Per-test isolation context for integration tests.
/// Each test gets its own topic, consumer group, and client.
/// The AdminClient is shared across all tests via the fixture.
/// </summary>
internal sealed class IntegrationTestContext : IAsyncDisposable
{
    private readonly AdminClient _sharedAdmin;

    public string Topic { get; }
    public string GroupId { get; }
    public ProsodyClient Client { get; }

    private IntegrationTestContext(AdminClient sharedAdmin, string topic, string groupId, ProsodyClient client)
    {
        _sharedAdmin = sharedAdmin;
        Topic = topic;
        GroupId = groupId;
        Client = client;
    }

    /// <summary>
    /// Creates a new isolated test context with its own topic and client.
    /// </summary>
    /// <param name="sharedAdmin">Shared admin client.</param>
    /// <param name="configure">Optional callback to override client options before construction.</param>
    public static async Task<IntegrationTestContext> CreateAsync(
        AdminClient sharedAdmin,
        Action<ClientOptions>? configure = null
    )
    {
        var topic = TopicGenerator.GenerateTopicName();
        var groupId = TopicGenerator.GenerateGroupId();

        // Create topic first, before creating the client
        await sharedAdmin.CreateTopicAsync(topic, 4, 1);

        // Retry client creation if topic not yet visible (Kafka metadata propagation delay)
        ProsodyClient? client = null;
        Exception? lastException = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var options = new ClientOptions
                {
                    BootstrapServers = [IntegrationTestFixture.BootstrapServers],
                    GroupId = groupId,
                    SourceSystem = "test-source",
                    SubscribedTopics = [topic],
                    ProbePort = 0,
                    Mode = ClientMode.Pipeline,
                    CassandraNodes = [IntegrationTestFixture.CassandraNodes],
                    CassandraKeyspace = IntegrationTestFixture.CassandraKeyspace,
                };
                configure?.Invoke(options);
                client = new ProsodyClient(options);
                break;
            }
            catch (Exception ex) when (ex.Message.Contains("topics not found", StringComparison.OrdinalIgnoreCase))
            {
                lastException = ex;
                await Task.Delay(100 * (attempt + 1));
            }
        }

        return client is null
            ? throw new InvalidOperationException(
                $"Failed to create client for topic {topic} after retries",
                lastException
            )
            : new IntegrationTestContext(sharedAdmin, topic, groupId, client);
    }

    /// <summary>
    /// Builds a second client bound to this context's topic and group id (not a fresh group), sharing
    /// the same bootstrap and Cassandra options. Identity persistence requires a shared group, so this
    /// is the seam the same-name-different-kind identity-mismatch scenario runs across two clients.
    /// The caller owns the returned client: unsubscribe and dispose it before this context disposes.
    /// </summary>
    /// <param name="configure">Optional callback to override client options before construction.</param>
    /// <returns>A client subscribed to the same topic and group as this context.</returns>
    public async Task<ProsodyClient> CreateSiblingClientAsync(Action<ClientOptions>? configure = null)
    {
        ProsodyClient? client = null;
        Exception? lastException = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var options = new ClientOptions
                {
                    BootstrapServers = [IntegrationTestFixture.BootstrapServers],
                    GroupId = GroupId,
                    SourceSystem = "test-source",
                    SubscribedTopics = [Topic],
                    ProbePort = 0,
                    Mode = ClientMode.Pipeline,
                    CassandraNodes = [IntegrationTestFixture.CassandraNodes],
                    CassandraKeyspace = IntegrationTestFixture.CassandraKeyspace,
                };
                configure?.Invoke(options);
                client = new ProsodyClient(options);
                break;
            }
            catch (Exception ex) when (ex.Message.Contains("topics not found", StringComparison.OrdinalIgnoreCase))
            {
                lastException = ex;
                await Task.Delay(100 * (attempt + 1));
            }
        }

        return client
            ?? throw new InvalidOperationException(
                $"Failed to create sibling client for topic {Topic} after retries",
                lastException
            );
    }

    public async ValueTask DisposeAsync()
    {
        if (await Client.GetConsumerStateAsync() == ConsumerState.Running)
        {
            await Client.UnsubscribeAsync();
        }
        await Client.DisposeAsync();

        try
        {
            await _sharedAdmin.DeleteTopicAsync(Topic);
        }
        catch (InvalidOperationException)
        {
            // Topic may not exist or already be deleted
        }
        catch (TimeoutException)
        {
            // Kafka cluster may be slow during cleanup
        }
    }
}
