namespace Prosody.Configuration;

// This file owns the conversion of the options to the native options.

public sealed partial class ClientOptions
{
    private static Native.SpanRelation? ToNativeSpanRelation(SpanRelation? relation) =>
        relation switch
        {
            SpanRelation.Child => Native.SpanRelation.Child,
            SpanRelation.FollowsFrom => Native.SpanRelation.FollowsFrom,
            null => null,
            _ => throw new InvalidOperationException($"Unknown span relation: {relation}"),
        };

    private Native.ClientMode? ToNativeMode() =>
        Mode switch
        {
            ClientMode.Pipeline => Native.ClientMode.Pipeline,
            ClientMode.LowLatency => Native.ClientMode.LowLatency,
            ClientMode.BestEffort => Native.ClientMode.BestEffort,
            null => null,
            _ => throw new InvalidOperationException($"Unknown client mode: {Mode}"),
        };

    /// <summary>
    /// Converts to the internal native options type.
    /// </summary>
    internal Native.ClientOptions ToNative() =>
        ToNativeBase() with
        {
            StateCollections = StateCollections is null
                ? null
                : Array.ConvertAll(StateCollections, definition => definition.ToNative()),
            StateCacheDir = StateCacheDir,
            StateOwnedCacheSize = StateOwnedCacheSize,
            StateReadCacheSize = StateReadCacheSize,
            StateReadCacheTtl = StateReadCache?.Ttl,
            StateReadCacheDisabled = StateReadCache?.IsDisabled,
            Subsystem = Subsystem,
        };

    private Native.ClientOptions ToNativeBase() =>
        new(
            BootstrapServers: BootstrapServers,
            GroupId: GroupId,
            SubscribedTopics: SubscribedTopics,
            Mode: ToNativeMode(),
            AllowedEvents: AllowedEvents,
            SourceSystem: SourceSystem,
            Mock: Mock,
            PeerBindAddress: PeerBindAddress?.ToString(),
            PeerAdvertisedConnect: PeerAdvertisedConnect?.OriginalString,
            PeerNetworkName: PeerNetworkName,
            PeerCacheCapacity: PeerCacheCapacity,
            PeerRegistrationTtl: PeerRegistrationTtl,
            MaxConcurrency: MaxConcurrency,
            MaxUncommitted: MaxUncommitted,
            IdempotenceCacheSize: IdempotenceCacheSize,
            IdempotenceVersion: IdempotenceVersion,
            IdempotenceTtl: IdempotenceTtl,
            Timeout: Timeout,
            StallThreshold: StallThreshold,
            ShutdownTimeout: ShutdownTimeout,
            PollInterval: PollInterval,
            CommitInterval: CommitInterval,
            ProbePort: ProbePort,
            SlabSize: SlabSize,
            SendTimeout: SendTimeout,
            MaxRetries: MaxRetries,
            RetryBase: RetryBase,
            MaxRetryDelay: MaxRetryDelay,
            FailureTopic: FailureTopic,
            DeferEnabled: DeferEnabled,
            DeferBase: DeferBase,
            DeferMaxDelay: DeferMaxDelay,
            DeferFailureThreshold: DeferFailureThreshold,
            DeferFailureWindow: DeferFailureWindow,
            DeferStoreCacheSize: DeferStoreCacheSize,
            LoaderCacheSize: LoaderCacheSize,
            LoaderSeekTimeout: LoaderSeekTimeout,
            LoaderDiscardThreshold: LoaderDiscardThreshold,
            MonopolizationEnabled: MonopolizationEnabled,
            MonopolizationThreshold: MonopolizationThreshold,
            MonopolizationWindow: MonopolizationWindow,
            MonopolizationCacheSize: MonopolizationCacheSize,
            SchedulerFailureWeight: SchedulerFailureWeight,
            SchedulerMaxWait: SchedulerMaxWait,
            SchedulerWaitWeight: SchedulerWaitWeight,
            SchedulerCacheSize: SchedulerCacheSize,
            CassandraNodes: CassandraNodes,
            CassandraKeyspace: CassandraKeyspace,
            CassandraDatacenter: CassandraDatacenter,
            CassandraRack: CassandraRack,
            CassandraUser: CassandraUser,
            CassandraPassword: CassandraPassword,
            CassandraRetention: CassandraRetention,
            TelemetryTopic: TelemetryTopic,
            TelemetryEnabled: TelemetryEnabled,
            MessageSpans: ToNativeSpanRelation(MessageSpans),
            TimerSpans: ToNativeSpanRelation(TimerSpans)
        );
}
