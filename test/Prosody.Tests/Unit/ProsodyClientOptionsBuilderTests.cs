using Prosody.Native;
using Xunit;

namespace Prosody.Tests.Unit;

/// <summary>
/// Unit tests for ProsodyClientOptionsBuilder fluent API.
/// Verifies builder pattern works correctly with all 46 configuration parameters.
/// </summary>
public class ProsodyClientOptionsBuilderTests
{
    // ==========================================================================
    // Fluent API tests
    // ==========================================================================

    [Fact]
    public void Builder_FluentChain_ReturnsBuilderInstance()
    {
        var builder = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("test-group")
            .WithSubscribedTopics("test-topic");

        Assert.NotNull(builder);
    }

    [Fact]
    public void Builder_Build_CreatesOptionsInstance()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("test-group")
            .WithSubscribedTopics("test-topic")
            .Build();

        Assert.NotNull(options);
        Assert.Equal("localhost:9092", options.BootstrapServers);
        Assert.Equal("test-group", options.GroupId);
        Assert.NotNull(options.SubscribedTopics);
        Assert.Single(options.SubscribedTopics);
        Assert.Equal("test-topic", options.SubscribedTopics[0]);
    }

    // ==========================================================================
    // Core Kafka Configuration tests
    // ==========================================================================

    [Fact]
    public void WithBootstrapServers_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("broker1:9092,broker2:9092")
            .Build();

        Assert.Equal("broker1:9092,broker2:9092", options.BootstrapServers);
        Assert.True(options.IsSet("BootstrapServers"));
    }

    [Fact]
    public void WithBootstrapServers_ThrowsOnNull()
    {
        var builder = ProsodyClientOptions.CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.WithBootstrapServers(null!));
    }

    [Fact]
    public void WithGroupId_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithGroupId("my-consumer-group")
            .Build();

        Assert.Equal("my-consumer-group", options.GroupId);
    }

    [Fact]
    public void WithSubscribedTopics_MultipleTopics_SetsAllValues()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithSubscribedTopics("topic1", "topic2", "topic3")
            .Build();

        Assert.NotNull(options.SubscribedTopics);
        Assert.Equal(3, options.SubscribedTopics.Count);
        Assert.Equal("topic1", options.SubscribedTopics[0]);
        Assert.Equal("topic2", options.SubscribedTopics[1]);
        Assert.Equal("topic3", options.SubscribedTopics[2]);
    }

    [Fact]
    public void WithAllowedEvents_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithAllowedEvents("order.", "customer.")
            .Build();

        Assert.NotNull(options.AllowedEvents);
        Assert.Equal(2, options.AllowedEvents.Count);
    }

    [Fact]
    public void WithSourceSystem_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithSourceSystem("my-service")
            .Build();

        Assert.Equal("my-service", options.SourceSystem);
    }

    [Fact]
    public void WithMock_True_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMock(true)
            .Build();

        Assert.True(options.Mock);
    }

    [Fact]
    public void WithMock_False_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMock(false)
            .Build();

        Assert.False(options.Mock);
    }

    // ==========================================================================
    // Operating Mode tests
    // ==========================================================================

    [Theory]
    [InlineData(ProsodyMode.Pipeline)]
    [InlineData(ProsodyMode.LowLatency)]
    [InlineData(ProsodyMode.BestEffort)]
    public void WithMode_AllModes_SetsValue(ProsodyMode mode)
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMode(mode)
            .Build();

        Assert.Equal(mode, options.Mode);
    }

    // ==========================================================================
    // Concurrency & Limits tests
    // ==========================================================================

    [Fact]
    public void WithMaxConcurrency_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMaxConcurrency(64)
            .Build();

        Assert.Equal(64, options.MaxConcurrency);
    }

    [Fact]
    public void WithMaxUncommitted_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMaxUncommitted(1000)
            .Build();

        Assert.Equal(1000, options.MaxUncommitted);
    }

    [Fact]
    public void WithIdempotenceCacheSize_Zero_DisablesCache()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithIdempotenceCacheSize(0)
            .Build();

        Assert.Equal(0, options.IdempotenceCacheSize);
    }

    // ==========================================================================
    // Timing Configuration tests
    // ==========================================================================

    [Fact]
    public void WithSendTimeout_SetsValue()
    {
        var timeout = TimeSpan.FromSeconds(30);
        var options = ProsodyClientOptions.CreateBuilder()
            .WithSendTimeout(timeout)
            .Build();

        Assert.Equal(timeout, options.SendTimeout);
    }

    [Fact]
    public void WithStallThreshold_SetsValue()
    {
        var threshold = TimeSpan.FromMinutes(5);
        var options = ProsodyClientOptions.CreateBuilder()
            .WithStallThreshold(threshold)
            .Build();

        Assert.Equal(threshold, options.StallThreshold);
    }

    // ==========================================================================
    // Retry Configuration tests
    // ==========================================================================

    [Fact]
    public void WithRetryBase_SetsValue()
    {
        var baseDelay = TimeSpan.FromMilliseconds(100);
        var options = ProsodyClientOptions.CreateBuilder()
            .WithRetryBase(baseDelay)
            .Build();

        Assert.Equal(baseDelay, options.RetryBase);
    }

    [Fact]
    public void WithMaxRetries_Zero_UnlimitedRetries()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMaxRetries(0)
            .Build();

        Assert.Equal(0, options.MaxRetries);
    }

    [Fact]
    public void WithFailureTopic_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithFailureTopic("failures")
            .Build();

        Assert.Equal("failures", options.FailureTopic);
    }

    // ==========================================================================
    // Health Probe tests
    // ==========================================================================

    [Fact]
    public void WithProbePort_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithProbePort(8080)
            .Build();

        Assert.Equal(8080, options.ProbePort);
    }

    [Fact]
    public void WithProbePort_NegativeOne_DisablesProbe()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithProbePort(-1)
            .Build();

        Assert.Equal(-1, options.ProbePort);
    }

    // ==========================================================================
    // Cassandra Configuration tests
    // ==========================================================================

    [Fact]
    public void WithCassandraNodes_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithCassandraNodes("cassandra1:9042,cassandra2:9042")
            .Build();

        Assert.Equal("cassandra1:9042,cassandra2:9042", options.CassandraNodes);
    }

    [Fact]
    public void WithCassandraRetention_SetsValue()
    {
        var retention = TimeSpan.FromDays(7);
        var options = ProsodyClientOptions.CreateBuilder()
            .WithCassandraRetention(retention)
            .Build();

        Assert.Equal(retention, options.CassandraRetention);
    }

    // ==========================================================================
    // Scheduler Configuration tests
    // ==========================================================================

    [Fact]
    public void WithSchedulerFailureWeight_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithSchedulerFailureWeight(0.3)
            .Build();

        Assert.Equal(0.3, options.SchedulerFailureWeight);
    }

    // ==========================================================================
    // Monopolization Configuration tests
    // ==========================================================================

    [Fact]
    public void WithMonopolizationEnabled_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMonopolizationEnabled(true)
            .Build();

        Assert.True(options.MonopolizationEnabled);
    }

    [Fact]
    public void WithMonopolizationThreshold_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMonopolizationThreshold(0.8)
            .Build();

        Assert.Equal(0.8, options.MonopolizationThreshold);
    }

    // ==========================================================================
    // Defer Configuration tests
    // ==========================================================================

    [Fact]
    public void WithDeferEnabled_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithDeferEnabled(true)
            .Build();

        Assert.True(options.DeferEnabled);
    }

    [Fact]
    public void WithDeferDiscardThreshold_SetsValue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithDeferDiscardThreshold(100)
            .Build();

        Assert.Equal(100, options.DeferDiscardThreshold);
    }

    // ==========================================================================
    // IsSet tracking tests
    // ==========================================================================

    [Fact]
    public void IsSet_UnsetProperty_ReturnsFalse()
    {
        var options = ProsodyClientOptions.CreateBuilder().Build();

        Assert.False(options.IsSet("BootstrapServers"));
        Assert.False(options.IsSet("MaxConcurrency"));
    }

    [Fact]
    public void IsSet_SetProperty_ReturnsTrue()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithMaxConcurrency(32)
            .Build();

        Assert.True(options.IsSet("BootstrapServers"));
        Assert.True(options.IsSet("MaxConcurrency"));
        Assert.False(options.IsSet("GroupId"));
    }

    [Fact]
    public void UnsetProperties_ReturnNull()
    {
        var options = ProsodyClientOptions.CreateBuilder().Build();

        Assert.Null(options.BootstrapServers);
        Assert.Null(options.GroupId);
        Assert.Null(options.Mode);
        Assert.Null(options.MaxConcurrency);
        Assert.Null(options.SendTimeout);
        Assert.Null(options.CassandraNodes);
    }

    // ==========================================================================
    // Full configuration test
    // ==========================================================================

    [Fact]
    public void Builder_AllRequiredFieldsSet_BuildsSuccessfully()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("test-group")
            .WithSubscribedTopics("test-topic")
            .WithMode(ProsodyMode.Pipeline)
            .WithMaxConcurrency(32)
            .WithSendTimeout(TimeSpan.FromSeconds(30))
            .WithMock(false)
            .Build();

        Assert.Equal("localhost:9092", options.BootstrapServers);
        Assert.Equal("test-group", options.GroupId);
        Assert.NotNull(options.SubscribedTopics);
        Assert.Single(options.SubscribedTopics);
        Assert.Equal(ProsodyMode.Pipeline, options.Mode);
        Assert.Equal(32, options.MaxConcurrency);
        Assert.Equal(TimeSpan.FromSeconds(30), options.SendTimeout);
        Assert.False(options.Mock);
    }

    // ==========================================================================
    // ToFFI conversion tests
    // ==========================================================================

    [Fact]
    public void ToFFI_NoOptionsSet_AllOptionFieldsAreNone()
    {
        var options = ProsodyClientOptions.CreateBuilder().Build();
        var ffi = options.ToFFI();

        // Required fields get empty strings
        Assert.Equal(string.Empty, ffi.bootstrap_servers);
        Assert.Equal(string.Empty, ffi.group_id);
        Assert.Equal(string.Empty, ffi.subscribed_topics);

        // All Option fields must be None
        Assert.True(ffi.allowed_events.IsNone);
        Assert.True(ffi.source_system.IsNone);
        Assert.True(ffi.mock.IsNone);
        Assert.True(ffi.mode.IsNone);
        Assert.True(ffi.max_concurrency.IsNone);
        Assert.True(ffi.max_uncommitted.IsNone);
        Assert.True(ffi.max_enqueued_per_key.IsNone);
        Assert.True(ffi.idempotence_cache_size.IsNone);
        Assert.True(ffi.send_timeout_ms.IsNone);
        Assert.True(ffi.stall_threshold_ms.IsNone);
        Assert.True(ffi.shutdown_timeout_ms.IsNone);
        Assert.True(ffi.poll_interval_ms.IsNone);
        Assert.True(ffi.commit_interval_ms.IsNone);
        Assert.True(ffi.timeout_ms.IsNone);
        Assert.True(ffi.slab_size_ms.IsNone);
        Assert.True(ffi.retry_base_ms.IsNone);
        Assert.True(ffi.max_retry_delay_ms.IsNone);
        Assert.True(ffi.max_retries.IsNone);
        Assert.True(ffi.failure_topic.IsNone);
        Assert.True(ffi.probe_port.IsNone);
        Assert.True(ffi.cassandra_nodes.IsNone);
        Assert.True(ffi.cassandra_keyspace.IsNone);
        Assert.True(ffi.cassandra_datacenter.IsNone);
        Assert.True(ffi.cassandra_rack.IsNone);
        Assert.True(ffi.cassandra_user.IsNone);
        Assert.True(ffi.cassandra_password.IsNone);
        Assert.True(ffi.cassandra_retention_seconds.IsNone);
        Assert.True(ffi.scheduler_failure_weight.IsNone);
        Assert.True(ffi.scheduler_max_wait_ms.IsNone);
        Assert.True(ffi.scheduler_wait_weight.IsNone);
        Assert.True(ffi.scheduler_cache_size.IsNone);
        Assert.True(ffi.monopolization_enabled.IsNone);
        Assert.True(ffi.monopolization_threshold.IsNone);
        Assert.True(ffi.monopolization_window_ms.IsNone);
        Assert.True(ffi.monopolization_cache_size.IsNone);
        Assert.True(ffi.defer_enabled.IsNone);
        Assert.True(ffi.defer_base_ms.IsNone);
        Assert.True(ffi.defer_max_delay_ms.IsNone);
        Assert.True(ffi.defer_failure_threshold.IsNone);
        Assert.True(ffi.defer_failure_window_ms.IsNone);
        Assert.True(ffi.defer_cache_size.IsNone);
        Assert.True(ffi.defer_seek_timeout_ms.IsNone);
        Assert.True(ffi.defer_discard_threshold.IsNone);
    }

    [Fact]
    public void ToFFI_RequiredFieldsSet_SetsStringValues()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("test-group")
            .WithSubscribedTopics("topic1", "topic2")
            .Build();

        var ffi = options.ToFFI();

        Assert.Equal("localhost:9092", ffi.bootstrap_servers);
        Assert.Equal("test-group", ffi.group_id);
        Assert.Equal("topic1,topic2", ffi.subscribed_topics);
    }

    [Fact]
    public void ToFFI_OptionalFieldsSet_SetsSomeValues()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithBootstrapServers("localhost:9092")
            .WithGroupId("test-group")
            .WithSubscribedTopics("test-topic")
            .WithAllowedEvents("order.", "customer.")
            .WithSourceSystem("my-service")
            .WithMock(true)
            .WithMode(ProsodyMode.LowLatency)
            .WithMaxConcurrency(64)
            .WithSendTimeout(TimeSpan.FromSeconds(30))
            .WithFailureTopic("failures")
            .WithProbePort(-1)
            .WithSchedulerFailureWeight(0.5)
            .WithDeferDiscardThreshold(100)
            .Build();

        var ffi = options.ToFFI();

        // String options
        Assert.True(ffi.allowed_events.IsSome);
        Assert.Equal("order.,customer.", ffi.allowed_events.AsSome());
        Assert.True(ffi.source_system.IsSome);
        Assert.Equal("my-service", ffi.source_system.AsSome());
        Assert.True(ffi.failure_topic.IsSome);
        Assert.Equal("failures", ffi.failure_topic.AsSome());

        // Boolean option
        Assert.True(ffi.mock.IsSome);
        Assert.True(ffi.mock.AsSome());

        // Mode (i32)
        Assert.True(ffi.mode.IsSome);
        Assert.Equal(1, ffi.mode.AsSome()); // LowLatency = 1

        // U32 options
        Assert.True(ffi.max_concurrency.IsSome);
        Assert.Equal(64u, ffi.max_concurrency.AsSome());

        // I32 option
        Assert.True(ffi.probe_port.IsSome);
        Assert.Equal(-1, ffi.probe_port.AsSome());

        // U64 options (TimeSpan -> milliseconds)
        Assert.True(ffi.send_timeout_ms.IsSome);
        Assert.Equal(30000ul, ffi.send_timeout_ms.AsSome());

        // Weight options (double 0.0-1.0 -> u32 * 10000)
        Assert.True(ffi.scheduler_failure_weight.IsSome);
        Assert.Equal(5000u, ffi.scheduler_failure_weight.AsSome());

        // I64 option
        Assert.True(ffi.defer_discard_threshold.IsSome);
        Assert.Equal(100L, ffi.defer_discard_threshold.AsSome());
    }

    [Fact]
    public void ToFFI_BooleanFalse_SetsSomeFalse()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithMock(false)
            .Build();

        var ffi = options.ToFFI();

        Assert.True(ffi.mock.IsSome);
        Assert.False(ffi.mock.AsSome());
    }

    [Fact]
    public void ToFFI_AllModes_ConvertsCorrectly()
    {
        var pipeline = ProsodyClientOptions.CreateBuilder().WithMode(ProsodyMode.Pipeline).Build().ToFFI();
        var lowLatency = ProsodyClientOptions.CreateBuilder().WithMode(ProsodyMode.LowLatency).Build().ToFFI();
        var bestEffort = ProsodyClientOptions.CreateBuilder().WithMode(ProsodyMode.BestEffort).Build().ToFFI();

        Assert.Equal(0, pipeline.mode.AsSome());
        Assert.Equal(1, lowLatency.mode.AsSome());
        Assert.Equal(2, bestEffort.mode.AsSome());
    }

    [Fact]
    public void ToFFI_WeightConversions_AreAccurate()
    {
        var options = ProsodyClientOptions.CreateBuilder()
            .WithSchedulerFailureWeight(0.25)
            .WithSchedulerWaitWeight(0.75)
            .WithMonopolizationThreshold(0.8)
            .WithDeferFailureThreshold(0.1)
            .Build();

        var ffi = options.ToFFI();

        Assert.Equal(2500u, ffi.scheduler_failure_weight.AsSome());
        Assert.Equal(7500u, ffi.scheduler_wait_weight.AsSome());
        Assert.Equal(8000u, ffi.monopolization_threshold.AsSome());
        Assert.Equal(1000u, ffi.defer_failure_threshold.AsSome());
    }
}
