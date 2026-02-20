using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prosody.Extensions;
using Prosody.Native;

namespace Prosody.Logging;

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
    // System.Threading.Lock (net9.0+) is a lightweight lock type purpose-built
    // for the lock statement; on net8.0 we fall back to a plain object monitor.
#if NET9_0_OR_GREATER
    private static readonly Lock SyncLock = new();
#else
    private static readonly object SyncLock = new();
#endif

    private static LogSinkBridge? _sink;
    private static ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Configures logging for all Prosody clients. Must only be called once.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use.</param>
    /// <exception cref="InvalidOperationException">Thrown if logging has already been configured.</exception>
    /// <remarks>
    /// Thread-safe. Logs use the <c>Prosody.Native</c> category.
    /// </remarks>
    public static void Configure(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var sink = new LogSinkBridge(loggerFactory);
        lock (SyncLock)
        {
            if (_sink is not null)
            {
                throw new InvalidOperationException("Prosody logging has already been configured.");
            }

            _sink = sink;
            _loggerFactory = loggerFactory;
            ProsodyFfiMethods.ConfigureLogSink(sink);
        }
    }

    /// <summary>
    /// Creates a logger with the specified category name using the configured factory.
    /// Returns <see cref="NullLogger.Instance"/> if logging has not been configured.
    /// </summary>
    internal static ILogger CreateLogger(string categoryName)
    {
        lock (SyncLock)
        {
            return _loggerFactory?.CreateLogger(categoryName) ?? NullLogger.Instance;
        }
    }

    /// <summary>
    /// Clears the current logging configuration. Intended for host shutdown.
    /// </summary>
    /// <remarks>
    /// Acquires <see cref="SyncLock"/> to avoid racing with <see cref="Configure"/> —
    /// primarily relevant in parallel test scenarios where one hosted service may stop while another is starting.
    /// </remarks>
    internal static void Clear()
    {
        lock (SyncLock)
        {
            _sink = null;
            _loggerFactory = null;
            ProsodyFfiMethods.ClearLogSink();
        }
    }
}
