using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Prosody.Configuration;
using Prosody.Extensions;
using Prosody.Logging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>Client disposal retains its logger after the logging service stops.</summary>
[Collection(LoggingIsolationCollection.Name)]
public sealed class ShutdownLoggingTests
{
    [Fact]
    public async Task BackgroundDisposalRetainsItsLoggerAfterLoggingStops()
    {
        var logged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var collector = FakeLogCollector.Create(
            new FakeLogCollectorOptions
            {
                OutputSink = line =>
                {
                    if (line.Contains("Failed to shut down", StringComparison.Ordinal))
                    {
                        logged.TrySetResult();
                    }
                },
            }
        );
        using var factory = new FakeLoggerFactory(collector);
        ProsodyLogging.Configure(factory);
        try
        {
            var failure = new InvalidOperationException("shutdown failed");
            await using var client = new ProsodyClient(
                new ClientOptions { Mock = true, SourceSystem = "background-shutdown" },
                connect: null,
                shutdownNative: _ => Task.FromException(failure)
            );
            await client.ConnectAsync(TestContext.Current.CancellationToken);
            ProsodyLogging.Clear();
            collector.Clear();

            await Task.Run(client.Dispose, TestContext.Current.CancellationToken);

            await logged.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var record = Assert.Single(collector.GetSnapshot(), item => item.Id.Id == 5);
            Assert.Same(failure, record.Exception);
        }
        finally
        {
            ProsodyLogging.Clear();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostStopLogsNativeShutdownFailureAfterLoggingStops(bool timeout)
    {
        var options = new ClientOptions
        {
            Mock = true,
            SourceSystem = "shutdown-logging",
            BootstrapServers = [TestDefaults.BootstrapServers],
        };
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        Exception failure = timeout ? new TimeoutException() : new Native.FfiException.Cancelled("shutdown failed");
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddSingleton<ILoggerFactory>(factory);
        builder.Services.AddProsodyLogging();
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton(sp => new ProsodyClient(
            options,
            connect: null,
            shutdownNative: _ => Task.FromException(failure),
            logger: sp.GetRequiredService<ILogger<ProsodyClient>>()
        ));
        builder.Services.AddHostedService<ProsodyClientLifecycle>();
        using var host = builder.Build();
        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.Services.GetRequiredService<ProsodyClient>().ConnectAsync(TestContext.Current.CancellationToken);
            collector.Clear();

            await host.StopAsync(TestContext.Current.CancellationToken);

            var record = Assert.Single(collector.GetSnapshot(), item => item.Id.Id == (timeout ? 7 : 5));
            Assert.Equal(timeout ? LogLevel.Warning : LogLevel.Error, record.Level);
            if (!timeout)
            {
                Assert.Same(failure, record.Exception);
            }
        }
        finally
        {
            ProsodyLogging.Clear();
        }
    }
}
