using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Prosody.Configuration;
using Prosody.Extensions;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Invariant under test: the lifecycle service connects only when asked, and its disposal wait
/// never outlives the host's stop deadline.
/// </summary>
public sealed class ProsodyClientLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly TaskCompletionSource _shutdownFailedLogged = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly FakeLogger _logger;

    public ProsodyClientLifecycleTests() =>
        _logger = new FakeLogger(
            FakeLogCollector.Create(
                new FakeLogCollectorOptions
                {
                    OutputSink = line =>
                    {
                        if (line.Contains("Failed to shut down", StringComparison.Ordinal))
                        {
                            _shutdownFailedLogged.TrySetResult();
                        }
                    },
                }
            )
        );

    /// <summary>Stands in for the client: counts connects and holds disposal until released.</summary>
    private sealed class Fake
    {
        public int Connects { get; private set; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Connects++;
            return Task.CompletedTask;
        }

        /// <summary>The thread that called disposal. The client closes itself on that thread before returning.</summary>
        public int? ClaimedBy { get; private set; }

        public ValueTask DisposeAsync()
        {
            ClaimedBy = Environment.CurrentManagedThreadId;
            return new(DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            await Release.Task;
            Disposed = true;
        }
    }

    private ProsodyClientLifecycle Lifecycle(Fake fake, bool connectOnStart = false) =>
        new(fake.ConnectAsync, fake.DisposeAsync, connectOnStart, _logger);

    [Fact]
    public async Task ConnectOnStartConnectsOnlyWhenOptedIn()
    {
        var eager = new Fake();
        var lazy = new Fake();

        await Lifecycle(eager, connectOnStart: true).StartAsync(Ct);
        await Lifecycle(lazy).StartAsync(Ct);

        Assert.Equal(1, eager.Connects);
        Assert.Equal(0, lazy.Connects);
    }

    [Fact]
    public async Task DisposalInsideTheDeadlineCompletes()
    {
        var fake = new Fake();
        fake.Release.SetResult();

        await Lifecycle(fake).StoppedAsync(Ct).WaitAsync(Deadline, Ct);

        Assert.True(fake.Disposed);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task DisposalThatOutlivesTheDeadlineIsAbandonedAndStillCompletes()
    {
        var fake = new Fake();
        using var deadline = new CancellationTokenSource();

        var stopped = Lifecycle(fake).StoppedAsync(deadline.Token);
        await deadline.CancelAsync();
        await stopped.WaitAsync(Deadline, Ct);

        var warning = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("stop deadline", warning.Message, StringComparison.Ordinal);
        Assert.False(fake.Disposed);

        fake.Release.SetResult();
        await fake.DisposeAsync().AsTask().WaitAsync(Deadline, Ct);
        Assert.True(fake.Disposed);
    }

    [Fact]
    public async Task StopClaimsTheClientOnTheCallingThreadBeforeItReturns()
    {
        var fake = new Fake();

        var stopped = Lifecycle(fake).StoppedAsync(new CancellationToken(canceled: true));
        Assert.Equal(Environment.CurrentManagedThreadId, fake.ClaimedBy);
        await stopped.WaitAsync(Deadline, Ct);

        Assert.False(fake.Disposed);
        Assert.Single(_logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Warning);
        fake.Release.SetResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledWaitIsAbandonedWhenDisposalCompletesBeforeHandling(bool faulted)
    {
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource();
        var wait = disposal.Task.WaitAsync(deadline.Token);
        await deadline.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);

        var failure = new InvalidOperationException("disposal failed");
        if (faulted)
        {
            disposal.SetException(failure);
        }
        else
        {
            disposal.SetResult();
        }

        await Lifecycle(new Fake()).AwaitDisposalAsync(disposal.Task, wait, deadline.Token).WaitAsync(Deadline, Ct);

        var records = _logger.Collector.GetSnapshot();
        var warning = Assert.Single(records, record => record.Level == LogLevel.Warning);
        Assert.Contains("stop deadline", warning.Message, StringComparison.Ordinal);
        if (faulted)
        {
            var error = Assert.Single(records, record => record.Level == LogLevel.Error);
            Assert.Same(failure, error.Exception);
            Assert.Equal(2, records.Count);
        }
        else
        {
            Assert.Single(records);
        }
    }

    [Fact]
    public async Task WorkerUnsubscribingDuringAPendingBuildDoesNotHangHostRun()
    {
        var neverSettles = new TaskCompletionSource<Native.ProsodyClient>();
        var options = new ClientOptions
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-group",
        };
        var builder = Host.CreateEmptyApplicationBuilder(null);
        // A factory-created singleton is owned and disposed by the container; a bare instance is not.
        builder.Services.AddSingleton(_ => new ProsodyClient(options, () => neverSettles.Task));
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddHostedService<ProsodyClientLifecycle>();
        builder.Services.AddHostedService(sp => new UnsubscribingWorker(sp.GetRequiredService<ProsodyClient>()));
        var shutdownTimeout = TimeSpan.FromSeconds(2);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = shutdownTimeout);
        var host = builder.Build();
        var client = host.Services.GetRequiredService<ProsodyClient>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = lifetime.ApplicationStarted.Register(() => started.SetResult());

        try
        {
            var run = host.RunAsync(Ct);
            await started.Task.WaitAsync(Deadline, Ct);
            var connect = client.ConnectAsync(Ct);
            lifetime.StopApplication();
            await run.WaitAsync(shutdownTimeout, Ct);

            Assert.False(connect.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(Ct));
        }
        finally
        {
            await registration.DisposeAsync();
        }
    }

    private sealed class UnsubscribingWorker(ProsodyClient client) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => client.UnsubscribeAsync();
    }

    [Fact]
    public async Task LateDisposalFaultIsLoggedNotThrown()
    {
        var fake = new Fake();
        using var deadline = new CancellationTokenSource();
        var stopped = Lifecycle(fake).StoppedAsync(deadline.Token);
        await deadline.CancelAsync();
        await stopped.WaitAsync(Deadline, Ct);

        fake.Release.SetException(new InvalidOperationException("late"));

        await _shutdownFailedLogged.Task.WaitAsync(Deadline, Ct);
        var error = Assert.Single(_logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task FailedStartupDoesNotWaitOnThePendingBuild()
    {
        var neverSettles = new TaskCompletionSource<Native.ProsodyClient>();
        var options = new ClientOptions
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-group",
            ConnectOnStart = true,
        };
        await using var client = new ProsodyClient(options, () => neverSettles.Task);
        var builder = Host.CreateEmptyApplicationBuilder(null);
        // A factory-created singleton is owned and disposed by the container; a bare instance is not.
        builder.Services.AddSingleton(_ => client);
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddHostedService<ProsodyClientLifecycle>();
        using var host = builder.Build();
        using var startup = new CancellationTokenSource();

        var starting = host.StartAsync(startup.Token);
        await startup.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);

        await ((IAsyncDisposable)host).DisposeAsync().AsTask().WaitAsync(Deadline, Ct);
    }
}
