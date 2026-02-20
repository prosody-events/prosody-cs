using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ProsodyServiceCollectionExtensions"/>.
/// </summary>
public sealed class ProsodyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddProsodyClientBindsFromDefaultSection()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:SubscribedTopics:0"] = "orders",
                    ["Prosody:SubscribedTopics:1"] = "payments",
                    ["Prosody:Mode"] = "Pipeline",
                    ["Prosody:MaxConcurrency"] = "64",
                    ["Prosody:Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientBindsFromExplicitSectionPath()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["MyApp:Kafka:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["MyApp:Kafka:GroupId"] = "test-group",
                    ["MyApp:Kafka:Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient("MyApp:Kafka");

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientPostConfigureIsApplied()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "false",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act - PostConfigure overrides Mock to true
        services.AddProsodyClient(options => options.Mock = true);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientRegistersSingleton()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddProsodyClient();

        // Act
        using var provider = services.BuildServiceProvider();
        var client1 = provider.GetRequiredService<ProsodyClient>();
        var client2 = provider.GetRequiredService<ProsodyClient>();

        // Assert - singleton means same instance
        Assert.Same(client1, client2);
    }

    [Fact]
    public void AddProsodyClientSupportsNestedConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = "broker1:9092",
                    ["Prosody:BootstrapServers:1"] = "broker2:9092",
                    ["Prosody:GroupId"] = "nested-group",
                    ["Prosody:SubscribedTopics:0"] = "topic1",
                    ["Prosody:Mode"] = "LowLatency",
                    ["Prosody:FailureTopic"] = "dead-letters",
                    ["Prosody:Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientSupportsTimeSpanConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:Timeout"] = "00:02:00",
                    ["Prosody:StallThreshold"] = "00:10:00",
                    ["Prosody:ShutdownTimeout"] = "00:00:30",
                    ["Prosody:PollInterval"] = "00:00:00.200",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientSupportsEnumConfiguration()
    {
        // Arrange - test all ClientMode enum values
        var pipelineConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:Mode"] = "Pipeline",
                }
            )
            .Build();

        var lowLatencyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:Mode"] = "LowLatency",
                    ["Prosody:FailureTopic"] = "dead-letters",
                }
            )
            .Build();

        var bestEffortConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:Mode"] = "BestEffort",
                }
            )
            .Build();

        var pipelineServices = new ServiceCollection();
        pipelineServices.AddSingleton<IConfiguration>(pipelineConfig);

        var lowLatencyServices = new ServiceCollection();
        lowLatencyServices.AddSingleton<IConfiguration>(lowLatencyConfig);

        var bestEffortServices = new ServiceCollection();
        bestEffortServices.AddSingleton<IConfiguration>(bestEffortConfig);

        // Act
        pipelineServices.AddProsodyClient();
        lowLatencyServices.AddProsodyClient();
        bestEffortServices.AddProsodyClient();

        // Assert
        using var pipelineProvider = pipelineServices.BuildServiceProvider();
        using var lowLatencyProvider = lowLatencyServices.BuildServiceProvider();
        using var bestEffortProvider = bestEffortServices.BuildServiceProvider();

        using var pipelineClient = pipelineProvider.GetRequiredService<ProsodyClient>();
        using var lowLatencyClient = lowLatencyProvider.GetRequiredService<ProsodyClient>();
        using var bestEffortClient = bestEffortProvider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(pipelineClient);
        Assert.NotNull(lowLatencyClient);
        Assert.NotNull(bestEffortClient);
    }

    [Fact]
    public void AddProsodyClientWithConfigureOnly()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient(options =>
        {
            options.BootstrapServers = [TestDefaults.BootstrapServers];
            options.GroupId = "builder-only-group";
            options.SubscribedTopics = ["my-topic"];
            options.Mock = true;
        });

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientPostConfigureOverridesConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = "config-server:9092",
                    ["Prosody:GroupId"] = "config-group",
                    ["Prosody:MaxConcurrency"] = "32",
                    ["Prosody:Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act - PostConfigure overrides MaxConcurrency
        services.AddProsodyClient(options => options.MaxConcurrency = 128);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientEmptyConfigurationWithPostConfigure()
    {
        // Arrange - empty configuration
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act - use PostConfigure to set required mock mode
        services.AddProsodyClient(options =>
        {
            options.Mock = true;
            options.BootstrapServers = [TestDefaults.BootstrapServers];
            options.GroupId = "test";
        });

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientSupportsCassandraConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:CassandraNodes:0"] = "cass1:9042",
                    ["Prosody:CassandraNodes:1"] = "cass2:9042",
                    ["Prosody:CassandraKeyspace"] = "prosody_test",
                    ["Prosody:CassandraDatacenter"] = "dc1",
                    ["Prosody:CassandraUser"] = "testuser",
                    ["Prosody:CassandraPassword"] = "testpass",
                    ["Prosody:CassandraRetention"] = "180.00:00:00",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientSupportsNumericConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:MaxConcurrency"] = "128",
                    ["Prosody:MaxUncommitted"] = "256",
                    ["Prosody:MaxEnqueuedPerKey"] = "16",
                    ["Prosody:IdempotenceCacheSize"] = "8192",
                    ["Prosody:MaxRetries"] = "5",
                    ["Prosody:ProbePort"] = "8080",
                    ["Prosody:DeferCacheSize"] = "2048",
                    ["Prosody:SchedulerCacheSize"] = "16384",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientClonesOptionsToPreventMutation()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddProsodyClient(options =>
        {
            options.BootstrapServers = [TestDefaults.BootstrapServers];
            options.GroupId = "test-group";
            options.Mock = true;
            options.SourceSystem = "original";
        });

        // Act - resolve client first, then mutate the options
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        var resolvedOptions = provider.GetRequiredService<IOptions<ClientOptions>>().Value;
        resolvedOptions.SourceSystem = "mutated";

        // Assert - client should retain original value because it was cloned
        Assert.Equal("original", client.SourceSystem);
    }

    [Fact]
    public void AddProsodyClientSupportsDoubleConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:DeferFailureThreshold"] = "0.85",
                    ["Prosody:MonopolizationThreshold"] = "0.75",
                    ["Prosody:SchedulerFailureWeight"] = "0.4",
                    ["Prosody:SchedulerWaitWeight"] = "150.5",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddProsodyClient();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientOptionsAreBoundViaIOptions()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "options-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:MaxConcurrency"] = "64",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddProsodyClient();

        // Act
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ClientOptions>>().Value;

        // Assert - verify options were bound from configuration
        Assert.Multiple(
            () => Assert.Equal([TestDefaults.BootstrapServers], options.BootstrapServers!),
            () => Assert.Equal("options-group", options.GroupId),
            () => Assert.True(options.Mock),
            () => Assert.Equal(64u, options.MaxConcurrency)
        );
    }

    [Fact]
    public void AddProsodyClientValidateOnStartThrowsForInvalidConfig()
    {
        // Arrange - LowLatency without FailureTopic
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Prosody:BootstrapServers:0"] = TestDefaults.BootstrapServers,
                    ["Prosody:GroupId"] = "test-group",
                    ["Prosody:Mock"] = "true",
                    ["Prosody:Mode"] = "LowLatency",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddProsodyClient();

        // Act & Assert - validation runs eagerly when resolving IOptions<ClientOptions>
        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ClientOptions>>().Value
        );

        Assert.Contains("FailureTopic is required when Mode is LowLatency", ex.Message, StringComparison.Ordinal);
    }
}
