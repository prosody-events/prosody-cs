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
}
