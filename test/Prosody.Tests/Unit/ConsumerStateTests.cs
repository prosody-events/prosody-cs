namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for consumer state transitions and unsubscribe behavior.
/// These tests document the behavior needed for IAsyncDisposable implementation.
/// </summary>
public sealed class ConsumerStateTests
{
    private static ClientOptions MockOptions =>
        new()
        {
            Mock = true,
            GroupId = "test-group",
            SubscribedTopics = ["test-topic"],
        };

    private sealed class NoOpHandler : IProsodyHandler
    {
        public Task OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task OnTimerAsync(
            Context context,
            Timer timer,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    [Fact]
    public async Task NewClientStartsInConfiguredState()
    {
        using var client = new ProsodyClient(MockOptions);

        var state = await client.ConsumerStateAsync();

        Assert.Equal(ConsumerState.Configured, state);
    }

    [Fact]
    public async Task SubscribeTransitionsToRunningState()
    {
        using var client = new ProsodyClient(MockOptions);
        var handler = new NoOpHandler();

        await client.SubscribeAsync(handler);
        var state = await client.ConsumerStateAsync();

        Assert.Equal(ConsumerState.Running, state);

        await client.UnsubscribeAsync();
    }

    [Fact]
    public async Task UnsubscribeTransitionsFromRunningToConfigured()
    {
        using var client = new ProsodyClient(MockOptions);
        var handler = new NoOpHandler();

        await client.SubscribeAsync(handler);
        await client.UnsubscribeAsync();

        var state = await client.ConsumerStateAsync();

        Assert.Equal(ConsumerState.Configured, state);
    }

    [Fact]
    public async Task UnsubscribeThrowsWhenNotSubscribed()
    {
        // Calling Unsubscribe when not subscribed throws an exception.
        // This means DisposeAsync must check ConsumerState before unsubscribing.
        using var client = new ProsodyClient(MockOptions);

        var exception = await Assert.ThrowsAsync<Native.FfiException.Client>(
            () => client.UnsubscribeAsync()
        );

        Assert.Contains("consumer is not subscribed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsubscribeThrowsWhenCalledTwice()
    {
        // Double unsubscribe throws - state returns to Configured after first unsubscribe.
        using var client = new ProsodyClient(MockOptions);
        var handler = new NoOpHandler();

        await client.SubscribeAsync(handler);
        await client.UnsubscribeAsync();

        var exception = await Assert.ThrowsAsync<Native.FfiException.Client>(
            () => client.UnsubscribeAsync()
        );

        Assert.Contains("consumer is not subscribed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsyncUnsubscribesWhenRunning()
    {
        var client = new ProsodyClient(MockOptions);
        var handler = new NoOpHandler();

        await client.SubscribeAsync(handler);

        // DisposeAsync should unsubscribe automatically
        await client.DisposeAsync();

        // Client is now disposed - cannot check state
    }

    [Fact]
    public async Task DisposeAsyncSafeWhenNotSubscribed()
    {
        // DisposeAsync should not throw when consumer was never subscribed
        await using var client = new ProsodyClient(MockOptions);

        var exception = await Record.ExceptionAsync(async () => await client.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        // Calling DisposeAsync multiple times should be safe
        var client = new ProsodyClient(MockOptions);
        var handler = new NoOpHandler();

        await client.SubscribeAsync(handler);
        await client.DisposeAsync();

        var exception = await Record.ExceptionAsync(async () => await client.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task AwaitUsingPatternWorks()
    {
        // Verify the await using pattern works correctly
        var handler = new NoOpHandler();

        await using var client = new ProsodyClient(MockOptions);
        await client.SubscribeAsync(handler);

        // Client will be disposed at end of scope, unsubscribing automatically
    }
}
