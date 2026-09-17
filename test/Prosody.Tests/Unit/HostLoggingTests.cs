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

/// <summary>
/// Native logs reach the host logger for the whole client lifetime: during an eager connect and
/// after the logging service stops.
/// </summary>
[Collection(LoggingIsolationCollection.Name)]
public sealed class HostLoggingTests
{
    [Fact]
    public async Task ConnectOnStartLogsThroughTheHostWhateverTheRegistrationOrder()
    {
        var options = new ClientOptions
        {
            Mock = true,
            SourceSystem = "connect-on-start-logging",
            BootstrapServers = [TestDefaults.BootstrapServers],
            ConnectOnStart = true,
        };
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddSingleton<ILoggerFactory>(factory);
        builder.Services.AddSingleton(Options.Create(options));
        // The connect stands in for the native client, which logs through ProsodyLogging.
        builder.Services.AddSingleton(_ => new ProsodyClient(
            options,
            connect: async () =>
            {
                ProsodyLogging
                    .CreateLogger("Prosody.Native")
                    .Log(LogLevel.Information, 0, "connecting", null, static (state, _) => state);
                return await Native.ProsodyClient.ProsodyClientAsync(options.ToNative());
            }
        ));
        // The client is registered before logging on purpose.
        builder.Services.AddHostedService<ProsodyClientLifecycle>();
        builder.Services.AddProsodyLogging();
        using var host = builder.Build();
        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);

            Assert.Single(collector.GetSnapshot(), record => record.Message == "connecting");
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            ProsodyLogging.Clear();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStartupDisposalClearsLogging(bool cancelled)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddProsodyLogging();
        Exception failure = cancelled
            ? new OperationCanceledException()
            : new InvalidOperationException("connect failed");
        builder.Services.AddSingleton<IHostedService>(sp => new ProsodyClientLifecycle(
            _ => Task.FromException(failure),
            () => ValueTask.CompletedTask,
            connectOnStart: true,
            sp.GetRequiredService<ILogger<ProsodyClientLifecycle>>()
        ));
        using var host = builder.Build();
        using var factory = new FakeLoggerFactory();
        try
        {
            var actual = await Record.ExceptionAsync(() => host.RunAsync(TestContext.Current.CancellationToken));
            Assert.Same(failure, actual);

            ProsodyLogging.Configure(factory);
            Assert.IsNotType<Microsoft.Extensions.Logging.Abstractions.NullLogger>(
                ProsodyLogging.CreateLogger("next-host")
            );
        }
        finally
        {
            ProsodyLogging.Clear();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalClearsOnlyLoggingOwnedByTheService(bool started)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddProsodyLogging();
        using var host = builder.Build();
        using var factory = new FakeLoggerFactory();
        try
        {
            if (started)
            {
                await host.StartAsync(TestContext.Current.CancellationToken);
                await host.StopAsync(TestContext.Current.CancellationToken);
                ProsodyLogging.Configure(factory);
            }
            else
            {
                ProsodyLogging.Configure(factory);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    host.StartAsync(TestContext.Current.CancellationToken)
                );
            }

            host.Dispose();

            Assert.Throws<InvalidOperationException>(() => ProsodyLogging.Configure(factory));
        }
        finally
        {
            ProsodyLogging.Clear();
        }
    }

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
