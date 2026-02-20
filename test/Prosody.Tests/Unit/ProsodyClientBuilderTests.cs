using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ProsodyClientBuilder"/> fluent configuration.
/// </summary>
public sealed class ProsodyClientBuilderTests
{
    [Fact]
    public void CreateClientReturnsBuilder()
    {
        var builder = ProsodyClientBuilder.Create();
        Assert.NotNull(builder);
        Assert.IsType<ProsodyClientBuilder>(builder);
    }

    [Fact]
    public void WithBootstrapServersSingleServer()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithBootstrapServersMultipleServers()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers("broker1:9092", "broker2:9092", "broker3:9092")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithGroupId()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithGroupId("my-app")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSubscribedTopicsSingleTopic()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSubscribedTopics("my-topic")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSubscribedTopicsMultipleTopics()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSubscribedTopics("orders", "payments", "notifications")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithModeAllModes()
    {
        using var pipeline = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithMode(ClientMode.Pipeline)
            .WithSourceSystem("test")
            .WithMock(true)
            .Build();
        using var lowLatency = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithMode(ClientMode.LowLatency)
            .WithFailureTopic("dead-letters")
            .WithSourceSystem("test")
            .WithMock(true)
            .Build();
        using var bestEffort = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
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
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithAllowedEvents("user.", "account.")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSourceSystem()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithGroupId("my-app")
            .WithSourceSystem("different-source")
            .WithMock(true);

        using var client = builder.Build();
        Assert.Equal("different-source", client.SourceSystem);
    }

    [Fact]
    public void WithMockTrue()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithMaxConcurrency()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithMaxConcurrency(64)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithProbePort()
    {
        var builderEnabled = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithProbePort(8080)
            .WithSourceSystem("test")
            .WithMock(true);
        var builderDisabled = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithProbePort(0)
            .WithSourceSystem("test")
            .WithMock(true);

        using var clientEnabled = builderEnabled.Build();
        using var clientDisabled = builderDisabled.Build();
        Assert.NotNull(clientEnabled);
        Assert.NotNull(clientDisabled);
    }

    [Fact]
    public void WithMaxRetries()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithMaxRetries(5)
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithFailureTopic()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithMode(ClientMode.LowLatency)
            .WithFailureTopic("dead-letters")
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void WithSendTimeout()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSendTimeout(TimeSpan.FromSeconds(5))
            .WithSourceSystem("test")
            .WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void BuildThrowsWhenLowLatencyWithoutFailureTopic()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithMode(ClientMode.LowLatency)
            .WithSourceSystem("test")
            .WithMock(true);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("FailureTopic", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildThrowsWhenBootstrapServersEmpty()
    {
        var builder = ProsodyClientBuilder.Create().WithBootstrapServers().WithSourceSystem("test").WithMock(true);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("BootstrapServers", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildThrowsWhenSubscribedTopicsEmpty()
    {
        var builder = ProsodyClientBuilder.Create().WithSubscribedTopics().WithSourceSystem("test").WithMock(true);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("SubscribedTopics", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildThrowsWhenThresholdOutOfRange()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithSourceSystem("test")
            .WithMock(true)
            .Configure(options => options.DeferFailureThreshold = 1.5);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("DeferFailureThreshold", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSucceedsWithNullOptionalFields()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true);
        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConfigureAdvancedOptions()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .Configure(options =>
            {
                options.MaxUncommitted = 128;
                options.Timeout = TimeSpan.FromMinutes(2);
                options.StallThreshold = TimeSpan.FromMinutes(10);
                options.RetryBase = TimeSpan.FromMilliseconds(50);
                options.MaxRetryDelay = TimeSpan.FromMinutes(10);
            });

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConfigureDeferralOptions()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .Configure(options =>
            {
                options.DeferEnabled = true;
                options.DeferBase = TimeSpan.FromSeconds(2);
                options.DeferMaxDelay = TimeSpan.FromHours(12);
                options.DeferFailureThreshold = 0.8;
                options.DeferFailureWindow = TimeSpan.FromMinutes(10);
                options.DeferCacheSize = 2048;
            });

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConfigureMonopolizationOptions()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .Configure(options =>
            {
                options.MonopolizationEnabled = true;
                options.MonopolizationThreshold = 0.8;
                options.MonopolizationWindow = TimeSpan.FromMinutes(10);
                options.MonopolizationCacheSize = 4096;
            });

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConfigureSchedulerOptions()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .Configure(options =>
            {
                options.SchedulerFailureWeight = 0.4;
                options.SchedulerMaxWait = TimeSpan.FromMinutes(3);
                options.SchedulerWaitWeight = 150.0;
                options.SchedulerCacheSize = 4096;
            });

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void ConfigureCassandraOptions()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .Configure(options =>
            {
                options.CassandraNodes = ["cass1:9042", "cass2:9042"];
                options.CassandraKeyspace = "my_keyspace";
                options.CassandraDatacenter = "dc1";
                options.CassandraRack = "rack1";
                options.CassandraUser = "user";
                options.CassandraPassword = "pass";
                options.CassandraRetention = TimeSpan.FromDays(180);
            });

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void BuilderSupportsChainingReassignment()
    {
        var builder1 = ProsodyClientBuilder.Create().WithGroupId("group1");
        var builder2 = builder1.WithGroupId("group2");

        // builder1 and builder2 reference the same mutable builder
        // The pattern supports reassignment for conditional configuration
        Assert.Same(builder1, builder2);
    }

    [Fact]
    public void FullFluentConfiguration()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithGroupId("my-app")
            .WithSubscribedTopics("orders", "payments")
            .WithMode(ClientMode.Pipeline)
            .WithSourceSystem("my-source")
            .WithMaxConcurrency(64)
            .WithProbePort(8080)
            .WithMock(true)
            .Configure(options =>
            {
                options.StallThreshold = TimeSpan.FromMinutes(5);
            });

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void BuildClonesOptionsSoSubsequentMutationsDoNotAffectClient()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("original")
            .WithMock(true);

        using var client = builder.Build();

        // Mutate builder after Build() — should not affect the already-built client
        builder.WithSourceSystem("mutated");

        Assert.Equal("original", client.SourceSystem);
    }

    [Fact]
    public void ConditionalConfiguration()
    {
        var isDevelopment = true;

        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithGroupId("my-app")
            .WithSourceSystem("test");

        if (isDevelopment)
            builder = builder.WithMock(true);

        using var client = builder.Build();
        Assert.NotNull(client);
    }

    [Fact]
    public void ForPipelinePreset()
    {
        using var client = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .ForPipeline()
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void ForLowLatencyPreset()
    {
        using var client = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .ForLowLatency("dead-letters")
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void ForBestEffortPreset()
    {
        using var client = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .ForBestEffort()
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void ForLowLatencyThrowsWhenFailureTopicNull()
    {
        Assert.Throws<ArgumentNullException>(() => ProsodyClientBuilder.Create().ForLowLatency(null!));
    }

    [Fact]
    public void PresetCanBeOverriddenBySubsequentCalls()
    {
        using var client = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true)
            .ForPipeline()
            .WithMaxConcurrency(128)
            .Configure(options => options.DeferEnabled = false)
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void PresetReturnsSameBuilderForChaining()
    {
        var builder = ProsodyClientBuilder.Create();

        Assert.Multiple(
            () => Assert.Same(builder, builder.ForPipeline()),
            () => Assert.Same(builder, builder.ForBestEffort()),
            () => Assert.Same(builder, builder.ForLowLatency("dlq"))
        );
    }
}
