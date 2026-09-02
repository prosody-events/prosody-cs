using Microsoft.Extensions.Logging;

namespace Prosody.Tests.TestHelpers;

/// <summary>Names the <see cref="ILogger"/> member that a <see cref="ThrowingLoggerProvider"/> logger throws from.</summary>
internal enum ThrowFrom
{
    /// <summary>The logger throws from <see cref="ILogger.IsEnabled"/>.</summary>
    IsEnabled = 0,

    /// <summary>The logger enables every level and throws from <see cref="ILogger.Log{TState}"/>.</summary>
    Log = 1,
}

/// <summary>
/// This provider models a faulty logging provider. Register it in a real
/// <see cref="LoggerFactory"/> so the test exercises the provider pipeline that
/// wraps and rethrows provider exceptions.
/// </summary>
internal sealed class ThrowingLoggerProvider(ThrowFrom where) : ILoggerProvider
{
    /// <summary>The number of logger calls that threw.</summary>
    public int ThrownCallCount { get; private set; }

    public ILogger CreateLogger(string categoryName) => new ThrowingLogger(this, where);

    public void Dispose() { }

    private sealed class ThrowingLogger(ThrowingLoggerProvider owner, ThrowFrom where) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => where == ThrowFrom.IsEnabled ? throw Fault() : true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw Fault();

        private InvalidOperationException Fault()
        {
            owner.ThrownCallCount++;
            return new InvalidOperationException("provider fault");
        }
    }
}
