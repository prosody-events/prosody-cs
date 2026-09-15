using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Prosody.Configuration;
using Prosody.Extensions;
using Prosody.Messaging;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for the lazy native build and for disposal.
/// </summary>
/// <remarks>
/// Invariant under test: one build serves every caller. A caller's cancellation abandons only that
/// caller's wait. A failed build is evicted and retried. Disposal never waits on a pending build.
/// </remarks>
public sealed class DisposalTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClientOptions MockOptions =>
        new()
        {
            Mock = true,
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-group",
            SubscribedTopics = ["test-topic"],
        };

    private static Task<Native.ProsodyClient> MockNativeAsync() =>
        Native.ProsodyClient.ProsodyClientAsync(MockOptions.ToNative());

    /// <summary>A build factory the test controls: counts calls and completes when released.</summary>
    private sealed class Build
    {
        private int _attempts;

        public TaskCompletionSource<Native.ProsodyClient> Gate { get; } = new();

        /// <summary>
        /// Faults the build so that the client's cached task is already faulted on return. The gate
        /// runs continuations inline, and a thread-pool thread has no synchronization context, so
        /// the client's await resumes before this call completes.
        /// </summary>
        public Task FaultAsync(Exception error) => Task.Run(() => Gate.SetException(error));

        public int Attempts => Volatile.Read(ref _attempts);

        public Task<Native.ProsodyClient> Pending()
        {
            Interlocked.Increment(ref _attempts);
            return Gate.Task;
        }

        public async Task<Native.ProsodyClient> Immediate()
        {
            Interlocked.Increment(ref _attempts);
            return await MockNativeAsync();
        }
    }

    private sealed class NoOpHandler : IProsodyHandler<JsonElement>
    {
        public Task OnMessageAsync(
            ProsodyContext prosodyContext,
            Message<JsonElement> message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnExciseAsync(
            ProsodyContext prosodyContext,
            ExciseMessage message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(
            ProsodyContext prosodyContext,
            ProsodyTimer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private static bool IsReleased(Native.ProsodyClient native)
    {
        try
        {
            native.SourceSystem();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>Yields until the native handle is released. The deadline is a hang guard only.</summary>
    private static async Task WaitUntilReleasedAsync(Native.ProsodyClient native)
    {
        var started = Stopwatch.GetTimestamp();
        while (!IsReleased(native))
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < Deadline, "The native client was not released.");
            await Task.Yield();
        }
    }

    [Fact]
    public async Task DisposeAsyncSafeWhenNotSubscribed()
    {
        var client = await ProsodyClient.CreateAsync(MockOptions);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        var client = await ProsodyClient.CreateAsync(MockOptions);
        await client.SubscribeAsync(new NoOpHandler());
        await client.DisposeAsync();

        await client.DisposeAsync();
    }

    [Fact]
    public async Task CancelledWaiterAbandonsOnlyItsOwnWait()
    {
        var build = new Build();
        await using var client = new ProsodyClient(MockOptions, build.Pending);
        using var cts = new CancellationTokenSource();

        var first = client.ConnectAsync(cts.Token);
        await cts.CancelAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(cts.Token, error.CancellationToken);

        var native = await MockNativeAsync();
        build.Gate.SetResult(native);
        await client.ConnectAsync(Ct).WaitAsync(Deadline, Ct);
        Assert.Equal(1, build.Attempts);

        await client.DisposeAsync();
        Assert.True(IsReleased(native));
    }

    [Fact]
    public async Task AlreadyCancelledTokenDoesNotStartTheBuild()
    {
        var build = new Build();
        await using var client = new ProsodyClient(MockOptions, build.Pending);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ConnectAsync(new CancellationToken(canceled: true))
        );
        Assert.Equal(0, build.Attempts);
    }

    [Fact]
    public async Task FailedBuildIsRetried()
    {
        var attempts = 0;
        await using var client = new ProsodyClient(
            MockOptions,
            () =>
                Interlocked.Increment(ref attempts) == 1
                    ? Task.FromException<Native.ProsodyClient>(new InvalidOperationException("unavailable"))
                    : MockNativeAsync()
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(Ct));
        await client.ConnectAsync(Ct);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task BuildThatEndedCanceledIsNotRetained()
    {
        var attempts = 0;
        await using var client = new ProsodyClient(
            MockOptions,
            () =>
                Interlocked.Increment(ref attempts) == 1
                    ? Task.FromCanceled<Native.ProsodyClient>(new CancellationToken(canceled: true))
                    : MockNativeAsync()
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(Ct));
        await client.ConnectAsync(Ct);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task AbandonedBuildThatFaultsIsRetriedNotReplayed()
    {
        var build = new Build();
        var faulted = false;
        await using var client = new ProsodyClient(MockOptions, () => faulted ? build.Immediate() : build.Pending());
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, args) =>
        {
            if (args.Exception.GetBaseException().Message == _marker)
            {
                fired.TrySetResult();
            }
        };
        TaskScheduler.UnobservedTaskException += onUnobserved;
        try
        {
            await AbandonOneWaiterAsync(client);
            faulted = true;
            await build.FaultAsync(new InvalidOperationException(_marker));

            await client.ConnectAsync(Ct).WaitAsync(Deadline, Ct);
            Assert.Equal(2, build.Attempts);

            // The cache evicted the task returned by BuildAsync. The retained gate belongs to the factory, not the cache.
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(fired.Task.IsCompleted, "The evicted build raised UnobservedTaskException.");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= onUnobserved;
        }
    }

    private const string _marker = "abandoned build fault marker";

    /// <summary>Starts one waiter and cancels it. No reference to the waiter survives this call.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task AbandonOneWaiterAsync(ProsodyClient client)
    {
        using var cts = new CancellationTokenSource();
        var abandoned = client.ConnectAsync(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
    }

    [Fact]
    public async Task DisposeOfANeverConnectedClientIsSynchronousAndFinal()
    {
        var build = new Build();
        var client = new ProsodyClient(MockOptions, build.Pending);

        var disposal = client.DisposeAsync();

        Assert.True(disposal.IsCompletedSuccessfully);
        Assert.Equal(0, build.Attempts);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(Ct).WaitAsync(Deadline, Ct));
    }

    [Fact]
    public async Task DisposeDuringAPendingBuildReturnsAtOnceAndReleasesTheResult()
    {
        var build = new Build();
        var client = new ProsodyClient(MockOptions, build.Pending);
        var connect = client.ConnectAsync(Ct);

        var disposal = client.DisposeAsync();
        Assert.True(disposal.IsCompletedSuccessfully);

        var native = await MockNativeAsync();
        build.Gate.SetResult(native);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connect.WaitAsync(Deadline, Ct));
        await WaitUntilReleasedAsync(native);
    }

    [Fact]
    public async Task DisposeDuringAPendingBuildThatFaultsObservesTheFault()
    {
        var build = new Build();
        var client = new ProsodyClient(MockOptions, build.Pending);
        var connect = client.ConnectAsync(Ct);
        var disposal = client.DisposeAsync();
        Assert.True(disposal.IsCompletedSuccessfully);

        build.Gate.SetException(new InvalidOperationException("unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connect);
        Assert.True(build.Gate.Task.Exception is { } error && error.InnerException is InvalidOperationException);
    }

    [Fact]
    public async Task DisposeOfAConnectedClientClosesItAtOnceAndNeverBlocksOnTheNativeShutdown()
    {
        var ready = MockNativeAsync();
        using var block = new ManualResetEventSlim();
        await using var client = new ProsodyClient(
            MockOptions,
            () => ready,
            _ =>
            {
                block.Wait(Deadline);
                return Task.CompletedTask;
            }
        );
        await client.ConnectAsync(Ct);

        ValueTask disposal = default;
        await Task.Run(() => disposal = client.DisposeAsync(), Ct).WaitAsync(Deadline, Ct);

        Assert.False(disposal.IsCompleted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(Ct).WaitAsync(Deadline, Ct));
        block.Set();
        await disposal.AsTask().WaitAsync(Deadline, Ct);
        Assert.True(IsReleased(await ready));
    }

    [Fact]
    public async Task ShutdownDuringAPendingBuildThatFailsRejectsLaterConnects()
    {
        var build = new Build();
        await using var client = new ProsodyClient(MockOptions, build.Pending);
        var connect = client.ConnectAsync(Ct);

        var shutdown = client.ShutdownAsync();
        await build.FaultAsync(new InvalidOperationException("unavailable"));
        await shutdown.WaitAsync(Deadline, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connect.WaitAsync(Deadline, Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(Ct).WaitAsync(Deadline, Ct));
        Assert.Equal(1, build.Attempts);
    }

    [Fact]
    public async Task ShutdownOfAConnectedClientRejectsOperationsAndDisposeStillReleasesIt()
    {
        var ready = MockNativeAsync();
        var shutdowns = 0;
        var client = new ProsodyClient(
            MockOptions,
            () => ready,
            _ =>
            {
                shutdowns++;
                return Task.CompletedTask;
            }
        );
        await client.ConnectAsync(Ct);

        await client.ShutdownAsync().WaitAsync(Deadline, Ct);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.IsStalledAsync(Ct).WaitAsync(Deadline, Ct));
        await client.DisposeAsync().AsTask().WaitAsync(Deadline, Ct);
        Assert.Equal(1, shutdowns);
        Assert.True(IsReleased(await ready));
    }

    [Fact]
    public async Task ShutdownOfANeverConnectedClientRejectsLaterOperations()
    {
        var build = new Build();
        await using var client = new ProsodyClient(MockOptions, build.Pending);

        await client.ShutdownAsync();

        Assert.Equal(0, build.Attempts);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.IsStalledAsync(Ct).WaitAsync(Deadline, Ct));
    }

    [Fact]
    public async Task UnsubscribeNeverStartsOrWaitsOnABuild()
    {
        var build = new Build();
        await using var client = new ProsodyClient(MockOptions, build.Pending);

        await client.UnsubscribeAsync().WaitAsync(Deadline, Ct);
        Assert.Equal(0, build.Attempts);

        var connect = client.ConnectAsync(Ct);
        await client.UnsubscribeAsync().WaitAsync(Deadline, Ct);
        Assert.False(connect.IsCompleted);
        Assert.Equal(1, build.Attempts);
    }

    [Fact]
    public async Task QueryWithACancelledTokenThrowsWhileTheBuildStaysPending()
    {
        var build = new Build();
        await using var client = new ProsodyClient(MockOptions, build.Pending);
        var connect = client.ConnectAsync(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.IsStalledAsync(new CancellationToken(canceled: true)).WaitAsync(Deadline, Ct)
        );

        Assert.False(connect.IsCompleted);
        Assert.Equal(1, build.Attempts);
    }

    [Fact]
    public async Task StartupFailureDisposesAConnectedClientWithinTheShutdownBudget()
    {
        var options = MockOptions;
        options.ShutdownTimeout = TimeSpan.FromMilliseconds(200);
        options.ConnectOnStart = true;
        var ready = MockNativeAsync();
        var stalledShutdown = new TaskCompletionSource();
        await using var client = new ProsodyClient(options, () => ready, _ => stalledShutdown.Task);
        var builder = Host.CreateEmptyApplicationBuilder(null);
        // A factory-created singleton is owned and disposed by the container; a bare instance is not.
        builder.Services.AddSingleton(_ => client);
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddHostedService<ProsodyClientLifecycle>();
        builder.Services.AddSingleton<IHostedService>(new ThrowingService());
        using var host = builder.Build();

        // Host.StartAsync rethrows with no stop callbacks, so container disposal is the only
        // path that releases the connected client. Its native shutdown never settles here.
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(Ct).WaitAsync(Deadline, Ct));

        Assert.True(IsReleased(await ready));
    }

    private sealed class ThrowingService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("startup failed");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task SourceSystemMismatchFailsTheConnect()
    {
        var options = MockOptions;
        options.SourceSystem = "resolved";
        var ready = MockNativeAsync();
        await using var client = new ProsodyClient(options, () => ready);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(Ct));

        Assert.Contains("'test-group'", error.Message, StringComparison.Ordinal);
        Assert.True(IsReleased(await ready));
    }
}
