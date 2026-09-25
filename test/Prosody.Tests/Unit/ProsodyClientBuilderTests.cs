using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ProsodyClientBuilder"/> fluent configuration.
/// </summary>
public sealed class ProsodyClientBuilderTests : AsyncDisposalTestBase
{
    /// <summary>A builder for a mock client with the test bootstrap servers and source system.</summary>
    private static ProsodyClientBuilder MockBuilder() =>
        ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithSourceSystem("test")
            .WithMock(true);

    /// <summary>Builds a client from <paramref name="builder"/> and tracks it for disposal.</summary>
    private async Task<ProsodyClient> BuildAsync(ProsodyClientBuilder builder) => Track(await builder.BuildAsync());

    [Fact]
    public void CreateClientReturnsBuilder()
    {
        var builder = ProsodyClientBuilder.Create();
        Assert.NotNull(builder);
        Assert.IsType<ProsodyClientBuilder>(builder);
    }

    [Fact]
    public async Task WithBootstrapServersMultipleServers() =>
        Assert.NotNull(
            await BuildAsync(MockBuilder().WithBootstrapServers("broker1:9092", "broker2:9092", "broker3:9092"))
        );

    [Fact]
    public async Task WithGroupId() => Assert.NotNull(await BuildAsync(MockBuilder().WithGroupId("my-app")));

    [Fact]
    public async Task WithSubscribedTopicsSingleTopic() =>
        Assert.NotNull(await BuildAsync(MockBuilder().WithSubscribedTopics("my-topic")));

    [Fact]
    public async Task WithSubscribedTopicsMultipleTopics() =>
        Assert.NotNull(await BuildAsync(MockBuilder().WithSubscribedTopics("orders", "payments", "notifications")));

    [Fact]
    public async Task WithModeAllModes()
    {
        var pipeline = await BuildAsync(MockBuilder().WithMode(ClientMode.Pipeline));
        var lowLatency = await BuildAsync(
            MockBuilder().WithMode(ClientMode.LowLatency).WithFailureTopic("dead-letters")
        );
        var bestEffort = await BuildAsync(MockBuilder().WithMode(ClientMode.BestEffort));

        Assert.Multiple(
            () => Assert.NotNull(pipeline),
            () => Assert.NotNull(lowLatency),
            () => Assert.NotNull(bestEffort)
        );
    }

    [Fact]
    public async Task WithAllowedEvents() =>
        Assert.NotNull(await BuildAsync(MockBuilder().WithAllowedEvents("user.", "account.")));

    [Fact]
    public async Task WithSourceSystem()
    {
        var client = await BuildAsync(MockBuilder().WithGroupId("my-app").WithSourceSystem("different-source"));
        Assert.Equal("different-source", client.SourceSystem);
    }

    [Fact]
    public async Task WithMaxConcurrency() => Assert.NotNull(await BuildAsync(MockBuilder().WithMaxConcurrency(64)));

    [Fact]
    public async Task WithProbePort()
    {
        var clientEnabled = await BuildAsync(MockBuilder().WithProbePort(8080));
        var clientDisabled = await BuildAsync(MockBuilder().WithProbePort(0));
        Assert.NotNull(clientEnabled);
        Assert.NotNull(clientDisabled);
    }

    [Fact]
    public async Task WithMaxRetries() => Assert.NotNull(await BuildAsync(MockBuilder().WithMaxRetries(5)));

    [Fact]
    public async Task WithFailureTopic() =>
        Assert.NotNull(
            await BuildAsync(MockBuilder().WithMode(ClientMode.LowLatency).WithFailureTopic("dead-letters"))
        );

    [Fact]
    public async Task WithSendTimeout() =>
        Assert.NotNull(await BuildAsync(MockBuilder().WithSendTimeout(TimeSpan.FromSeconds(5))));

    [Fact]
    public async Task BuildSucceedsWithNullOptionalFields() => Assert.NotNull(await BuildAsync(MockBuilder()));

