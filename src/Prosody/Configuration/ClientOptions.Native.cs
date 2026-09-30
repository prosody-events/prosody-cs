using Prosody.Infrastructure;

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
    /// <exception cref="ArgumentOutOfRangeException">A duration option is negative.</exception>
    internal Native.ClientOptions ToNative() =>
        ToNativeBase() with
        {
            StateCollections = StateCollections is null
                ? null
                : Array.ConvertAll(StateCollections, definition => definition.ToNative()),
            StateCacheDir = StateCacheDir,
            StateOwnedCacheSize = StateOwnedCacheSize,
            StateMemtableSize = StateMemtableSize,
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
            PeerRegistrationTtl: Durations.ToNative(PeerRegistrationTtl),
            MaxConcurrency: MaxConcurrency,
            MaxUncommitted: MaxUncommitted,
            IdempotenceCacheSize: IdempotenceCacheSize,
            IdempotenceVersion: IdempotenceVersion,
            IdempotenceTtl: Durations.ToNative(IdempotenceTtl),
            Timeout: Durations.ToNative(Timeout),
            StallThreshold: Durations.ToNative(StallThreshold),
            ShutdownTimeout: Durations.ToNative(ShutdownTimeout),
            PollInterval: Durations.ToNative(PollInterval),
            CommitInterval: Durations.ToNative(CommitInterval),
            StatisticsInterval: Durations.ToNative(StatisticsInterval),
            ProbePort: ProbePort,
            SlabSize: Durations.ToNative(SlabSize),
            SendTimeout: Durations.ToNative(SendTimeout),
            MaxRetries: MaxRetries,
            RetryBase: Durations.ToNative(RetryBase),
            MaxRetryDelay: Durations.ToNative(MaxRetryDelay),
            FailureTopic: FailureTopic,
            DeferEnabled: DeferEnabled,
            DeferBase: Durations.ToNative(DeferBase),
            DeferMaxDelay: Durations.ToNative(DeferMaxDelay),
            DeferFailureThreshold: DeferFailureThreshold,
            DeferFailureWindow: Durations.ToNative(DeferFailureWindow),
            DeferStoreCacheSize: DeferStoreCacheSize,
            LoaderCacheSize: LoaderCacheSize,
            LoaderSeekTimeout: Durations.ToNative(LoaderSeekTimeout),
            LoaderDiscardThreshold: LoaderDiscardThreshold,
            MonopolizationEnabled: MonopolizationEnabled,
            MonopolizationThreshold: MonopolizationThreshold,
            MonopolizationWindow: Durations.ToNative(MonopolizationWindow),
            MonopolizationCacheSize: MonopolizationCacheSize,
            SchedulerFailureWeight: SchedulerFailureWeight,
            SchedulerMaxWait: Durations.ToNative(SchedulerMaxWait),
            SchedulerWaitWeight: SchedulerWaitWeight,
            SchedulerCacheSize: SchedulerCacheSize,
            CassandraNodes: CassandraNodes,
            CassandraKeyspace: CassandraKeyspace,
            CassandraDatacenter: CassandraDatacenter,
            CassandraRack: CassandraRack,
            CassandraUser: CassandraUser,
            CassandraPassword: CassandraPassword,
            CassandraRetention: Durations.ToNative(CassandraRetention),
            TelemetryTopic: TelemetryTopic,
            TelemetryEnabled: TelemetryEnabled,
            MessageSpans: ToNativeSpanRelation(MessageSpans),
            TimerSpans: ToNativeSpanRelation(TimerSpans)
        );
}
