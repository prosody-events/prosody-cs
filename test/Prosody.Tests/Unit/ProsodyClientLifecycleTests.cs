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

    private readonly FakeLogger _logger = new();

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

        public ValueTask DisposeAsync() => new(DisposeCoreAsync());

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
    }

    [Fact]
    public async Task WorkerUnsubscribingDuringAPendingBuildDoesNotHangHostRunAndTheLateBuildIsReleased()
    {
        var pending = new TaskCompletionSource<Native.ProsodyClient>();
        var options = new ClientOptions
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-group",
        };
        var builder = Host.CreateEmptyApplicationBuilder(null);
        // A factory-created singleton is owned and disposed by the container; a bare instance is not.
        builder.Services.AddSingleton(_ => new ProsodyClient(options, connect: () => pending.Task));
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

            // A build that settles after the host stops serves no caller and is released.
            var native = await Native.ProsodyClient.ProsodyClientAsync(options.ToNative());
            pending.SetResult(native);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => connect.WaitAsync(Deadline, Ct));
            await DisposalTests.WaitUntilReleasedAsync(native);
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
        await using var client = new ProsodyClient(options, connect: () => neverSettles.Task);
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
