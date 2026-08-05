using Microsoft.Extensions.Logging;

namespace Prosody.Logging;

internal static partial class LogHelper
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A cancellation token callback faulted during handler cancellation."
    )]
    internal static partial void LogCancellationCallbackFault(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to capture handler exception to Sentry.")]
    internal static partial void LogSentryCaptureFailed(ILogger logger, Exception exception);
}
