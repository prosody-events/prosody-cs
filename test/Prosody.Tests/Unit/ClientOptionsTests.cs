using Prosody.Native;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for UniFFI-generated ClientOptions record.
/// </summary>
public sealed class ClientOptionsTests
{
    private static ClientOptions CreateMinimalOptions(
        string bootstrapServers = "localhost:9092",
        string groupId = "test-group",
        string subscribedTopics = "test-topic") =>
        new(
            bootstrapServers: bootstrapServers,
            groupId: groupId,
            subscribedTopics: subscribedTopics,
            allowedEvents: null,
            sourceSystem: null,
            mock: null,
            mode: null,
            maxConcurrency: null,
            maxUncommitted: null,
            maxEnqueuedPerKey: null,
            idempotenceCacheSize: null,
            sendTimeoutMs: null,
            stallThresholdMs: null,
            shutdownTimeoutMs: null,
            pollIntervalMs: null,
            commitIntervalMs: null,
            timeoutMs: null,
            slabSizeMs: null,
            retryBaseMs: null,
            maxRetryDelayMs: null,
            maxRetries: null,
            failureTopic: null,
            probePort: null,
            cassandraNodes: null,
            cassandraKeyspace: null,
            cassandraDatacenter: null,
            cassandraRack: null,
            cassandraUser: null,
            cassandraPassword: null,
            cassandraRetentionSeconds: null,
            schedulerFailureWeight: null,
            schedulerMaxWaitMs: null,
            schedulerWaitWeight: null,
            schedulerCacheSize: null,
            monopolizationEnabled: null,
            monopolizationThreshold: null,
            monopolizationWindowMs: null,
            monopolizationCacheSize: null,
            deferEnabled: null,
            deferBaseMs: null,
            deferMaxDelayMs: null,
            deferFailureThreshold: null,
            deferFailureWindowMs: null,
            deferCacheSize: null,
            deferSeekTimeoutMs: null,
            deferDiscardThreshold: null
        );

    [Fact]
    public void ClientOptions_CanCreateWithRequiredFields()
    {
        var options = CreateMinimalOptions();

        Assert.Equal("localhost:9092", options.bootstrapServers);
        Assert.Equal("test-group", options.groupId);
        Assert.Equal("test-topic", options.subscribedTopics);
    }

    [Fact]
    public void ClientOptions_CanCreateWithMultipleTopics()
    {
        var options = CreateMinimalOptions(
            bootstrapServers: "broker1:9092,broker2:9092",
            subscribedTopics: "topic1,topic2,topic3"
        );

        Assert.Contains("topic1", options.subscribedTopics);
        Assert.Contains("topic2", options.subscribedTopics);
        Assert.Contains("topic3", options.subscribedTopics);
    }

    [Fact]
    public void ClientOptions_CanSetOptionalFields()
    {
        var options = new ClientOptions(
            bootstrapServers: "localhost:9092",
            groupId: "test-group",
            subscribedTopics: "test-topic",
            allowedEvents: "order.,payment.",
            sourceSystem: "my-service",
            mock: true,
            mode: 0, // Pipeline
            maxConcurrency: 16,
            maxUncommitted: 1000,
            maxEnqueuedPerKey: 100,
            idempotenceCacheSize: 10000,
            sendTimeoutMs: 5000,
            stallThresholdMs: 30000,
            shutdownTimeoutMs: 10000,
            pollIntervalMs: 100,
            commitIntervalMs: 1000,
            timeoutMs: 25000,
            slabSizeMs: 60000,
            retryBaseMs: 1000,
            maxRetryDelayMs: 60000,
            maxRetries: 5,
            failureTopic: "dlq-topic",
            probePort: 8080,
            cassandraNodes: null,
            cassandraKeyspace: null,
            cassandraDatacenter: null,
            cassandraRack: null,
            cassandraUser: null,
            cassandraPassword: null,
            cassandraRetentionSeconds: null,
            schedulerFailureWeight: null,
            schedulerMaxWaitMs: null,
            schedulerWaitWeight: null,
            schedulerCacheSize: null,
            monopolizationEnabled: null,
            monopolizationThreshold: null,
            monopolizationWindowMs: null,
            monopolizationCacheSize: null,
            deferEnabled: null,
            deferBaseMs: null,
            deferMaxDelayMs: null,
            deferFailureThreshold: null,
            deferFailureWindowMs: null,
            deferCacheSize: null,
            deferSeekTimeoutMs: null,
            deferDiscardThreshold: null
        );

        Assert.Equal("order.,payment.", options.allowedEvents);
        Assert.Equal("my-service", options.sourceSystem);
        Assert.True(options.mock);
        Assert.Equal(0, options.mode);
        Assert.Equal(16u, options.maxConcurrency);
    }

    [Fact]
    public void ClientOptions_RecordEquality_Works()
    {
        var options1 = CreateMinimalOptions();
        var options2 = CreateMinimalOptions();

        Assert.Equal(options1, options2);
    }

    [Fact]
    public void ClientOptions_RecordInequality_WhenFieldsDiffer()
    {
        var options1 = CreateMinimalOptions(groupId: "group-1");
        var options2 = CreateMinimalOptions(groupId: "group-2");

        Assert.NotEqual(options1, options2);
    }
}
