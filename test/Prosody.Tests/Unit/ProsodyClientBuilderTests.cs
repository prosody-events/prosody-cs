namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ProsodyClientBuilder"/> fluent configuration.
/// </summary>
public sealed class ProsodyClientBuilderTests
{
    [Fact]
    public void CreateClientReturnsBuilder()
    {
        var builder = CreateClient();

        Assert.NotNull(builder);
        Assert.IsType<ProsodyClientBuilder>(builder);
    }

    [Fact]
    public void WithBootstrapServersSingleServer()
    {
        var builder = CreateClient()
            .WithBootstrapServers("localhost:9092")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithBootstrapServersMultipleServers()
    {
        var builder = CreateClient()
            .WithBootstrapServers("broker1:9092", "broker2:9092", "broker3:9092")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithGroupId()
    {
        var builder = CreateClient().WithGroupId("my-app").WithSourceSystem("test").WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSubscribedTopicsSingleTopic()
    {
        var builder = CreateClient()
            .WithSubscribedTopics("my-topic")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSubscribedTopicsMultipleTopics()
    {
        var builder = CreateClient()
            .WithSubscribedTopics("orders", "payments", "notifications")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithModeAllModes()
    {
        using var pipeline = CreateClient()
            .WithMode(ClientMode.Pipeline)
            .WithSourceSystem("test")
            .WithMock(true)
            .Build();
        using var lowLatency = CreateClient()
            .WithMode(ClientMode.LowLatency)
            .WithSourceSystem("test")
            .WithMock(true)
            .Build();
        using var bestEffort = CreateClient()
            .WithMode(ClientMode.BestEffort)
            .WithSourceSystem("test")
            .WithMock(true)
            .Build();

        Assert.Multiple(
            () => Assert.NotNull(pipeline),
            () => Assert.NotNull(lowLatency),
            () => Assert.NotNull(bestEffort)
        );
    }

    [Fact]
    public void WithAllowedEvents()
    {
        var builder = CreateClient()
            .WithAllowedEvents("user.", "account.")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSourceSystem()
    {
        var builder = CreateClient()
            .WithGroupId("my-app")
            .WithSourceSystem("different-source")
            .WithMock(true);

        using var client = builder.Build();
        Assert.Equal("different-source", client.SourceSystem);
    }

    [Fact]
    public void WithMockTrue()
    {
        var builder = CreateClient().WithSourceSystem("test").WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithMaxConcurrency()
    {
        var builder = CreateClient().WithMaxConcurrency(64).WithSourceSystem("test").WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithMaxUncommitted()
    {
        var builder = CreateClient()
            .WithMaxUncommitted(128)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithTimeout()
    {
        var builder = CreateClient()
            .WithTimeout(TimeSpan.FromMinutes(2))
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithStallThreshold()
    {
        var builder = CreateClient()
            .WithStallThreshold(TimeSpan.FromMinutes(10))
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithProbePort()
    {
        var builderEnabled = CreateClient()
            .WithProbePort(8080)
            .WithSourceSystem("test")
            .WithMock(true);
        var builderDisabled = CreateClient()
            .WithProbePort(0)
            .WithSourceSystem("test")
            .WithMock(true);

        using var clientEnabled = builderEnabled.Build();
        using var clientDisabled = builderDisabled.Build();
        Assert.Multiple(() => Assert.NotNull(clientEnabled), () => Assert.NotNull(clientDisabled));
    }

    [Fact]
    public void WithRetryOptions()
    {
        var builder = CreateClient()
            .WithMaxRetries(5)
            .WithRetryBase(TimeSpan.FromMilliseconds(50))
            .WithMaxRetryDelay(TimeSpan.FromMinutes(10))
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithFailureTopic()
    {
        var builder = CreateClient()
            .WithMode(ClientMode.LowLatency)
            .WithFailureTopic("dead-letters")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithDeferralOptions()
    {
        var builder = CreateClient()
            .WithDeferEnabled(true)
            .WithDeferBase(TimeSpan.FromSeconds(2))
            .WithDeferMaxDelay(TimeSpan.FromHours(12))
            .WithDeferFailureThreshold(0.8)
            .WithDeferFailureWindow(TimeSpan.FromMinutes(10))
            .WithDeferCacheSize(2048)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithMonopolizationOptions()
    {
        var builder = CreateClient()
            .WithMonopolizationEnabled(true)
            .WithMonopolizationThreshold(0.8)
            .WithMonopolizationWindow(TimeSpan.FromMinutes(10))
            .WithMonopolizationCacheSize(4096)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSchedulerOptions()
    {
        var builder = CreateClient()
            .WithSchedulerFailureWeight(0.4)
            .WithSchedulerMaxWait(TimeSpan.FromMinutes(3))
            .WithSchedulerWaitWeight(150.0)
            .WithSchedulerCacheSize(4096)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithCassandraOptions()
    {
        var builder = CreateClient()
            .WithCassandraNodes("cass1:9042", "cass2:9042")
            .WithCassandraKeyspace("my_keyspace")
            .WithCassandraDatacenter("dc1")
            .WithCassandraRack("rack1")
            .WithCassandraUser("user")
            .WithCassandraPassword("pass")
            .WithCassandraRetention(TimeSpan.FromDays(180))
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void BuilderSupportsChainingReassignment()
    {
        var builder1 = CreateClient().WithGroupId("group1");
        var builder2 = builder1.WithGroupId("group2");

        // builder1 and builder2 reference the same mutable builder
        // The pattern supports reassignment for conditional configuration
        Assert.Same(builder1, builder2);
    }

    [Fact]
    public void FullFluentConfiguration()
    {
        var builder = CreateClient()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("my-app")
            .WithSubscribedTopics("orders", "payments")
            .WithMode(ClientMode.Pipeline)
            .WithSourceSystem("my-source")
            .WithMaxConcurrency(64)
            .WithStallThreshold(TimeSpan.FromMinutes(5))
            .WithProbePort(8080)
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConditionalConfiguration()
    {
        var isDevelopment = true;

        var builder = CreateClient()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("my-app")
            .WithSourceSystem("test");

        if (isDevelopment)
            builder = builder.WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }
}
