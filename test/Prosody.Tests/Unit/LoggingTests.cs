using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for logging integration. These tests must run sequentially
/// because they contend over a shared global log writer.
/// </summary>
[Collection("Sequential")]
public sealed class LoggingTests : IDisposable
{
    public LoggingTests()
    {
        // Ensure clean state before each test
        ProsodyLogging.Configure(null);
    }

    public void Dispose()
    {
        // Clean up after each test
        ProsodyLogging.Configure(null);
    }

    [Fact]
    public void ConfigureWithNullDisablesLogging()
    {
        // Arrange
        var logger = new TestLogger();
        using var factory = new TestLoggerFactory(logger);
        ProsodyLogging.Configure(factory);

        // Act
        ProsodyLogging.Configure(null);
        CreateProducerOnlyClient();

        // Assert - no logs captured since logging was disabled
        Assert.Empty(logger.LogEntries);
    }

    [Fact]
    public void ConfigureCanBeCalledMultipleTimes()
    {
        // Arrange
        var logger1 = new TestLogger();
        var logger2 = new TestLogger();
        using var factory1 = new TestLoggerFactory(logger1);
        using var factory2 = new TestLoggerFactory(logger2);

        // Act - configure to logger1, then switch to logger2
        ProsodyLogging.Configure(factory1);
        ProsodyLogging.Configure(factory2);
        CreateProducerOnlyClient();

        // Assert - logs should go to logger2 (the current config), not logger1
        Assert.Empty(logger1.LogEntries);
        AssertContainsDisablingConsumerLog(logger2);
    }

    [Fact]
    public void AddProsodyLoggingRegistersHostedService()
    {
        // Arrange & Act
        var (provider, factory) = BuildServiceProvider(new TestLogger());
        using var _ = factory;

        // Assert
        var hostedServices = provider.GetServices<IHostedService>();
        Assert.Contains(hostedServices, s => s.GetType().Name == "ProsodyLoggingHostedService");
    }

    [Fact]
    public async Task HostedServiceConfiguresLoggingOnStart()
    {
        // Arrange
        var logger = new TestLogger();
        var (provider, factory) = BuildServiceProvider(logger);
        using var _ = factory;
        var hostedService = GetLoggingHostedService(provider);

        // Act
        await hostedService.StartAsync(CancellationToken.None);
        CreateProducerOnlyClient();

        // Assert
        AssertContainsDisablingConsumerLog(logger);

        // Cleanup
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedServiceClearsLoggingOnStop()
    {
        // Arrange
        var logger = new TestLogger();
        var (provider, factory) = BuildServiceProvider(logger);
        using var _ = factory;
        var hostedService = GetLoggingHostedService(provider);
        await hostedService.StartAsync(CancellationToken.None);

        // Act
        await hostedService.StopAsync(CancellationToken.None);
        logger.LogEntries.Clear();
        CreateProducerOnlyClient();

        // Assert - logging was cleared, so no new logs captured
        Assert.Empty(logger.LogEntries);
    }

    [Fact]
    public void LoggingCapturesNativeMessages()
    {
        // Arrange
        var logger = new TestLogger();
        using var factory = new TestLoggerFactory(logger);
        ProsodyLogging.Configure(factory);

        // Act
        CreateProducerOnlyClient();

        // Assert
        AssertContainsDisablingConsumerLog(logger);
    }

    [Fact]
    public void LoggingCapturesStructuredFields()
    {
        // Arrange
        var logger = new TestLogger();
        using var factory = new TestLoggerFactory(logger);
        ProsodyLogging.Configure(factory);

        // Act
        CreateProducerOnlyClient();

        // Assert - verify structured fields are captured
        var entry = logger.LogEntries.First(e =>
            e.Message.Contains("disabling consumer", StringComparison.Ordinal)
        );

        Assert.NotNull(entry.Fields);
        Assert.True(entry.Fields.ContainsKey("Target"), "Should have Target field");
        Assert.True(entry.Fields.ContainsKey("Message"), "Should have Message field");
    }

    /// <summary>
    /// Creates a producer-only client (no consumer config).
    /// This triggers an info log: "disabling consumer (safe to ignore if you're only producing)"
    /// </summary>
    private static void CreateProducerOnlyClient()
    {
        using var client = new ProsodyClient(
            new ClientOptions
            {
                Mock = true,
                SourceSystem = "test",
                BootstrapServers = ["localhost:9092"],
            }
        );
    }

    private static (ServiceProvider Provider, TestLoggerFactory Factory) BuildServiceProvider(
        TestLogger logger
    )
    {
        var services = new ServiceCollection();
        var factory = new TestLoggerFactory(logger);
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddProsodyLogging();
        return (services.BuildServiceProvider(), factory);
    }

    private static IHostedService GetLoggingHostedService(ServiceProvider provider)
    {
        return provider
            .GetServices<IHostedService>()
            .First(s => s.GetType().Name == "ProsodyLoggingHostedService");
    }

    private static void AssertContainsDisablingConsumerLog(TestLogger logger)
    {
        Assert.NotEmpty(logger.LogEntries);
        Assert.Contains(
            logger.LogEntries,
            e =>
                e.Level == LogLevel.Information
                && e.Message.Contains("disabling consumer", StringComparison.Ordinal)
        );
    }

    private sealed class TestLogger : ILogger
    {
        public List<LogEntry> LogEntries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            // Capture structured fields if state is enumerable
            Dictionary<string, object?>? fields = null;
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                fields = kvps.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            LogEntries.Add(new LogEntry(logLevel, formatter(state, exception), exception, fields));
        }

        public sealed record LogEntry(
            LogLevel Level,
            string Message,
            Exception? Exception,
            Dictionary<string, object?>? Fields
        );
    }

    private sealed class TestLoggerFactory(TestLogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose() { }
    }
}
