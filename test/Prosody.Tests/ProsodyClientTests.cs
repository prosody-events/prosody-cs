using Xunit;

namespace Prosody.Tests;

/// <summary>
/// Placeholder tests for ProsodyClient functionality.
/// Real integration tests will be added in Phase 5 (T065-T069) after
/// ProsodyClientService is fully implemented in Phase 4.
/// </summary>
public class ProsodyClientTests
{
    [Fact]
    public void Options_CanBeCreatedViaBuilder()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("test-group")
            .WithSubscribedTopics("test-topic")
            .Build();

        Assert.Equal("localhost:9092", options.BootstrapServers);
        Assert.Equal("test-group", options.GroupId);
        Assert.NotNull(options.SubscribedTopics);
        Assert.Single(options.SubscribedTopics);
    }

    [Fact]
    public void Options_IsSet_ReturnsTrueForSetProperties()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("test-group")
            .WithSubscribedTopics("test-topic")
            .Build();

        Assert.True(options.IsSet("BootstrapServers"));
        Assert.True(options.IsSet("GroupId"));
        Assert.True(options.IsSet("SubscribedTopics"));
        Assert.False(options.IsSet("MaxConcurrency"));
    }
}
