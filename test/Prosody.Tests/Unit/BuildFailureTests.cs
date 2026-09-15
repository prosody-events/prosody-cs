using System.Runtime.CompilerServices;
using Prosody.Configuration;

namespace Prosody.Tests.Unit;

/// <summary>The client observes build faults even when cancellation completes the caller's wait first.</summary>
public sealed class BuildFailureTests
{
    [Fact]
    public void CancelledWaitObservesBuildFaultBeforeRethrow()
    {
        var marker = Guid.NewGuid().ToString();
        var unobserved = false;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception.GetBaseException().Message == marker)
            {
                unobserved = true;
            }
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            CompleteCancelledWait(marker);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(unobserved, "The client did not observe the build fault.");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CompleteCancelledWait(string marker)
    {
        using var client = new ProsodyClient(new ClientOptions { SourceSystem = "test" });
        var build = new TaskCompletionSource<Native.ProsodyClient>();
        using var cancellation = new CancellationTokenSource();
        var wait = build.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        build.SetException(new InvalidOperationException(marker));

        var result = client.AwaitNativeAsync(build.Task, wait);
        Assert.True(result.IsCompleted);
        var error = Assert.ThrowsAny<OperationCanceledException>(() => result.GetAwaiter().GetResult());
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
}
