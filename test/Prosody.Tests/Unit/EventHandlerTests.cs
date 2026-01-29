using Prosody.Native;
using Timer = Prosody.Native.Timer;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for implementing the NativeEventHandler interface.
/// </summary>
public sealed class EventHandlerTests
{
    /// <summary>
    /// Test implementation of NativeEventHandler that tracks calls.
    /// </summary>
    private sealed class TestNativeHandler : NativeEventHandler
    {
        public int MessageCount { get; private set; }
        public int TimerCount { get; private set; }
        public bool ShutdownCalled { get; private set; }
        public HandlerResultCode MessageResult { get; set; } = HandlerResultCode.Success;
        public HandlerResultCode TimerResult { get; set; } = HandlerResultCode.Success;

        public Task<HandlerResultCode> OnMessage(
            Context context,
            Message message,
            Dictionary<string, string> carrier
        )
        {
            MessageCount++;
            return Task.FromResult(MessageResult);
        }

        public Task<HandlerResultCode> OnTimer(
            Context context,
            Timer timer,
            Dictionary<string, string> carrier
        )
        {
            TimerCount++;
            return Task.FromResult(TimerResult);
        }

        public void OnShutdown() => ShutdownCalled = true;
    }

    [Fact]
    public void NativeEventHandler_CanBeImplemented()
    {
        NativeEventHandler handler = new TestNativeHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public void NativeEventHandler_OnShutdown_CanBeCalled()
    {
        var handler = new TestNativeHandler();

        handler.OnShutdown();

        Assert.True(handler.ShutdownCalled);
    }

    [Fact]
    public void NativeEventHandler_CanReturnDifferentResultCodes()
    {
        var handler = new TestNativeHandler
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
    private sealed class AsyncNativeHandler : NativeEventHandler
    {
        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(10);

        public async Task<HandlerResultCode> OnMessage(
            Context context,
            Message message,
            Dictionary<string, string> carrier
        )
        {
            await Task.Delay(Delay);
            return HandlerResultCode.Success;
        }

        public async Task<HandlerResultCode> OnTimer(
            Context context,
            Timer timer,
            Dictionary<string, string> carrier
        )
        {
            await Task.Delay(Delay);
            return HandlerResultCode.Success;
        }

        public void OnShutdown() { }
    }

    [Fact]
    public void NativeEventHandler_AsyncImplementation_CanBeCreated()
    {
        NativeEventHandler handler = new AsyncNativeHandler();
        Assert.NotNull(handler);
    }
}