    [Fact]
    public async Task ConfigureAdvancedOptions()
    {
        var builder = MockBuilder()
            .Configure(options =>
            {
                options.MaxUncommitted = 128;
                options.Timeout = TimeSpan.FromMinutes(2);
                options.StallThreshold = TimeSpan.FromMinutes(10);
                options.RetryBase = TimeSpan.FromMilliseconds(50);
                options.MaxRetryDelay = TimeSpan.FromMinutes(10);
            });

        Assert.NotNull(await BuildAsync(builder));
    }

    [Fact]
    public async Task ConfigureDeferralOptions()
    {
        var builder = MockBuilder()
            .Configure(options =>
            {
                options.DeferEnabled = true;
                options.DeferBase = TimeSpan.FromSeconds(2);
                options.DeferMaxDelay = TimeSpan.FromHours(12);
                options.DeferFailureThreshold = 0.8;
                options.DeferFailureWindow = TimeSpan.FromMinutes(10);
                options.LoaderCacheSize = 2048;
            });

        Assert.NotNull(await BuildAsync(builder));
    }

    [Fact]
    public async Task ConfigureMonopolizationOptions()
    {
        var builder = MockBuilder()
            .Configure(options =>
            {
                options.MonopolizationEnabled = true;
                options.MonopolizationThreshold = 0.8;
                options.MonopolizationWindow = TimeSpan.FromMinutes(10);
                options.MonopolizationCacheSize = 4096;
            });

        Assert.NotNull(await BuildAsync(builder));
    }

    [Fact]
    public async Task ConfigureSchedulerOptions()
    {
        var builder = MockBuilder()
            .Configure(options =>
            {
                options.SchedulerFailureWeight = 0.4;
                options.SchedulerMaxWait = TimeSpan.FromMinutes(3);
                options.SchedulerWaitWeight = 150.0;
                options.SchedulerCacheSize = 4096;
            });

        Assert.NotNull(await BuildAsync(builder));
    }

    [Fact]
    public async Task ConfigureCassandraOptions()
    {
        var builder = MockBuilder()
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

        Assert.NotNull(await BuildAsync(builder));
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
    public async Task FullFluentConfiguration()
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

        Assert.NotNull(await BuildAsync(builder));
    }

    [Fact]
    public async Task BuildClonesOptionsSoSubsequentMutationsDoNotAffectClient()
    {
        var builder = MockBuilder().WithSourceSystem("original");

        var client = await BuildAsync(builder);

        // Mutate builder after Build() — should not affect the already-built client
        builder.WithSourceSystem("mutated");

        Assert.Equal("original", client.SourceSystem);
    }

    [Fact]
    public async Task ConditionalConfiguration()
    {
        var isDevelopment = true;

        var builder = ProsodyClientBuilder
            .Create()
            .WithBootstrapServers(TestDefaults.BootstrapServers)
            .WithGroupId("my-app")
            .WithSourceSystem("test");

        if (isDevelopment)
            builder = builder.WithMock(true);

        Assert.NotNull(await BuildAsync(builder));
    }

    [Fact]
    public async Task ForPipelinePreset() => Assert.NotNull(await BuildAsync(MockBuilder().ForPipeline()));

    [Fact]
    public async Task ForLowLatencyPreset() =>
        Assert.NotNull(await BuildAsync(MockBuilder().ForLowLatency("dead-letters")));

    [Fact]
    public async Task ForBestEffortPreset() => Assert.NotNull(await BuildAsync(MockBuilder().ForBestEffort()));

    [Fact]
    public void ForLowLatencyThrowsWhenFailureTopicNull()
    {
        Assert.Throws<ArgumentNullException>(() => ProsodyClientBuilder.Create().ForLowLatency(null!));
    }

    [Fact]
    public async Task PresetCanBeOverriddenBySubsequentCalls()
    {
        var builder = MockBuilder()
            .ForPipeline()
            .WithMaxConcurrency(128)
            .Configure(options => options.DeferEnabled = false);

        Assert.NotNull(await BuildAsync(builder));
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
