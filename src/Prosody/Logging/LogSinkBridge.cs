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
    private static readonly EventId NativeLogEvent = new(1, "ProsodyNative");

    // State indices 0 = Target, 1 = Message (written first by PopulateState).
    private static readonly Func<List<KeyValuePair<string, object?>>, Exception?, string> Formatter = static (s, _) =>
        $"[{s[0].Value}] {s[1].Value}";

    private readonly ILogger _logger;

    public LogSinkBridge(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("Prosody.Native");
    }

    // Cast works because enum values match Microsoft.Extensions.Logging.LogLevel
    /// <inheritdoc />
    public bool IsEnabled(NativeLogLevel level) => _logger.IsEnabled((MsLogLevel)level);

    /// <inheritdoc />
    public void Log(NativeLogLevel level, string target, string message, string? file, uint? line, LogFields fields)
    {
        var logLevel = (MsLogLevel)level;

        if (!_logger.IsEnabled(logLevel))
        {
            return;
        }

        List<KeyValuePair<string, object?>> state = PopulateState(target, message, fields);
        _logger.Log(logLevel, NativeLogEvent, state: state, exception: null, formatter: Formatter);
    }

    private static List<KeyValuePair<string, object?>> PopulateState(string target, string message, LogFields fields)
    {
        int capacity =
            3 + fields.Strings.Count + fields.I64s.Count + fields.U64s.Count + fields.F64s.Count + fields.Bools.Count;

        // Target and Message must remain at indices 0 and 1 (used by Formatter).
        var state = new List<KeyValuePair<string, object?>>(capacity)
        {
            new("Target", target),
            new("Message", message),
        };

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

        // {OriginalFormat} last, per MEL convention.
        state.Add(new("{OriginalFormat}", "[{Target}] {Message}"));

        return state;
    }
}
