using System.Net;
using Prosody.Configuration;
using Prosody.State;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for ClientOptions configuration class.
/// </summary>
[Collection("Sequential")]
public sealed class ClientOptionsTests
{
    [Fact]
    public void CloneDeepCopiesCollections()
    {
        var servers = new[] { "broker1:9092", "broker2:9092" };
        var topics = new[] { "orders", "payments" };
        var events = new[] { "user.", "account." };
        var nodes = new[] { "cass1:9042", "cass2:9042" };

        var clone = new ClientOptions
        {
            BootstrapServers = servers,
            SubscribedTopics = topics,
            AllowedEvents = events,
            CassandraNodes = nodes,
            GroupId = "test-group",
        }.Clone();

        Assert.Multiple(
            () => Assert.NotSame(servers, clone.BootstrapServers),
            () => Assert.NotSame(topics, clone.SubscribedTopics),
            () => Assert.NotSame(events, clone.AllowedEvents),
            () => Assert.NotSame(nodes, clone.CassandraNodes),
            () => Assert.Equal(servers, clone.BootstrapServers),
            () => Assert.Equal(topics, clone.SubscribedTopics),
            () => Assert.Equal(events, clone.AllowedEvents),
            () => Assert.Equal(nodes, clone.CassandraNodes),
            () => Assert.Equal("test-group", clone.GroupId)
        );
    }

    [Fact]
    public void ClonePreservesNullCollections()
    {
        var original = new ClientOptions { GroupId = "test-group" };

        var clone = original.Clone();

        Assert.Multiple(
            () => Assert.Null(clone.BootstrapServers),
            () => Assert.Null(clone.SubscribedTopics),
            () => Assert.Null(clone.AllowedEvents),
            () => Assert.Null(clone.CassandraNodes),
            () => Assert.Equal("test-group", clone.GroupId)
        );
    }

    public static TheoryData<string> SameTypeOptions() =>
        [
            .. typeof(ClientOptions)
                .GetProperties()
                .Where(property =>
                    property.PropertyType != typeof(TimeSpan?)
                    && typeof(Native.ClientOptions).GetProperty(property.Name)?.PropertyType == property.PropertyType
                )
                .Select(property => property.Name),
        ];

    [Theory]
    [MemberData(nameof(SameTypeOptions))]
    public void ToNativeCopiesEachSameTypeOption(string name)
    {
        var property = typeof(ClientOptions).GetProperty(name)!;
        var nativeProperty = typeof(Native.ClientOptions).GetProperty(name)!;
        object sample = property.PropertyType switch
        {
            var type when type == typeof(string) => "value",
            var type when type == typeof(string[]) => new[] { "a", "b" },
            var type when type == typeof(bool?) => true,
            var type when type == typeof(uint?) => 7u,
            var type when type == typeof(ulong?) => 7ul,
            var type when type == typeof(ushort?) => (ushort)7,
            var type when type == typeof(double?) => 0.5,
            var type => throw new InvalidOperationException($"No sample value for {type}."),
        };
        var options = new ClientOptions();
        property.SetValue(options, sample);

        Assert.Multiple(
            () => Assert.Equal(sample, nativeProperty.GetValue(options.ToNative())),
            () => Assert.Null(nativeProperty.GetValue(new ClientOptions().ToNative()))
        );
    }

    [Fact]
    public void ToNativeConvertsPeerAddressTypes()
    {
        var options = new ClientOptions
        {
            PeerBindAddress = new IPEndPoint(IPAddress.IPv6Loopback, 9099),
            PeerAdvertisedConnect = new Uri("https://peer.example:443"),
        };

        var native = options.ToNative();

        Assert.Multiple(
            () => Assert.Equal("[::1]:9099", native.PeerBindAddress),
            () => Assert.Equal("https://peer.example:443", native.PeerAdvertisedConnect)
        );
    }

    [Fact]
    public void ToNativeConvertsEnumOptions()
    {
        var native = new ClientOptions
        {
            Mode = ClientMode.LowLatency,
            MessageSpans = SpanRelation.Child,
            TimerSpans = SpanRelation.FollowsFrom,
        }.ToNative();
        var unset = new ClientOptions().ToNative();

        Assert.Multiple(
            () => Assert.Equal(Native.ClientMode.LowLatency, native.Mode),
            () => Assert.Equal(Native.SpanRelation.Child, native.MessageSpans),
            () => Assert.Equal(Native.SpanRelation.FollowsFrom, native.TimerSpans),
            () => Assert.Null(unset.Mode),
            () => Assert.Null(unset.MessageSpans),
            () => Assert.Null(unset.TimerSpans)
        );
    }

