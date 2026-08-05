using Prosody.Infrastructure;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests the per-invocation cancellation registry.
/// </summary>
public sealed class CancellationRegistryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static Task WhenCancelled(CancellationToken token)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => cancelled.TrySetResult());
        return cancelled.Task;
    }

    [Fact]
    public async Task CancelReachesTheRegisteredHandler()
    {
        var registry = new CancellationRegistry();
        var source = registry.Register(handlerId: 1);

        registry.Cancel(handlerId: 1);

        await WhenCancelled(source.Token).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        registry.Complete(handlerId: 1);
    }

    [Fact]
    public async Task LateCancelCannotReachALaterHandler()
    {
        var registry = new CancellationRegistry();
        var first = registry.Register(handlerId: 1);
        registry.Complete(handlerId: 1);
        var second = registry.Register(handlerId: 2);

        registry.Cancel(handlerId: 1);
        registry.Cancel(handlerId: 2);

        await WhenCancelled(second.Token).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.False(first.Token.IsCancellationRequested);
        registry.Complete(handlerId: 2);
    }

    [Fact]
    public async Task RegisterReturnsAFreshUncancelledSource()
    {
        var registry = new CancellationRegistry();
        var first = registry.Register(handlerId: 1);
        registry.Cancel(handlerId: 1);
        await WhenCancelled(first.Token).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        registry.Complete(handlerId: 1);

        var second = registry.Register(handlerId: 2);

        Assert.NotSame(first, second);
        Assert.False(second.IsCancellationRequested);
        registry.Complete(handlerId: 2);
    }

    [Fact]
    public void SecondRegistrationForAnActiveHandlerThrows()
    {
        var registry = new CancellationRegistry();
        registry.Register(handlerId: 1);

        Assert.Throws<InvalidOperationException>(() => registry.Register(handlerId: 1));
    }

    [Fact]
    public void CompletedHandlerIdCanRegisterAgain()
    {
        var registry = new CancellationRegistry();
        registry.Register(handlerId: 1);
        registry.Complete(handlerId: 1);

        var source = registry.Register(handlerId: 1);

        Assert.False(source.IsCancellationRequested);
        registry.Complete(handlerId: 1);
    }

    [Fact]
    public void CancelForAnUnknownHandlerIsANoOp()
    {
        var registry = new CancellationRegistry();
        var source = registry.Register(handlerId: 1);

        registry.Cancel(handlerId: 2);

        Assert.False(source.IsCancellationRequested);
        registry.Complete(handlerId: 1);
    }
}
