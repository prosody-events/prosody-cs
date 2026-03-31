using Prosody.Configuration;
using Prosody.Tests.TestHelpers;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for ClientOptions configuration class.
/// </summary>
public sealed class ClientOptionsTests
{
    [Fact]
    public void DefaultConstructorCreatesEmptyOptions()
    {
        var options = new ClientOptions();

        Assert.Multiple(
            () => Assert.Null(options.BootstrapServers),
            () => Assert.Null(options.GroupId),
            () => Assert.Null(options.SubscribedTopics),
            () => Assert.Null(options.Mode),
            () => Assert.Null(options.StallThreshold)
        );
    }

    [Fact]
    public void CanSpecifyOnlyNeededFields()
    {
        var options = new ClientOptions
        {
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "my-app",
            SubscribedTopics = ["my-topic"],
        };

        Assert.Multiple(
            () => Assert.Equal([TestDefaults.BootstrapServers], options.BootstrapServers),
            () => Assert.Equal("my-app", options.GroupId),
            () => Assert.Equal(["my-topic"], options.SubscribedTopics),
            () => Assert.Null(options.Mode)
        );
    }

    [Fact]
    public void CanSpecifyMultipleBootstrapServers()
    {
        var options = new ClientOptions { BootstrapServers = ["broker1:9092", "broker2:9092", "broker3:9092"] };

        Assert.Multiple(
            () => Assert.Equal(3, options.BootstrapServers!.Length),
            () => Assert.Contains("broker1:9092", options.BootstrapServers),
            () => Assert.Contains("broker2:9092", options.BootstrapServers),
            () => Assert.Contains("broker3:9092", options.BootstrapServers)
        );
    }

    [Fact]
    public void CanSpecifyMultipleTopics()
    {
        var options = new ClientOptions { SubscribedTopics = ["orders", "payments", "notifications"] };

        Assert.Equal(3, options.SubscribedTopics!.Length);
    }

    [Fact]
    public void DurationFieldsAcceptTimeSpan()
    {
        var options = new ClientOptions
        {
            StallThreshold = TimeSpan.FromMinutes(5),
            ShutdownTimeout = TimeSpan.FromSeconds(30),
            PollInterval = TimeSpan.FromMilliseconds(100),
            CommitInterval = TimeSpan.FromSeconds(1),
        };

        Assert.Multiple(
            () => Assert.Equal(TimeSpan.FromMinutes(5), options.StallThreshold),
            () => Assert.Equal(TimeSpan.FromSeconds(30), options.ShutdownTimeout),
            () => Assert.Equal(TimeSpan.FromMilliseconds(100), options.PollInterval),
            () => Assert.Equal(TimeSpan.FromSeconds(1), options.CommitInterval)
        );
    }

    [Fact]
    public void ModeAcceptsEnumValue()
    {
        var pipelineOptions = new ClientOptions { Mode = ClientMode.Pipeline };
        var lowLatencyOptions = new ClientOptions { Mode = ClientMode.LowLatency };
        var bestEffortOptions = new ClientOptions { Mode = ClientMode.BestEffort };

        Assert.Multiple(
            () => Assert.Equal(ClientMode.Pipeline, pipelineOptions.Mode),
            () => Assert.Equal(ClientMode.LowLatency, lowLatencyOptions.Mode),
            () => Assert.Equal(ClientMode.BestEffort, bestEffortOptions.Mode)
        );
    }

    [Fact]
    public void ThresholdFieldsAcceptDouble()
    {
        var options = new ClientOptions
        {
            DeferFailureThreshold = 0.9,
            MonopolizationThreshold = 0.75,
            SchedulerFailureWeight = 0.3,
        };

        Assert.Multiple(
            () => Assert.Equal(0.9, options.DeferFailureThreshold),
            () => Assert.Equal(0.75, options.MonopolizationThreshold),
            () => Assert.Equal(0.3, options.SchedulerFailureWeight)
        );
    }

    [Fact]
    public void ProbePortAcceptsUshort()
    {
        var enabledOptions = new ClientOptions { ProbePort = 8080 };
        var disabledOptions = new ClientOptions { ProbePort = 0 };

        Assert.Multiple(
            () => Assert.Equal((ushort)8080, enabledOptions.ProbePort),
            () => Assert.Equal((ushort)0, disabledOptions.ProbePort)
        );
    }

    [Fact]
    public void LowLatencyModeWithFailureTopic()
    {
        var options = new ClientOptions
        {
            Mode = ClientMode.LowLatency,
            FailureTopic = "dead-letters",
            MaxRetries = 3,
        };

        Assert.Multiple(
            () => Assert.Equal(ClientMode.LowLatency, options.Mode),
            () => Assert.Equal("dead-letters", options.FailureTopic),
            () => Assert.Equal(3u, options.MaxRetries)
        );
    }

