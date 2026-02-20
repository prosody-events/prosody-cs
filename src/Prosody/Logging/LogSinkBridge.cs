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
    /// Structured log state that holds native log fields without boxing value types
    /// until a consumer enumerates. The formatter reads <see cref="NativeLogState.Formatted"/>
    /// directly, avoiding list indexing on every log call.
    /// </summary>
    private readonly struct NativeLogState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private const string OriginalFormat = "[{Target}] {Message}";

        private readonly string _target;
        private readonly string _message;
        private readonly string? _file;
        private readonly uint? _line;
        private readonly LogFields _fields;

        internal NativeLogState(string target, string message, string? file, uint? line, LogFields fields)
        {
            _target = target;
            _message = message;
            _file = file;
            _line = line;
            _fields = fields;
            Formatted = $"[{target}] {message}";
        }

        internal string Formatted { get; }

        public int Count =>
            3
            + (_file is not null ? 1 : 0)
            + (_line is not null ? 1 : 0)
            + _fields.Strings.Count
            + _fields.I64s.Count
            + _fields.U64s.Count
            + _fields.F64s.Count
            + _fields.Bools.Count;

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                // Fixed entries: Target, Message, optional SourceFile/SourceLine,
                // then typed field dictionaries, then {OriginalFormat} last per MEL convention.
                if (index == 0)
                    return new("Target", _target);
                if (index == 1)
                    return new("Message", _message);

                var pos = 2;

                if (_file is not null)
                {
                    if (index == pos)
                        return new("SourceFile", _file);
                    pos++;
                }

                if (_line is not null)
                {
                    if (index == pos)
                        return new("SourceLine", _line);
                    pos++;
                }

                var offset = index - pos;
                if (offset < _fields.Strings.Count)
                    return BoxedEntry(_fields.Strings, offset);
                offset -= _fields.Strings.Count;

                if (offset < _fields.I64s.Count)
                    return BoxedEntry(_fields.I64s, offset);
                offset -= _fields.I64s.Count;

                if (offset < _fields.U64s.Count)
                    return BoxedEntry(_fields.U64s, offset);
                offset -= _fields.U64s.Count;

                if (offset < _fields.F64s.Count)
                    return BoxedEntry(_fields.F64s, offset);
                offset -= _fields.F64s.Count;

                if (offset < _fields.Bools.Count)
                    return BoxedEntry(_fields.Bools, offset);
                offset -= _fields.Bools.Count;

                if (offset == 0)
                    return new("{OriginalFormat}", OriginalFormat);

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return new("Target", _target);
            yield return new("Message", _message);

            if (_file is not null)
                yield return new("SourceFile", _file);

            if (_line is not null)
                yield return new("SourceLine", _line);

            foreach ((string key, string value) in _fields.Strings)
                yield return new(key, value);

            foreach ((string key, long value) in _fields.I64s)
                yield return new(key, value);

            foreach ((string key, ulong value) in _fields.U64s)
                yield return new(key, value);

            foreach ((string key, double value) in _fields.F64s)
                yield return new(key, value);

            foreach ((string key, bool value) in _fields.Bools)
                yield return new(key, value);

            yield return new("{OriginalFormat}", OriginalFormat);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private static KeyValuePair<string, object?> BoxedEntry<T>(Dictionary<string, T> dict, int offset)
        {
            using var enumerator = dict.GetEnumerator();
            for (var i = 0; i <= offset; i++)
                enumerator.MoveNext();
            var entry = enumerator.Current;
            return new(entry.Key, entry.Value);
        }
    }
}
