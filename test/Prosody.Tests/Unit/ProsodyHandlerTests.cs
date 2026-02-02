namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for implementing the IProsodyHandler interface.
/// </summary>
public sealed class ProsodyHandlerTests
{
    /// <summary>
    /// Test implementation of IProsodyHandler that tracks calls.
    /// </summary>
    private sealed class TestEventHandler : IProsodyHandler
    {
        public int MessageCount { get; private set; }
        public int TimerCount { get; private set; }
        public HandlerResultCode MessageResult { get; set; } = HandlerResultCode.Success;
        public HandlerResultCode TimerResult { get; set; } = HandlerResultCode.Success;

        public Task<HandlerResultCode> OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            MessageCount++;
            return Task.FromResult(MessageResult);
        }

        public Task<HandlerResultCode> OnTimerAsync(
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
    public void CanImplementIProsodyHandler()
    {
        IProsodyHandler handler = new TestEventHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public void CanReturnDifferentResultCodes()
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
    private sealed class AsyncEventHandler : IProsodyHandler
    {
        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(10);

        public async Task<HandlerResultCode> OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Delay, cancellationToken);
            return HandlerResultCode.Success;
        }

        public async Task<HandlerResultCode> OnTimerAsync(
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
    public void CanCreateAsyncImplementation()
    {
        IProsodyHandler handler = new AsyncEventHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public void HandlerResultCodeHasExpectedValues()
    {
        Assert.Equal(0, (int)HandlerResultCode.Success);
        Assert.Equal(1, (int)HandlerResultCode.TransientError);
        Assert.Equal(2, (int)HandlerResultCode.PermanentError);
        Assert.Equal(3, (int)HandlerResultCode.Cancelled);
    }
}
