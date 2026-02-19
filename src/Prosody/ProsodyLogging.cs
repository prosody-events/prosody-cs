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

        if (Interlocked.CompareExchange(ref _sink, sink, null) is not null)
        {
            throw new InvalidOperationException("Prosody logging has already been configured.");
        }

        ProsodyFfiMethods.ConfigureLogSink(sink);
    }

    /// <summary>
    /// Clears the current logging configuration. Intended for host shutdown.
    /// </summary>
    internal static void Clear()
    {
        Interlocked.Exchange(ref _sink, null);
        ProsodyFfiMethods.ClearLogSink();
    }
}
