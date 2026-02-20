using System.Collections;
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
    private static readonly EventId NativeLogEvent = new(99, "ProsodyNative");

    private readonly ILogger _logger;

    internal LogSinkBridge(ILoggerFactory loggerFactory)
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

        var state = new NativeLogState(target, message, file, line, fields);
        _logger.Log(logLevel, NativeLogEvent, state: state, exception: null, formatter: static (s, _) => s.Formatted);
    }

    /// <summary>
    /// Array-backed log state. The formatter reads <see cref="NativeLogState.Formatted"/>
    /// directly (no indexing into the array). The array provides O(1) positional access
    /// for sinks that index or enumerate the state. Boxing of value-type fields happens
    /// once at construction when populating the array.
    /// </summary>
    private readonly struct NativeLogState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _entries;

        internal NativeLogState(string target, string message, string? file, uint? line, LogFields fields)
        {
            Formatted = $"[{target}] {message}";

            int capacity =
                3
                + (file is not null ? 1 : 0)
                + (line is not null ? 1 : 0)
                + fields.Strings.Count
                + fields.I64s.Count
                + fields.U64s.Count
                + fields.F64s.Count
                + fields.Bools.Count;

            var entries = new KeyValuePair<string, object?>[capacity];
            int i = 0;

            entries[i++] = new("Target", target);
            entries[i++] = new("Message", message);

            if (file is not null)
                entries[i++] = new("SourceFile", file);

            if (line is not null)
                entries[i++] = new("SourceLine", line);

            foreach ((string key, string value) in fields.Strings)
                entries[i++] = new(key, value);

            foreach ((string key, long value) in fields.I64s)
                entries[i++] = new(key, value);

            foreach ((string key, ulong value) in fields.U64s)
                entries[i++] = new(key, value);

            foreach ((string key, double value) in fields.F64s)
                entries[i++] = new(key, value);

            foreach ((string key, bool value) in fields.Bools)
                entries[i++] = new(key, value);

            // {OriginalFormat} last, per MEL convention.
            entries[i] = new("{OriginalFormat}", "[{Target}] {Message}");

            _entries = entries;
        }

        internal string Formatted { get; }

        public int Count => _entries.Length;

        public KeyValuePair<string, object?> this[int index] => _entries[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)_entries).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();
    }
}