    [Fact]
    public void CassandraConfiguration()
    {
        var options = new ClientOptions
        {
            CassandraNodes = ["cass1:9042", "cass2:9042"],
            CassandraKeyspace = "prosody",
            CassandraDatacenter = "dc1",
            CassandraRetention = TimeSpan.FromDays(365),
        };

        Assert.Multiple(
            () => Assert.Equal(2, options.CassandraNodes?.Length),
            () => Assert.Equal("prosody", options.CassandraKeyspace),
            () => Assert.Equal("dc1", options.CassandraDatacenter),
            () => Assert.Equal(TimeSpan.FromDays(365), options.CassandraRetention)
        );
    }

    [Fact]
    public void CloneDeepCopiesCollections()
    {
        var servers = new[] { "broker1:9092", "broker2:9092" };
        var topics = new[] { "orders", "payments" };
        var events = new[] { "user.", "account." };
        var nodes = new[] { "cass1:9042", "cass2:9042" };

        var original = new ClientOptions
        {
            BootstrapServers = servers,
            SubscribedTopics = topics,
            AllowedEvents = events,
            CassandraNodes = nodes,
            GroupId = "test-group",
        };

        var clone = original.Clone();

        // Mutate the original arrays
        servers[0] = "mutated:9092";
        topics[0] = "mutated";
        events[0] = "mutated.";
        nodes[0] = "mutated:9042";

        Assert.Multiple(
            () => Assert.Equal("broker1:9092", clone.BootstrapServers![0]),
            () => Assert.Equal("orders", clone.SubscribedTopics![0]),
            () => Assert.Equal("user.", clone.AllowedEvents![0]),
            () => Assert.Equal("cass1:9042", clone.CassandraNodes![0]),
            () => Assert.Equal("test-group", clone.GroupId)
        );
    }

    [Fact]
    public void CloneDeepCopiesArrays()
    {
        var servers = new[] { "broker1:9092", "broker2:9092" };
        var topics = new[] { "orders", "payments" };

        var original = new ClientOptions { BootstrapServers = servers, SubscribedTopics = topics };

        var clone = original.Clone();

        Assert.Multiple(
            () => Assert.NotSame(servers, clone.BootstrapServers),
            () => Assert.NotSame(topics, clone.SubscribedTopics),
            () => Assert.Equal(servers, clone.BootstrapServers),
            () => Assert.Equal(topics, clone.SubscribedTopics)
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

    [Fact]
    public void ToNativeConvertsAllFields()
    {
        var options = new ClientOptions
        {
            BootstrapServers = [TestDefaults.BootstrapServers],
            GroupId = "test-app",
            Mode = ClientMode.LowLatency,
            StallThreshold = TimeSpan.FromMinutes(5),
            TelemetryTopic = "my-telemetry-topic",
            TelemetryEnabled = false,
        };

        var native = options.ToNative();

        Assert.Multiple(
            () => Assert.Equal([TestDefaults.BootstrapServers], native.BootstrapServers!),
            () => Assert.Equal("test-app", native.GroupId),
            () => Assert.Equal(Native.ClientMode.LowLatency, native.Mode),
            () => Assert.Equal(TimeSpan.FromMinutes(5), native.StallThreshold),
            () => Assert.Equal("my-telemetry-topic", native.TelemetryTopic),
            () => Assert.Equal(false, native.TelemetryEnabled)
        );
    }

    [Fact]
    public void ToNativePreservesNullValues()
    {
        var options = new ClientOptions { GroupId = "only-this" };

        var native = options.ToNative();

        Assert.Multiple(
            () => Assert.Null(native.BootstrapServers),
            () => Assert.Equal("only-this", native.GroupId),
            () => Assert.Null(native.Mode),
            () => Assert.Null(native.StallThreshold),
            () => Assert.Null(native.TelemetryTopic),
            () => Assert.Null(native.TelemetryEnabled)
        );
    }

    [Fact]
    public void ToNativeConvertsSpanRelation()
    {
        var options = new ClientOptions
        {
            MessageSpans = SpanRelation.Child,
            TimerSpans = SpanRelation.FollowsFrom,
        };

        var native = options.ToNative();

        Assert.Multiple(
            () => Assert.Equal(Native.SpanRelation.Child, native.MessageSpans),
            () => Assert.Equal(Native.SpanRelation.FollowsFrom, native.TimerSpans)
        );
    }

    [Fact]
    public void ToNativePreservesNullSpanRelation()
    {
        var options = new ClientOptions { GroupId = "test" };

        var native = options.ToNative();

        Assert.Multiple(
            () => Assert.Null(native.MessageSpans),
            () => Assert.Null(native.TimerSpans)
        );
    }
}
