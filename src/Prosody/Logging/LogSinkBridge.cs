using Microsoft.Extensions.Logging;
using Prosody.Native;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NativeLogLevel = Prosody.Native.LogLevel;

namespace Prosody.Logging;

/// <summary>
/// Bridges the native Rust logging callback interface to <see cref="ILogger"/>.
/// </summary>
internal sealed class LogSinkBridge : LogSink
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
    public void Log(
        NativeLogLevel level,
        string target,
        string message,
        string? file,
        uint? line,
        LogFields fields
    )
    {
        var logLevel = (MsLogLevel)level;

        // Build state dictionary with all structured fields
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Target", target),
            new("{OriginalFormat}", "[{Target}] {Message}"),
        };

        // Add source location if available
        if (file is not null)
        {
            state.Add(new("SourceFile", file));
        }
        if (line is not null)
        {
            state.Add(new("SourceLine", line.Value));
        }

        // Add all structured fields with their native types
        foreach (var (key, value) in fields.Strings)
        {
            state.Add(new(key, value));
        }
        foreach (var (key, value) in fields.I64s)
        {
            state.Add(new(key, value));
        }
        foreach (var (key, value) in fields.U64s)
        {
            state.Add(new(key, value));
        }
        foreach (var (key, value) in fields.F64s)
        {
            state.Add(new(key, value));
        }
        foreach (var (key, value) in fields.Bools)
        {
            state.Add(new(key, value));
        }

        // Add Message last (some formatters expect it in a specific position)
        state.Add(new("Message", message));

        _logger.Log(
            logLevel,
            eventId: default,
            state: state,
            exception: null,
            formatter: static (s, _) =>
            {
                var target = s.FirstOrDefault(kvp => kvp.Key == "Target").Value;
                var msg = s.FirstOrDefault(kvp => kvp.Key == "Message").Value;
                return $"[{target}] {msg}";
            }
        );
    }
}
