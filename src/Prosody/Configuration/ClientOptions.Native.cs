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

    /// <summary>
    /// Maps <see cref="Timeout.InfiniteTimeSpan"/> to a send that never times out. Every other value
    /// takes the normal duration conversion.
    /// </summary>
    private static Native.SendTimeout? ToNativeSendTimeout(TimeSpan? timeout) =>
        timeout switch
        {
            null => null,
            { } infinite when infinite == System.Threading.Timeout.InfiniteTimeSpan =>
                new Native.SendTimeout.Unlimited(),
            { } duration => new Native.SendTimeout.Limited(Durations.ToNative(duration, nameof(SendTimeout))),
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
            SendTimeout: ToNativeSendTimeout(SendTimeout),
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
