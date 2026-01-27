namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Test event handler that forwards events to streams for verification.
/// All operations are synchronous and forward to provided streams.
/// </summary>
/// <remarks>
/// Reference: ../prosody-rb/spec/client_spec.rb handler patterns
/// </remarks>
public class TestEventHandler : IEventHandler
{
    private readonly MessageStream? _messageStream;
    private readonly TimerEventStream? _timerStream;
    private readonly EventNotifier? _shutdownNotifier;
    private readonly Func<Exception, bool>? _isPermanentFunc;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestEventHandler"/> class.
    /// </summary>
    /// <param name="messageStream">Optional stream to receive message events.</param>
    /// <param name="timerStream">Optional stream to receive timer events.</param>
    /// <param name="shutdownNotifier">Optional notifier for shutdown events.</param>
    /// <param name="isPermanentFunc">Optional function to determine if an error is permanent.</param>
    public TestEventHandler(
        MessageStream? messageStream = null,
        TimerEventStream? timerStream = null,
        EventNotifier? shutdownNotifier = null,
        Func<Exception, bool>? isPermanentFunc = null)
    {
        _messageStream = messageStream;
        _timerStream = timerStream;
        _shutdownNotifier = shutdownNotifier;
        _isPermanentFunc = isPermanentFunc;
    }

    /// <inheritdoc />
    public Task OnMessageAsync(IEventContext context, Message message, CancellationToken cancellationToken)
    {
        _messageStream?.Push(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnTimerAsync(IEventContext context, Trigger trigger, CancellationToken cancellationToken)
    {
        _timerStream?.Push(trigger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool IsPermanentError(Exception exception)
    {
        if (_isPermanentFunc is not null)
        {
            return _isPermanentFunc(exception);
        }

        // Default: PermanentException is permanent, everything else is transient
        return exception is PermanentException;
    }

    /// <summary>
    /// Signals shutdown to the notifier if configured.
    /// Call this when the handler is being shut down.
    /// </summary>
    internal void SignalShutdown()
    {
        _shutdownNotifier?.Signal();
    }
}
