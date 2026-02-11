namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for ClientOptions configuration record.
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
            BootstrapServers = ["localhost:9092"],
            GroupId = "my-app",
            SubscribedTopics = ["my-topic"],
        };

        Assert.Multiple(
            () => Assert.Equal(["localhost:9092"], options.BootstrapServers!),
            () => Assert.Equal("my-app", options.GroupId),
            () => Assert.Equal(["my-topic"], options.SubscribedTopics!),
            () => Assert.Null(options.Mode)
        );
    }

    [Fact]
    public void CanSpecifyMultipleBootstrapServers()
    {
        var options = new ClientOptions
        {
            BootstrapServers = ["broker1:9092", "broker2:9092", "broker3:9092"],
        };

        Assert.Multiple(
            () => Assert.Equal(3, options.BootstrapServers!.Value.Length),
            () => Assert.Contains("broker1:9092", options.BootstrapServers!.Value),
            () => Assert.Contains("broker2:9092", options.BootstrapServers!.Value),
            () => Assert.Contains("broker3:9092", options.BootstrapServers!.Value)
        );
    }

    [Fact]
    public void CanSpecifyMultipleTopics()
    {
        var options = new ClientOptions
        {
            SubscribedTopics = ["orders", "payments", "notifications"],
        };

        Assert.Equal(3, options.SubscribedTopics!.Value.Length);
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
    public void RecordEqualityWorksForScalarFields()
    {
        // Note: Records with array fields use reference equality for arrays,
        // so we test equality with scalar fields only
        var options1 = new ClientOptions { GroupId = "test", SourceSystem = "my-app" };
        var options2 = new ClientOptions { GroupId = "test", SourceSystem = "my-app" };

        Assert.Equal(options1, options2);
    }

    [Fact]
    public void RecordInequalityWhenFieldsDiffer()
    {
        var options1 = new ClientOptions { GroupId = "group-1" };
        var options2 = new ClientOptions { GroupId = "group-2" };

        Assert.NotEqual(options1, options2);
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
    public void WithExpressionCreatesModifiedCopy()
    {
        var original = new ClientOptions { GroupId = "original", Mode = ClientMode.Pipeline };

        var modified = original with { Mode = ClientMode.LowLatency, FailureTopic = "dlq" };

        Assert.Multiple(
            // Original unchanged
            () => Assert.Equal("original", original.GroupId),
            () => Assert.Equal(ClientMode.Pipeline, original.Mode),
            () => Assert.Null(original.FailureTopic),
            // Modified has new values
            () => Assert.Equal("original", modified.GroupId),
            () => Assert.Equal(ClientMode.LowLatency, modified.Mode),
            () => Assert.Equal("dlq", modified.FailureTopic)
        );
    }

    [Fact]
    public void ToNativeConvertsAllFields()
    {
        var options = new ClientOptions
        {
            BootstrapServers = ["localhost:9092"],
            GroupId = "test-app",
            Mode = ClientMode.LowLatency,
            StallThreshold = TimeSpan.FromMinutes(5),
        };

        var native = options.ToNative();

        Assert.Multiple(
            () => Assert.Equal(["localhost:9092"], native.BootstrapServers!),
            () => Assert.Equal("test-app", native.GroupId),
            () => Assert.Equal(Native.ClientMode.LowLatency, native.Mode),
            () => Assert.Equal(TimeSpan.FromMinutes(5), native.StallThreshold)
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
            () => Assert.Null(native.StallThreshold)
        );
    }
}
