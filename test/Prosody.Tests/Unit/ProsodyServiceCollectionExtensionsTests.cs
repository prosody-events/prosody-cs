using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for <see cref="ProsodyServiceCollectionExtensions"/>.
/// </summary>
public sealed class ProsodyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddProsodyClientBindsFromConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["BootstrapServers:0"] = "localhost:9092",
                    ["GroupId"] = "test-group",
                    ["SubscribedTopics:0"] = "orders",
                    ["SubscribedTopics:1"] = "payments",
                    ["Mode"] = "Pipeline",
                    ["MaxConcurrency"] = "64",
                    ["Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddProsodyClient(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientWithBuilderCustomization()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["BootstrapServers:0"] = "localhost:9092",
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();
        var builderCalled = false;

        // Act
        services.AddProsodyClient(
            configuration,
            builder =>
            {
                builderCalled = true;
                builder.WithMaxConcurrency(128);
            }
        );

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.True(builderCalled);
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
                    ["BootstrapServers:0"] = "localhost:9092",
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddProsodyClient(configuration);

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
        // Arrange - simulates nested config like "Prosody:BootstrapServers"
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

        // Act - use GetSection to get the nested section
        services.AddProsodyClient(configuration.GetSection("Prosody"));

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
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                    ["Timeout"] = "00:02:00", // 2 minutes
                    ["StallThreshold"] = "00:10:00", // 10 minutes
                    ["ShutdownTimeout"] = "00:00:30", // 30 seconds
                    ["PollInterval"] = "00:00:00.200", // 200ms
                }
            )
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddProsodyClient(configuration);

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
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                    ["Mode"] = "Pipeline",
                }
            )
            .Build();

        var lowLatencyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                    ["Mode"] = "LowLatency",
                    ["FailureTopic"] = "dead-letters",
                }
            )
            .Build();

        var bestEffortConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                    ["Mode"] = "BestEffort",
                }
            )
            .Build();

        var pipelineServices = new ServiceCollection();
        var lowLatencyServices = new ServiceCollection();
        var bestEffortServices = new ServiceCollection();

        // Act
        pipelineServices.AddProsodyClient(pipelineConfig);
        lowLatencyServices.AddProsodyClient(lowLatencyConfig);
        bestEffortServices.AddProsodyClient(bestEffortConfig);

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
    public void AddProsodyClientWithBuilderOnly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddProsodyClient(builder =>
            builder
                .WithBootstrapServers("localhost:9092")
                .WithGroupId("builder-only-group")
                .WithSubscribedTopics("my-topic")
                .WithMock(true)
        );

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientBuilderOverridesConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["BootstrapServers:0"] = "config-server:9092",
                    ["GroupId"] = "config-group",
                    ["MaxConcurrency"] = "32",
                    ["Mock"] = "true",
                }
            )
            .Build();

        var services = new ServiceCollection();

        // Act - builder overrides MaxConcurrency
        services.AddProsodyClient(configuration, builder => builder.WithMaxConcurrency(128));

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientEmptyConfigurationUsesDefaults()
    {
        // Arrange - empty configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        // Act - use builder to set required mock mode
        services.AddProsodyClient(
            configuration,
            builder => builder.WithMock(true).WithGroupId("test")
        );

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
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                    ["CassandraNodes:0"] = "cass1:9042",
                    ["CassandraNodes:1"] = "cass2:9042",
                    ["CassandraKeyspace"] = "prosody_test",
                    ["CassandraDatacenter"] = "dc1",
                    ["CassandraUser"] = "testuser",
                    ["CassandraPassword"] = "testpass",
                    ["CassandraRetention"] = "180.00:00:00", // 180 days
                }
            )
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddProsodyClient(configuration);

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
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                    ["MaxConcurrency"] = "128",
                    ["MaxUncommitted"] = "256",
                    ["MaxEnqueuedPerKey"] = "16",
                    ["IdempotenceCacheSize"] = "8192",
                    ["MaxRetries"] = "5",
                    ["ProbePort"] = "8080",
                    ["DeferCacheSize"] = "2048",
                    ["SchedulerCacheSize"] = "16384",
                }
            )
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddProsodyClient(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddProsodyClientSupportsDoubleConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["GroupId"] = "test-group",
                    ["Mock"] = "true",
                    ["DeferFailureThreshold"] = "0.85",
                    ["MonopolizationThreshold"] = "0.75",
                    ["SchedulerFailureWeight"] = "0.4",
                    ["SchedulerWaitWeight"] = "150.5",
                }
            )
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddProsodyClient(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<ProsodyClient>();

        Assert.NotNull(client);
    }
}
