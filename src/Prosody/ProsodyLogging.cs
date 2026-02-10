using Microsoft.Extensions.Logging;
using Prosody.Logging;
using Prosody.Native;

namespace Prosody;

/// <summary>
/// Global logging configuration for Prosody. Configure once at startup before creating clients.
/// </summary>
/// <remarks>
/// For DI scenarios, use <see cref="ProsodyServiceCollectionExtensions.AddProsodyLogging"/> instead.
/// </remarks>
/// <example>
/// <code>
/// var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
/// ProsodyLogging.Configure(loggerFactory);
///
/// var client = new ProsodyClient(options); // Uses configured logger
/// </code>
/// </example>
public static class ProsodyLogging
{
    private static readonly object Lock = new();
    private static LogSinkBridge? _sink;

    /// <summary>
    /// Configures logging for all Prosody clients.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use, or <c>null</c> to disable logging.</param>
    /// <remarks>
    /// Thread-safe; each call replaces the previous configuration.
    /// Logs use the <c>Prosody.Native</c> category.
    /// </remarks>
    public static void Configure(ILoggerFactory? loggerFactory)
    {
        lock (Lock)
        {
            if (loggerFactory is null)
            {
                _sink = null;
                ProsodyFfiMethods.ClearLogSink();
                return;
            }

            _sink = new LogSinkBridge(loggerFactory);
            ProsodyFfiMethods.ConfigureLogSink(_sink);
        }
    }
}
