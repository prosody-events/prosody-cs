using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Prosody.Configuration;
using Prosody.Extensions;
using Prosody.Logging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

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
    public void ClearDisablesLogging()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);

        // Verify the logging pipeline is working before we test Clear
        CreateProducerOnlyClient();
        AssertContainsDisablingConsumerLog(collector);

        // Clear logging and reset the collector
        ProsodyLogging.Clear();
        collector.Clear();

        // A second client should produce no logs now that the sink is detached
        CreateProducerOnlyClient();
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
    public void ConfigureCanBeCalledAgainAfterClear()
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
        CreateProducerOnlyClient();

        // Assert - logs should go to collector2
        Assert.Empty(collector1.GetSnapshot());
        AssertContainsDisablingConsumerLog(collector2);
    }

    [Fact]
    public async Task ClearAndConfigureAreAtomicUnderConcurrency()
    {
        // Exercises the race window between Clear() and Configure().
        // Before the fix, ClearLogSink() ran outside the lock, so a concurrent
        // Configure() could succeed and then have its sink immediately cleared.
        // With the fix, both operations are fully serialized under SyncLock.
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
            CreateProducerOnlyClient();
            AssertContainsDisablingConsumerLog(collector);

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
        CreateProducerOnlyClient();

        AssertContainsDisablingConsumerLog(factory.Collector);
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
        CreateProducerOnlyClient();

        // Assert - logging was cleared, so no new logs captured
        Assert.Empty(factory.Collector.GetSnapshot());
    }

    [Fact]
    public void LoggingCapturesNativeMessages()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);
        CreateProducerOnlyClient();
        AssertContainsDisablingConsumerLog(collector);
    }

    [Fact]
    public void LoggingCapturesStructuredFields()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);

        CreateProducerOnlyClient();

        // Assert - verify structured fields are captured
        FakeLogRecord record = collector
            .GetSnapshot()
            .First(r => r.Message.Contains("disabling consumer", StringComparison.Ordinal));

        Assert.True(record.GetStructuredStateValue("Target") is not null, "Should have Target field");
        Assert.True(record.GetStructuredStateValue("Message") is not null, "Should have Message field");
    }

    // Creates a producer-only client, which triggers a "disabling consumer" info log.
    private static void CreateProducerOnlyClient()
    {
        using var client = new ProsodyClient(
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

    private static void AssertContainsDisablingConsumerLog(FakeLogCollector collector)
    {
        var snapshot = collector.GetSnapshot();
        Assert.NotEmpty(snapshot);
        Assert.Contains(
            snapshot,
            r =>
                r.Level == LogLevel.Information
                && r.Message.Contains("disabling consumer", StringComparison.OrdinalIgnoreCase)
        );
    }

    private sealed class FakeLoggerFactory(FakeLogCollector collector) : ILoggerFactory
    {
        public FakeLogCollector Collector { get; } = collector;

        public FakeLoggerFactory()
            : this(new FakeLogCollector()) { }

        public ILogger CreateLogger(string categoryName) => new FakeLogger(Collector, categoryName);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }
}
