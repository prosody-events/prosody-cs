using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Prosody.Configuration;
using Prosody.Extensions;
using Prosody.Logging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

[Collection(LoggingIsolationCollection.Name)]
public sealed class LoggingTests : IDisposable
{
    public LoggingTests()
    {
        // Ensure clean state before each test
        ProsodyLogging.Clear();
    }

    // Clean up after each test
    public void Dispose() => ProsodyLogging.Clear();

    [Fact]
    public async Task ClearDisablesLogging()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);

        // Verify the logging pipeline is working before we test Clear
        await CreateProducerOnlyClientAsync();
        AssertContainsDisabledConsumerLog(collector);

        // Clear logging and reset the collector
        ProsodyLogging.Clear();
        collector.Clear();

        // A second client should produce no logs now that the sink is detached
        await CreateProducerOnlyClientAsync();
        Assert.Empty(collector.GetSnapshot());
    }

    [Fact]
    public void ConfigureThrowsWhenCalledTwice()
    {
        var collector1 = new FakeLogCollector();
        var collector2 = new FakeLogCollector();
        using var factory1 = new FakeLoggerFactory(collector1);
        using var factory2 = new FakeLoggerFactory(collector2);

        ProsodyLogging.Configure(factory1);
        Assert.Throws<InvalidOperationException>(() => ProsodyLogging.Configure(factory2));
    }

    [Fact]
    public async Task ConfigureCanBeCalledAgainAfterClear()
    {
        var collector1 = new FakeLogCollector();
        var collector2 = new FakeLogCollector();
        using var factory1 = new FakeLoggerFactory(collector1);
        using var factory2 = new FakeLoggerFactory(collector2);

        // Act - configure, clear, then reconfigure
        ProsodyLogging.Configure(factory1);
        ProsodyLogging.Clear();
        collector1.Clear(); // Discard any stale logs captured while the sink was active
        ProsodyLogging.Configure(factory2);
        await CreateProducerOnlyClientAsync();

        // Assert - logs should go to collector2
        Assert.Empty(collector1.GetSnapshot());
        AssertContainsDisabledConsumerLog(collector2);
    }

    [Fact]
    public async Task ClearAndConfigureAreAtomicUnderConcurrency()
    {
        // Exercise managed target changes while native logging stays active.
        const int iterations = 50;

        for (int i = 0; i < iterations; i++)
        {
            var collector = new FakeLogCollector();
            using var factory = new FakeLoggerFactory(collector);

            // Configure a baseline so Clear() has something to clear
            ProsodyLogging.Configure(factory);

            using var barrier = new Barrier(2);

            var clearTask = Task.Run(
                () =>
                {
                    barrier.SignalAndWait(TestContext.Current.CancellationToken);
                    ProsodyLogging.Clear();
                },
                TestContext.Current.CancellationToken
            );

            var configureTask = Task.Run(
                () =>
                {
                    barrier.SignalAndWait(TestContext.Current.CancellationToken);
                    try
                    {
                        // May throw InvalidOperationException if Clear() hasn't run yet;
                        // that's fine — we only care that no corruption occurs.
                        ProsodyLogging.Configure(factory);
                    }
                    catch (InvalidOperationException)
                    {
                        // Expected: Configure() raced ahead of Clear()
                    }
                },
                TestContext.Current.CancellationToken
            );

            await Task.WhenAll(clearTask, configureTask);

            // After both tasks complete, verify we can always do a clean
            // Clear() -> Configure() cycle without corruption.
            ProsodyLogging.Clear();
            ProsodyLogging.Configure(factory);
            await CreateProducerOnlyClientAsync();
            AssertContainsDisabledConsumerLog(collector);

            ProsodyLogging.Clear();
        }
    }

    [Fact]
    public void AddProsodyLoggingRegistersHostedService()
    {
        (ServiceProvider provider, FakeLoggerFactory factory) = BuildServiceProvider();
        using FakeLoggerFactory _ = factory;
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        Assert.Contains(hostedServices, s => s.GetType().Name == "ProsodyLoggingHostedService");
    }

    [Fact]
    public async Task HostedServiceConfiguresLoggingOnStart()
    {
        (ServiceProvider provider, FakeLoggerFactory factory) = BuildServiceProvider();
        using FakeLoggerFactory _ = factory;
        IHostedService hostedService = GetLoggingHostedService(provider);

        await hostedService.StartAsync(CancellationToken.None);
        await CreateProducerOnlyClientAsync();

        AssertContainsDisabledConsumerLog(factory.Collector);
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedServiceClearsLoggingOnStop()
    {
        var (provider, factory) = BuildServiceProvider();
        using FakeLoggerFactory _ = factory;
        IHostedService hostedService = GetLoggingHostedService(provider);
        await hostedService.StartAsync(CancellationToken.None);

        await hostedService.StopAsync(CancellationToken.None);
        factory.Collector.Clear();
        await CreateProducerOnlyClientAsync();

        // Assert - logging was cleared, so no new logs captured
        Assert.Empty(factory.Collector.GetSnapshot());
    }

    [Fact]
    public async Task LoggingCapturesNativeMessages()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);
        await CreateProducerOnlyClientAsync();
        AssertContainsDisabledConsumerLog(collector);
    }

    [Fact]
    public async Task LoggingCapturesStructuredFields()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);

        await CreateProducerOnlyClientAsync();

        // Assert - verify structured fields are captured
        FakeLogRecord record = collector
            .GetSnapshot()
            .First(r => r.Message.Contains("consumer is disabled", StringComparison.Ordinal));

        Assert.True(record.GetStructuredStateValue("Target") is not null, "Should have Target field");
        Assert.True(record.GetStructuredStateValue("Message") is not null, "Should have Message field");
    }

    // Creates a producer-only client, which reports that the consumer is disabled.
    private static async Task CreateProducerOnlyClientAsync()
    {
        await using var client = await ProsodyClient.CreateAsync(
            new ClientOptions
            {
                Mock = true,
                SourceSystem = "test",
                BootstrapServers = [TestDefaults.BootstrapServers],
            }
        );
    }

    private static (ServiceProvider Provider, FakeLoggerFactory Factory) BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var factory = new FakeLoggerFactory();
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddProsodyLogging();
        return (services.BuildServiceProvider(), factory);
    }

    private static IHostedService GetLoggingHostedService(ServiceProvider provider)
    {
        return provider.GetServices<IHostedService>().First(s => s.GetType().Name == "ProsodyLoggingHostedService");
    }

    private static void AssertContainsDisabledConsumerLog(FakeLogCollector collector)
    {
        var snapshot = collector.GetSnapshot();
        Assert.NotEmpty(snapshot);
        Assert.Contains(
            snapshot,
            r =>
                r.Level == LogLevel.Information
                && r.Message.Contains("consumer is disabled", StringComparison.OrdinalIgnoreCase)
        );
    }
}
