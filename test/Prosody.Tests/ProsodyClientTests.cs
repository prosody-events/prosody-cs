using Xunit;

namespace Prosody.Tests;

public class ProsodyClientTests
{
    [Fact]
    public async Task Client_CanBeCreated()
    {
        var options = new ProsodyClientOptions
        {
            BootstrapServers = "localhost:9092",
            GroupId = "test-group",
            SubscribedTopics = ["test-topic"]
        };

        await using var client = new ProsodyClient(options);

        Assert.NotNull(client);
    }
}
