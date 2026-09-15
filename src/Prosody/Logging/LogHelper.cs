using Microsoft.Extensions.Logging;

namespace Prosody.Logging;

internal static partial class LogHelper
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "OnCancel() faulted synchronously")]
    internal static partial void LogOnCancelSyncFault(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "OnCancel() faulted after handler completed")]
    internal static partial void LogOnCancelLateFault(ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "OnCancel() faulted")]
    internal static partial void LogOnCancelFault(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to capture handler exception to Sentry.")]
    internal static partial void LogSentryCaptureFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Failed to shut down the Prosody client during disposal."
    )]
    internal static partial void LogShutdownFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "The host shutdown timeout fired before the Prosody client finished disposal. Disposal continues in the background."
    )]
    internal static partial void LogDisposalAbandoned(ILogger logger);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Warning,
        Message = "Native shutdown did not finish within the shutdown budget of {Budget}. The native client is released anyway."
    )]
    internal static partial void LogNativeShutdownAbandoned(ILogger logger, TimeSpan budget);
}
