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

        var client = await CreateClientAsync(groupId, topic, configure);
        return new IntegrationTestContext(sharedAdmin, topic, groupId, client);
    }

    private static async Task<ProsodyClient> CreateClientAsync(
        string groupId,
        string topic,
        Action<ClientOptions>? configure
    )
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
            PeerBindAddress = new(System.Net.IPAddress.Loopback, 0),
        };
        configure?.Invoke(options);
        return await ProsodyClient.CreateAsync(options);
    }

    /// <summary>
    /// Builds a second client bound to this context's topic and group id (not a fresh group), sharing
    /// the same bootstrap and Cassandra options. Identity persistence requires a shared group, so this
    /// is the seam the same-name-different-kind identity-mismatch scenario runs across two clients.
    /// The caller owns the returned client. Dispose it before this context disposes.
    /// </summary>
    /// <param name="configure">Optional callback to override client options before construction.</param>
    /// <returns>A client subscribed to the same topic and group as this context.</returns>
    public Task<ProsodyClient> CreateSiblingClientAsync(Action<ClientOptions>? configure = null) =>
        CreateClientAsync(GroupId, Topic, configure);

    public async ValueTask DisposeAsync()
    {
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
