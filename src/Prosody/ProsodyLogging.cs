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
    // System.Threading.Lock (net9.0+) is a lightweight lock type purpose-built
    // for the lock statement; on net8.0 we fall back to a plain object monitor.
#if NET9_0_OR_GREATER
    private static readonly Lock SyncLock = new();
#else
    private static readonly object SyncLock = new();
#endif
    private static LogSinkBridge? _sink;

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
            ProsodyFfiMethods.ConfigureLogSink(sink);
        }
    }

    /// <summary>
    /// Clears the current logging configuration. Intended for host shutdown.
    /// </summary>
    internal static void Clear()
    {
        // No lock needed: the Rust side uses ArcSwapOption (lock-free atomic store),
        // and this is only called during shutdown or in tests — never concurrently with Configure in practice.
        _sink = null;
        ProsodyFfiMethods.ClearLogSink();
    }
}
