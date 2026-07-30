using System.ComponentModel;
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
    private static bool _processExitHandlerRegistered;

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
            RegisterProcessExitShutdown();
        }
    }

    /// <summary>
    /// Flushes buffered telemetry (OpenTelemetry spans and metrics) to the exporter
    /// without tearing the export pipeline down. A safe no-op when telemetry was
    /// never initialized.
    /// </summary>
    /// <remarks>
    /// Telemetry is process-global, so this is the right call when a single client
    /// is disposed while the process keeps running — it forces the export that the
    /// batch span processor and periodic metric reader would otherwise defer to
    /// their timers. For a deterministic final export at process exit, prefer
    /// <see cref="ShutdownTelemetry"/>. Blocks until the export completes.
    /// </remarks>
    /// <exception cref="Native.FfiException">Thrown if the span or metric exporter fails to flush.</exception>
    public static void FlushTelemetry() => ProsodyFfiMethods.FlushTelemetry();

    /// <summary>
    /// Flushes and shuts down the process-global telemetry pipeline. A safe no-op
    /// when telemetry was never initialized.
    /// </summary>
    /// <remarks>
    /// Only correct at actual process exit: shutdown tears telemetry down for the
    /// whole process, so calling it per client would disable telemetry for every
    /// sibling client. This runs automatically once via
    /// <see cref="AppDomain.ProcessExit"/> after logging is configured; call it
    /// directly only when managing process teardown yourself. Blocks until the
    /// final export completes.
    /// </remarks>
    /// <exception cref="Native.FfiException">Thrown if the span or metric pipeline fails to shut down.</exception>
    public static void ShutdownTelemetry() => ProsodyFfiMethods.ShutdownTelemetry();

    /// <summary>
    /// Registers a one-shot <see cref="AppDomain.ProcessExit"/> handler that shuts
    /// telemetry down for the whole process, giving a deterministic final export at
    /// real teardown. Idempotent; the caller must hold <see cref="SyncLock"/>.
    /// </summary>
    private static void RegisterProcessExitShutdown()
    {
        if (_processExitHandlerRegistered)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            try
            {
                ShutdownTelemetry();
            }
            catch (Native.FfiException)
            {
                // Best-effort at process exit: a telemetry shutdown failure must not
                // fault a process that is already tearing down.
            }
        };

        _processExitHandlerRegistered = true;
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

    /// <summary>
    /// Resets the logging configuration so that <see cref="Configure"/> can be called again.
    /// Intended for test fixtures and test teardown — not for production use.
    /// </summary>
    /// <remarks>
    /// After calling this method, log events from the native layer are silently discarded
    /// until <see cref="Configure"/> is called again.
    /// Thread-safe: acquires the same lock as <see cref="Configure"/> to prevent races.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ResetForTesting() => Clear();
}
