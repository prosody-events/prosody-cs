using Microsoft.Extensions.Logging;

namespace Prosody.Logging;

internal static partial class LogHelper
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Cancel watch faulted")]
    internal static partial void LogCancelWatchFault(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to capture handler exception to Sentry.")]
    internal static partial void LogSentryCaptureFailed(ILogger logger, Exception exception);
}
