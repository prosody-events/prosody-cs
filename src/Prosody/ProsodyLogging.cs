using Microsoft.Extensions.Logging;
using Prosody.Logging;
using Prosody.Native;

namespace Prosody;

/// <summary>
/// Global logging configuration for the Prosody library.
/// </summary>
/// <remarks>
/// <para>
/// Prosody uses a global logging configuration that applies to all client instances.
/// Configure logging once at application startup before creating any <see cref="ProsodyClient"/> instances.
/// </para>
/// <para>
/// For dependency injection scenarios, use the <see cref="ProsodyServiceCollectionExtensions.AddProsodyLogging"/>
/// extension method instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Configure logging at startup
/// var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
/// ProsodyLogging.Configure(loggerFactory);
///
/// // Create clients - they all use the configured logger
/// var client = new ProsodyClient(options);
/// </code>
/// </example>
public static class ProsodyLogging
{
    private static readonly object Lock = new();
    private static LogSinkBridge? _sink;

    /// <summary>
    /// Configures logging for all Prosody clients.
    /// </summary>
    /// <param name="loggerFactory">
    /// The logger factory to use for creating loggers.
    /// Pass <c>null</c> to disable logging.
    /// </param>
    /// <remarks>
    /// <para>
    /// This method is thread-safe and can be called multiple times.
    /// Each call replaces the previous logger configuration.
    /// </para>
    /// <para>
    /// Log categories used:
    /// <list type="bullet">
    /// <item><description><c>Prosody.Native</c> - All native library logs</description></item>
    /// </list>
    /// </para>
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
