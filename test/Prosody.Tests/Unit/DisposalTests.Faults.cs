using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Prosody.Tests.Unit;

/// <summary>Checks that disposal observes faults after callers stop their waits.</summary>
public sealed partial class DisposalTests
{
    [Fact]
    public Task DisposeDuringAPendingBuildThatFaultsObservesTheFault() =>
        AssertFaultObservedAsync(DisposeAbandonedBuildAsync);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownFaultAfterDisposalTimeoutIsObservedAndLogged(bool synchronous)
    {
        var logger = new FakeLogger();
        var error = await AssertFaultObservedAsync(fault =>
            DisposeBeforeShutdownFaultAsync(fault, logger, synchronous)
        );

        var records = logger.Collector.GetSnapshot();
        Assert.Single(records, record => record.Level == LogLevel.Warning);
        var failure = Assert.Single(records, record => record.Level == LogLevel.Error);
        Assert.Same(error, failure.Exception);
    }

    private static async Task<Exception> AssertFaultObservedAsync(Func<Exception, Task<WeakReference>> exercise)
    {
        var error = new InvalidOperationException(Guid.NewGuid().ToString());
        var unobserved = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (ReferenceEquals(error, args.Exception.GetBaseException()))
            {
                Interlocked.Increment(ref unobserved);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var task = await exercise(error);
            var started = Stopwatch.GetTimestamp();
            do
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Assert.True(Stopwatch.GetElapsedTime(started) < Deadline, "The faulted task was not collected.");
                await Task.Yield();
            } while (task.IsAlive);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.Equal(0, Volatile.Read(ref unobserved));
            return error;
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    /// <summary>Drops all strong task references before the collection check.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> DisposeAbandonedBuildAsync(Exception error)
    {
        var build = new Build();
        var client = new ProsodyClient(MockOptions, build.Pending);
        await AbandonOneWaiterAsync(client);

        // Track the cached build, not the factory task that BuildAsync already observes.
        var field = typeof(ProsodyClient).GetField("_native", BindingFlags.Instance | BindingFlags.NonPublic);
        var cached = Assert.IsAssignableFrom<Task>(field!.GetValue(client));
        var reference = new WeakReference(cached);
        Assert.False(cached.IsCompleted);
        var disposal = client.DisposeAsync();
        Assert.True(disposal.IsCompletedSuccessfully);
        await disposal;
        await build.FaultAsync(error);
        return reference;
    }

    private static void DisposeSynchronously(ProsodyClient client) => client.Dispose();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> DisposeBeforeShutdownFaultAsync(
        Exception error,
        FakeLogger logger,
        bool synchronous
    )
    {
        var options = MockOptions;
        options.ShutdownTimeout = TimeSpan.Zero;
        var native = await MockNativeAsync();
        var shutdown = new TaskCompletionSource();
        var client = new ProsodyClient(options, () => Task.FromResult(native), _ => shutdown.Task, logger);
        await client.ConnectAsync(Ct);

        if (synchronous)
        {
            DisposeSynchronously(client);
            await WaitUntilReleasedAsync(native);
        }
        else
        {
            await client.DisposeAsync().AsTask().WaitAsync(Deadline, Ct);
        }

        Assert.True(IsReleased(native));
        Assert.False(shutdown.Task.IsCompleted);
        var reference = new WeakReference(client.ShutdownAsync());
        await Task.Run(() => shutdown.SetException(error), Ct);
        return reference;
    }
}