    [Fact]
    public void ToNativeConvertsPublishedReadCachePolicy()
    {
        var cached = new ClientOptions { StateReadCache = StateReadCache.For(TimeSpan.FromSeconds(2)) }.ToNative();
        var uncached = new ClientOptions { StateReadCache = StateReadCache.Disabled }.ToNative();

        Assert.Multiple(
            () => Assert.Equal(new Native.ReadCache.Ttl(TimeSpan.FromSeconds(2)), cached.StateReadCache),
            () => Assert.Equal(new Native.ReadCache.Disabled(), uncached.StateReadCache)
        );
    }

    public static TheoryData<string> DurationOptions() =>
        [
            .. typeof(ClientOptions)
                .GetProperties()
                .Where(property => property.PropertyType == typeof(TimeSpan?))
                .Select(property => property.Name),
        ];

    [Theory]
    [MemberData(nameof(DurationOptions))]
    public void ToNativeConvertsEachDurationAndRejectsNegative(string name)
    {
        var property = typeof(ClientOptions).GetProperty(name)!;
        var valid = new ClientOptions();
        property.SetValue(valid, TimeSpan.FromSeconds(3));
        var negative = new ClientOptions();
        property.SetValue(negative, TimeSpan.FromTicks(-1));

        var native = valid.ToNative();
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => negative.ToNative());

        Assert.Multiple(
            () =>
                Assert.Equal(TimeSpan.FromSeconds(3), typeof(Native.ClientOptions).GetProperty(name)!.GetValue(native)),
            () => Assert.Equal(name, error.ParamName)
        );
    }

    /// <summary>Invariant: the parser accepts what the native client accepts and rejects the rest.</summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("30s", 30_000)]
    [InlineData("500ms", 500)]
    [InlineData("1m 30s", 90_000)]
    [InlineData("1h30m", 5_400_000)]
    [InlineData(" 2 hours ", 7_200_000)]
    [InlineData("1d", 86_400_000)]
    [InlineData("1.5m", 90_000)]
    [InlineData("1 .5m", 90_000)]
    [InlineData("1. 5 m", 90_000)]
    [InlineData("1 5s", 15_000)]
    [InlineData("2w", 1_209_600_000)]
    [InlineData("250us", 0.25)]
    public void DurationParsesNativeFormat(string text, double milliseconds) =>
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), Duration.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData(" 0")]
    [InlineData("0 ")]
    [InlineData("00")]
    [InlineData("30")]
    [InlineData("s")]
    [InlineData("30x")]
    [InlineData("-5s")]
    [InlineData(".5s")]
    [InlineData("1.5.5s")]
    [InlineData("1.s")]
    [InlineData("1m .5s")]
    public void DurationRejectsUnsupportedText(string text) => Assert.Null(Duration.Parse(text));

    /// <summary>
    /// Invariant: the result equals the native client's duration after truncation to ticks.
    /// A fraction that <c>humantime</c> rejects as inexact is accepted and truncated the same way.
    /// </summary>
    [Theory]
    [InlineData("99ns", 0)]
    [InlineData("150ns", 1)]
    [InlineData("50ns 50ns", 1)]
    [InlineData("0.5ns", 0)]
    [InlineData("0.0001h", 3_600_000)]
    [InlineData("0.36h", 1296 * TimeSpan.TicksPerSecond)]
    [InlineData("922337203685s 477580700ns", long.MaxValue)]
    public void DurationTruncatesTheExactSumToTicks(string text, long ticks) =>
        Assert.Equal(TimeSpan.FromTicks(ticks), Duration.Parse(text));

    [Theory]
    [InlineData("922337203685s 477580701ns")]
    [InlineData("30000y")]
    [InlineData("18446744073709551616s")]
    [InlineData("100000000000000000000000000000ns")]
    public void DurationBeyondTimeSpanThrows(string text) =>
        Assert.Throws<OverflowException>(() => Duration.Parse(text));

    [Theory]
    [InlineData("2m", 120)]
    [InlineData("0", 0)]
    public void ShutdownTimeoutResolvesTheEnvironmentVariableWhenUnset(string text, int seconds)
    {
        var previous = Environment.GetEnvironmentVariable("PROSODY_SHUTDOWN_TIMEOUT");
        Environment.SetEnvironmentVariable("PROSODY_SHUTDOWN_TIMEOUT", text);
        try
        {
            var fromEnvironment = new ClientOptions().ResolveShutdownTimeout();
            var explicitWins = new ClientOptions { ShutdownTimeout = TimeSpan.FromSeconds(1) }.ResolveShutdownTimeout();

            Assert.Multiple(
                () => Assert.Equal(TimeSpan.FromSeconds(seconds), fromEnvironment),
                () => Assert.Equal(TimeSpan.FromSeconds(1), explicitWins)
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROSODY_SHUTDOWN_TIMEOUT", previous);
        }
    }
}
