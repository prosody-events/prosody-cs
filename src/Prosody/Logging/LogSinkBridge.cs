using Microsoft.Extensions.Logging;
using Prosody.Native;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NativeLogLevel = Prosody.Native.LogLevel;

namespace Prosody.Logging;

/// <summary>
/// Bridges the native Rust logging callback interface to <see cref="ILogger"/>.
/// </summary>
internal sealed partial class LogSinkBridge : LogSink
{
    private readonly ILogger _logger;

    public LogSinkBridge(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("Prosody.Native");
    }

    /// <inheritdoc />
    public bool IsEnabled(NativeLogLevel level)
    {
        // Cast works because enum values match Microsoft.Extensions.Logging.LogLevel
        return _logger.IsEnabled((MsLogLevel)level);
    }

    /// <inheritdoc />
    public void Log(NativeLogLevel level, string target, string message, string? file, uint? line)
    {
        // Cast works because enum values match Microsoft.Extensions.Logging.LogLevel
        var logLevel = (MsLogLevel)level;

        switch (logLevel)
        {
            case MsLogLevel.Trace:
                LogNativeTrace(_logger, target, message);
                break;
            case MsLogLevel.Debug:
                LogNativeDebug(_logger, target, message);
                break;
            case MsLogLevel.Information:
                LogNativeInformation(_logger, target, message);
                break;
            case MsLogLevel.Warning:
                LogNativeWarning(_logger, target, message);
                break;
            case MsLogLevel.Error:
            case MsLogLevel.Critical:
                LogNativeError(_logger, target, message);
                break;
        }
    }

    [LoggerMessage(Level = MsLogLevel.Trace, Message = "[{Target}] {Message}")]
    private static partial void LogNativeTrace(ILogger logger, string target, string message);

    [LoggerMessage(Level = MsLogLevel.Debug, Message = "[{Target}] {Message}")]
    private static partial void LogNativeDebug(ILogger logger, string target, string message);

    [LoggerMessage(Level = MsLogLevel.Information, Message = "[{Target}] {Message}")]
    private static partial void LogNativeInformation(ILogger logger, string target, string message);

    [LoggerMessage(Level = MsLogLevel.Warning, Message = "[{Target}] {Message}")]
    private static partial void LogNativeWarning(ILogger logger, string target, string message);

    [LoggerMessage(Level = MsLogLevel.Error, Message = "[{Target}] {Message}")]
    private static partial void LogNativeError(ILogger logger, string target, string message);
}
