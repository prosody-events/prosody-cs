namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for implementing the IEventHandler interface.
/// </summary>
public sealed class EventHandlerTests
{
    /// <summary>
    /// Test implementation of IEventHandler that tracks calls.
    /// </summary>
    private sealed class TestEventHandler : IEventHandler
    {
        public int MessageCount { get; private set; }
        public int TimerCount { get; private set; }
        public HandlerResultCode MessageResult { get; set; } = HandlerResultCode.Success;
        public HandlerResultCode TimerResult { get; set; } = HandlerResultCode.Success;

        public Task<HandlerResultCode> OnMessage(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            MessageCount++;
            return Task.FromResult(MessageResult);
        }

        public Task<HandlerResultCode> OnTimer(
            Context context,
            Timer timer,
            CancellationToken cancellationToken
        )
        {
            TimerCount++;
            return Task.FromResult(TimerResult);
        }
    }

    [Fact]
    public void IEventHandler_CanBeImplemented()
    {
        IEventHandler handler = new TestEventHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public void IEventHandler_CanReturnDifferentResultCodes()
    {
        var handler = new TestEventHandler
        {
            MessageResult = HandlerResultCode.TransientError,
            TimerResult = HandlerResultCode.PermanentError,
        };

        Assert.Equal(HandlerResultCode.TransientError, handler.MessageResult);
        Assert.Equal(HandlerResultCode.PermanentError, handler.TimerResult);
    }

    /// <summary>
    /// Test handler that uses async/await properly.
    /// </summary>
    private sealed class AsyncEventHandler : IEventHandler
    {
        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(10);

        public async Task<HandlerResultCode> OnMessage(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Delay, cancellationToken);
            return HandlerResultCode.Success;
        }

        public async Task<HandlerResultCode> OnTimer(
            Context context,
            Timer timer,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Delay, cancellationToken);
            return HandlerResultCode.Success;
        }
    }

    [Fact]
    public void IEventHandler_AsyncImplementation_CanBeCreated()
    {
        IEventHandler handler = new AsyncEventHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public void HandlerResultCode_HasExpectedValues()
    {
        Assert.Equal(0, (int)HandlerResultCode.Success);
        Assert.Equal(1, (int)HandlerResultCode.TransientError);
        Assert.Equal(2, (int)HandlerResultCode.PermanentError);
        Assert.Equal(3, (int)HandlerResultCode.Cancelled);
    }
}
