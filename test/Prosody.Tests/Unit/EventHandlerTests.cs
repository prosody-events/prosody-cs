using Prosody.Native;
using ProsodyEventHandler = Prosody.Native.EventHandler;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for implementing the EventHandler interface.
/// </summary>
public sealed class EventHandlerTests
{
    /// <summary>
    /// Test implementation of EventHandler that tracks calls.
    /// </summary>
    private sealed class TestHandler : ProsodyEventHandler
    {
        public int MessageCount { get; private set; }
        public int TimerCount { get; private set; }
        public bool ShutdownCalled { get; private set; }
        public HandlerResultCode MessageResult { get; set; } = HandlerResultCode.Success;
        public HandlerResultCode TimerResult { get; set; } = HandlerResultCode.Success;

        public Task<HandlerResultCode> OnMessage(MessageEvent @event)
        {
            MessageCount++;
            return Task.FromResult(MessageResult);
        }

        public Task<HandlerResultCode> OnTimer(TimerEvent @event)
        {
            TimerCount++;
            return Task.FromResult(TimerResult);
        }

        public void OnShutdown() => ShutdownCalled = true;
    }

    [Fact]
    public void EventHandler_CanBeImplemented()
    {
        ProsodyEventHandler handler = new TestHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public void EventHandler_OnShutdown_CanBeCalled()
    {
        var handler = new TestHandler();

        handler.OnShutdown();

        Assert.True(handler.ShutdownCalled);
    }

    [Fact]
    public void EventHandler_CanReturnDifferentResultCodes()
    {
        var handler = new TestHandler
        {
            MessageResult = HandlerResultCode.TransientError,
            TimerResult = HandlerResultCode.PermanentError
        };

        Assert.Equal(HandlerResultCode.TransientError, handler.MessageResult);
        Assert.Equal(HandlerResultCode.PermanentError, handler.TimerResult);
    }

    /// <summary>
    /// Test handler that uses async/await properly.
    /// </summary>
    private sealed class AsyncHandler : ProsodyEventHandler
    {
        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(10);

        public async Task<HandlerResultCode> OnMessage(MessageEvent @event)
        {
            await Task.Delay(Delay);
            return HandlerResultCode.Success;
        }

        public async Task<HandlerResultCode> OnTimer(TimerEvent @event)
        {
            await Task.Delay(Delay);
            return HandlerResultCode.Success;
        }

        public void OnShutdown() { }
    }

    [Fact]
    public void EventHandler_AsyncImplementation_CanBeCreated()
    {
        ProsodyEventHandler handler = new AsyncHandler();
        Assert.NotNull(handler);
    }

    /// <summary>
    /// Test handler using cancellation token pattern.
    /// Demonstrates how to convert AwaitCancel() to CancellationToken.
    /// </summary>
    private sealed class CancellationAwareHandler : ProsodyEventHandler
    {
        public async Task<HandlerResultCode> OnMessage(MessageEvent @event)
        {
            using var cts = new CancellationTokenSource();

            // Start task that awaits Rust cancellation signal
            var cancelTask = Task.Run(async () =>
            {
                await @event.AwaitCancel();
                cts.Cancel();
            });

            try
            {
                // Simulate work with cancellation support
                await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                return HandlerResultCode.Success;
            }
            catch (OperationCanceledException)
            {
                return HandlerResultCode.Cancelled;
            }
        }

        public async Task<HandlerResultCode> OnTimer(TimerEvent @event)
        {
            if (@event.ShouldCancel())
            {
                return HandlerResultCode.Cancelled;
            }

            await Task.CompletedTask;
            return HandlerResultCode.Success;
        }

        public void OnShutdown() { }
    }

    [Fact]
    public void EventHandler_CancellationAwareImplementation_CanBeCreated()
    {
        ProsodyEventHandler handler = new CancellationAwareHandler();
        Assert.NotNull(handler);
    }
}
