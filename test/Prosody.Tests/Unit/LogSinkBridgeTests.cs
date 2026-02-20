using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Prosody.Logging;
using Prosody.Native;
using Prosody.Tests.TestHelpers;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NativeLogLevel = Prosody.Native.LogLevel;

namespace Prosody.Tests.Unit;

public sealed class LogSinkBridgeTests
{
    private static LogFields EmptyLogFields() => new(new(), new(), new(), new(), new());

    private static FakeLogRecord SingleRecord(FakeLogCollector collector)
    {
        var snapshot = collector.GetSnapshot();
        Assert.Single(snapshot);
        return snapshot[0];
    }

    [Theory]
    [InlineData(0)] // Trace
    [InlineData(1)] // Debug
    [InlineData(2)] // Information
    [InlineData(3)] // Warning
    [InlineData(4)] // Error
    [InlineData(5)] // Critical
    public void IsEnabledReturnsTrueForAllLevels(int levelValue)
    {
        var level = (NativeLogLevel)levelValue;
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        Assert.True(bridge.IsEnabled(level));
    }

    [Theory]
    [InlineData(0)] // Trace
    [InlineData(1)] // Debug
    [InlineData(2)] // Information
    public void IsEnabledReturnsFalseWhenLevelDisabled(int levelValue)
    {
        var level = (NativeLogLevel)levelValue;
        using var factory = new FilteringLoggerFactory(MsLogLevel.Warning);
        var bridge = new LogSinkBridge(factory);

        Assert.False(bridge.IsEnabled(level));
    }

    [Fact]
    public void LogDoesNotEmitWhenLevelDisabled()
    {
        var collector = new FakeLogCollector();
        using var factory = new FilteringLoggerFactory(MsLogLevel.Error, collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Debug, "my.target", "hello", null, null, EmptyLogFields());

        Assert.Empty(collector.GetSnapshot());
    }

    [Fact]
    public void LogEmitsCorrectEventId()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "my.target", "hello", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Equal(99, record.Id.Id);
        Assert.Equal("ProsodyNative", record.Id.Name);
    }

    [Theory]
    [InlineData(0, MsLogLevel.Trace)]
    [InlineData(1, MsLogLevel.Debug)]
    [InlineData(2, MsLogLevel.Information)]
    [InlineData(3, MsLogLevel.Warning)]
    [InlineData(4, MsLogLevel.Error)]
    [InlineData(5, MsLogLevel.Critical)]
    public void LogForwardsCorrectLogLevel(int nativeLevelValue, MsLogLevel expectedLevel)
    {
        var nativeLevel = (NativeLogLevel)nativeLevelValue;
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(nativeLevel, "t", "m", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Equal(expectedLevel, record.Level);
    }

    [Fact]
    public void LogEmitsFormattedMessage()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "my.target", "hello world", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Equal("[my.target] hello world", record.Message);
    }

    [Fact]
    public void LoggerUsesNativeCategory()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "t", "m", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Equal("Prosody.Native", record.Category);
    }

    [Fact]
    public void LogIncludesTargetAndMessageFields()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "my.target", "hello world", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Equal("my.target", record.GetStructuredStateValue("Target"));
        Assert.Equal("hello world", record.GetStructuredStateValue("Message"));
    }

    [Fact]
    public void LogIncludesOriginalFormatField()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "t", "m", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Equal("[{Target}] {Message}", record.GetStructuredStateValue("{OriginalFormat}"));
    }

    [Fact]
    public void LogIncludesSourceLocationWhenProvided()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "t", "m", "src/lib.rs", 42u, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Equal("src/lib.rs", record.GetStructuredStateValue("SourceFile"));
        Assert.Equal("42", record.GetStructuredStateValue("SourceLine"));
    }

    [Fact]
    public void LogOmitsSourceLocationWhenNull()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "t", "m", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.Null(record.GetStructuredStateValue("SourceFile"));
        Assert.Null(record.GetStructuredStateValue("SourceLine"));
    }

    [Fact]
    public void LogIncludesAllFieldTypes()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        var fields = new LogFields(
            Strings: new() { { "str_key", "str_val" } },
            I64s: new() { { "i64_key", -42L } },
            U64s: new() { { "u64_key", 99UL } },
            F64s: new() { { "f64_key", 3.14 } },
            Bools: new() { { "bool_key", true } }
        );

        bridge.Log(NativeLogLevel.Information, "t", "m", null, null, fields);

        var record = SingleRecord(collector);
        Assert.Equal("str_val", record.GetStructuredStateValue("str_key"));
        Assert.Equal("-42", record.GetStructuredStateValue("i64_key"));
        Assert.Equal("99", record.GetStructuredStateValue("u64_key"));
        Assert.Equal("3.14", record.GetStructuredStateValue("f64_key"));
        Assert.Equal("True", record.GetStructuredStateValue("bool_key"));
    }

    [Fact]
    public void LogWithEmptyFieldsContainsOnlyCoreEntries()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "t", "m", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        Assert.NotNull(record.StructuredState);
        Assert.Equal(3, record.StructuredState!.Count);
    }

    [Fact]
    public void FieldOrderIsTargetMessageSourceCustomOriginalFormat()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        var fields = new LogFields(
            Strings: new() { { "custom_str", "v" } },
            I64s: new(),
            U64s: new(),
            F64s: new(),
            Bools: new()
        );

        bridge.Log(NativeLogLevel.Information, "t", "m", "src/lib.rs", 42u, fields);

        var record = SingleRecord(collector);
        var state = record.StructuredState;
        Assert.NotNull(state);

        Assert.Equal("Target", state![0].Key);
        Assert.Equal("Message", state[1].Key);
        Assert.Equal("SourceFile", state[2].Key);
        Assert.Equal("SourceLine", state[3].Key);
        Assert.Equal("custom_str", state[4].Key);
        Assert.Equal("{OriginalFormat}", state[^1].Key);
    }

    [Fact]
    public void OriginalFormatIsAlwaysLastEntry()
    {
        var collector = new FakeLogCollector();
        using var factory = new FakeLoggerFactory(collector);
        var bridge = new LogSinkBridge(factory);

        bridge.Log(NativeLogLevel.Information, "t", "m", null, null, EmptyLogFields());

        var record = SingleRecord(collector);
        var state = record.StructuredState;
        Assert.NotNull(state);
        Assert.Equal("{OriginalFormat}", state![^1].Key);
    }

    /// <summary>
    /// An <see cref="ILoggerFactory"/> that creates <see cref="FakeLogger"/> instances
    /// with levels below the specified minimum disabled via <see cref="FakeLogger.ControlLevel"/>.
    /// </summary>
    private sealed class FilteringLoggerFactory(MsLogLevel minLevel, FakeLogCollector? collector = null)
        : ILoggerFactory
    {
        private readonly FakeLogCollector _collector = collector ?? new FakeLogCollector();

        public ILogger CreateLogger(string categoryName)
        {
            var logger = new FakeLogger(_collector, categoryName);
            foreach (MsLogLevel level in Enum.GetValues<MsLogLevel>())
            {
                if (level < minLevel)
                {
                    logger.ControlLevel(level, false);
                }
            }

            return logger;
        }

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }
}
