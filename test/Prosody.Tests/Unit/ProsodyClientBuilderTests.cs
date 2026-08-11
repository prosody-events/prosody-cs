using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ProsodyClientBuilder"/> fluent configuration.
/// </summary>
public sealed class ProsodyClientBuilderTests : AsyncDisposalTestBase
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
        Assert.NotNull(client);
    }

    [Fact]
    public void WithModeAllModes()
    {
        var pipeline = Track(
            ProsodyClientBuilder
                .Create()
                .WithBootstrapServers(TestDefaults.BootstrapServers)
                .WithMode(ClientMode.Pipeline)
                .WithSourceSystem("test")
                .WithMock(true)
                .Build()
        );
        var lowLatency = Track(
            ProsodyClientBuilder
                .Create()
                .WithBootstrapServers(TestDefaults.BootstrapServers)
                .WithMode(ClientMode.LowLatency)
                .WithFailureTopic("dead-letters")
                .WithSourceSystem("test")
                .WithMock(true)
                .Build()
        );
        var bestEffort = Track(
            ProsodyClientBuilder
                .Create()
                .WithBootstrapServers(TestDefaults.BootstrapServers)
                .WithMode(ClientMode.BestEffort)
                .WithSourceSystem("test")
                .WithMock(true)
                .Build()
        );

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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var clientEnabled = Track(builderEnabled.Build());
        var clientDisabled = Track(builderDisabled.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
        Assert.NotNull(client);
    }

    [Fact]
    public void BuildSucceedsWithNullOptionalFields()
    {
        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true);
        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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
                options.LoaderCacheSize = 2048;
            });

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());
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

        var client = Track(builder.Build());

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

        var client = Track(builder.Build());
        Assert.NotNull(client);
    }

    [Fact]
    public void ForPipelinePreset()
    {
        var client = Track(
            ProsodyClientBuilder
                .Create()
                .WithBootstrapServers(TestDefaults.BootstrapServers)
                .WithSourceSystem("test")
                .WithMock(true)
                .ForPipeline()
                .Build()
        );

        Assert.NotNull(client);
    }

    [Fact]
    public void ForLowLatencyPreset()
    {
        var client = Track(
            ProsodyClientBuilder
                .Create()
                .WithBootstrapServers(TestDefaults.BootstrapServers)
                .WithSourceSystem("test")
                .WithMock(true)
                .ForLowLatency("dead-letters")
                .Build()
        );

        Assert.NotNull(client);
    }

    [Fact]
    public void ForBestEffortPreset()
    {
        var client = Track(
            ProsodyClientBuilder
                .Create()
                .WithBootstrapServers(TestDefaults.BootstrapServers)
                .WithSourceSystem("test")
                .WithMock(true)
                .ForBestEffort()
                .Build()
        );

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
        var client = Track(
            ProsodyClientBuilder
                .Create()
                .WithBootstrapServers(TestDefaults.BootstrapServers)
                .WithSourceSystem("test")
                .WithMock(true)
                .ForPipeline()
                .WithMaxConcurrency(128)
                .Configure(options => options.DeferEnabled = false)
                .Build()
        );

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
