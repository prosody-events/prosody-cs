//! The [`ClientOptions`] record that configures a Prosody client.

use std::time::Duration;

use super::{ClientMode, ReadCache, SpanRelation, StateCollectionConfig};

/// The options that C# `ClientOptions` fills. `None` means the default.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct ClientOptions {
    // Core options
    /// Kafka bootstrap servers for initial cluster connection.
    #[uniffi(default = None)]
    pub bootstrap_servers: Option<Vec<String>>,

    /// Consumer group ID, typically your application name.
    #[uniffi(default = None)]
    pub group_id: Option<String>,

    /// Topics to subscribe to for message consumption.
    #[uniffi(default = None)]
    pub subscribed_topics: Option<Vec<String>>,

    /// Operating mode controlling failure handling behavior.
    #[uniffi(default = None)]
    pub mode: Option<ClientMode>,

    /// Event type prefixes to process; `None` allows all events.
    #[uniffi(default = None)]
    pub allowed_events: Option<Vec<String>>,

    /// Source system identifier attached to outgoing messages.
    #[uniffi(default = None)]
    pub source_system: Option<String>,

    /// Enables in-memory mock client for testing.
    #[uniffi(default = None)]
    pub mock: Option<bool>,

    /// Address for the peer listener.
    #[uniffi(default = None)]
    pub peer_bind_address: Option<String>,

    /// gRPC connect URI that other clients use for this client.
    #[uniffi(default = None)]
    pub peer_advertised_connect: Option<String>,

    /// Network name used to identify direct routes.
    #[uniffi(default = None)]
    pub peer_network_name: Option<String>,

    /// Maximum number of peer channels and registrations in each cache.
    #[uniffi(default = None)]
    pub peer_cache_capacity: Option<u64>,

    /// Duration of each peer registration lease.
    #[uniffi(default = None)]
    pub peer_registration_ttl: Option<Duration>,

    // Consumer options
    /// Maximum messages processed concurrently.
    #[uniffi(default = None)]
    pub max_concurrency: Option<u32>,

    /// Maximum uncommitted messages before pausing consumption.
    #[uniffi(default = None)]
    pub max_uncommitted: Option<u32>,

    /// Capacity of the idempotence and deduplication caches.
    #[uniffi(default = None)]
    pub idempotence_cache_size: Option<u32>,

    /// Version string for deduplication hashes.
    #[uniffi(default = None)]
    pub idempotence_version: Option<String>,

    /// TTL for deduplication records in Cassandra.
    #[uniffi(default = None)]
    pub idempotence_ttl: Option<Duration>,

    /// Maximum handler execution time before cancellation.
    #[uniffi(default = None)]
    pub timeout: Option<Duration>,

    /// Duration without progress before reporting unhealthy.
    #[uniffi(default = None)]
    pub stall_threshold: Option<Duration>,

    /// Grace period for in-flight work during shutdown.
    #[uniffi(default = None)]
    pub shutdown_timeout: Option<Duration>,

    /// Interval between Kafka poll operations.
    #[uniffi(default = None)]
    pub poll_interval: Option<Duration>,

    /// Interval between offset commits to Kafka.
    #[uniffi(default = None)]
    pub commit_interval: Option<Duration>,

    /// Interval between librdkafka statistics reports.
    #[uniffi(default = None)]
    pub statistics_interval: Option<Duration>,

    /// HTTP port for health check endpoints (`/livez`, `/readyz`).
    #[uniffi(default = None)]
    pub probe_port: Option<u16>,

    /// Timer storage bucket granularity.
    #[uniffi(default = None)]
    pub slab_size: Option<Duration>,

    // Producer options
    /// Maximum time to wait for message delivery acknowledgment.
    #[uniffi(default = None)]
    pub send_timeout: Option<Duration>,

    // Retry options
    /// Low-latency retries before routing to the failure topic.
    #[uniffi(default = None)]
    pub max_retries: Option<u32>,

    /// Initial delay for exponential backoff between retries.
    #[uniffi(default = None)]
    pub retry_base: Option<Duration>,

    /// Maximum delay between retry attempts.
    #[uniffi(default = None)]
    pub max_retry_delay: Option<Duration>,

    /// Dead-letter topic for unprocessable messages.
    #[uniffi(default = None)]
    pub failure_topic: Option<String>,

    // Deferral options (Pipeline mode)
    /// Enables message deferral for transient failures.
    #[uniffi(default = None)]
    pub defer_enabled: Option<bool>,

    /// Initial delay before retrying a deferred message.
    #[uniffi(default = None)]
    pub defer_base: Option<Duration>,

    /// Maximum delay between deferred retry attempts.
    #[uniffi(default = None)]
    pub defer_max_delay: Option<Duration>,

    /// Failure rate threshold for disabling deferral.
    #[uniffi(default = None)]
    pub defer_failure_threshold: Option<f64>,

    /// Time window for measuring failure rate.
    #[uniffi(default = None)]
    pub defer_failure_window: Option<Duration>,

    /// Maximum entries in the write-through cache of each Cassandra defer
    /// store.
    #[uniffi(default = None)]
    pub defer_store_cache_size: Option<u32>,

    // Kafka message loader options (all modes)
    /// Maximum messages retained by the shared Kafka loader.
    #[uniffi(default = None)]
    pub loader_cache_size: Option<u32>,

    /// Timeout for Kafka loader seek operations.
    #[uniffi(default = None)]
    pub loader_seek_timeout: Option<Duration>,

    /// Sequential-read distance before the loader seeks.
    #[uniffi(default = None)]
    pub loader_discard_threshold: Option<u32>,

    // Monopolization detection options (Pipeline mode)
    /// Enables hot key detection and throttling.
    #[uniffi(default = None)]
    pub monopolization_enabled: Option<bool>,

    /// Processing time fraction that triggers monopolization throttling.
    #[uniffi(default = None)]
    pub monopolization_threshold: Option<f64>,

    /// Time window for measuring key processing time.
    #[uniffi(default = None)]
    pub monopolization_window: Option<Duration>,

    /// Maximum distinct keys tracked for monopolization detection.
    #[uniffi(default = None)]
    pub monopolization_cache_size: Option<u32>,

    // Fair scheduling options (all modes)
    /// Fraction of processing capacity reserved for retry attempts.
    #[uniffi(default = None)]
    pub scheduler_failure_weight: Option<f64>,

    /// Wait duration for maximum priority boost.
    #[uniffi(default = None)]
    pub scheduler_max_wait: Option<Duration>,

    /// Priority boost multiplier for waiting messages.
    #[uniffi(default = None)]
    pub scheduler_wait_weight: Option<f64>,

    /// Maximum distinct keys tracked by the fair scheduler.
    #[uniffi(default = None)]
    pub scheduler_cache_size: Option<u32>,

    // Cassandra options (required for timers in non-mock mode)
    /// Cassandra contact nodes for timer storage.
    #[uniffi(default = None)]
    pub cassandra_nodes: Option<Vec<String>>,

    /// Cassandra keyspace for timer tables.
    #[uniffi(default = None)]
    pub cassandra_keyspace: Option<String>,

    /// Cassandra datacenter for datacenter-aware load balancing.
    #[uniffi(default = None)]
    pub cassandra_datacenter: Option<String>,

    /// Cassandra rack for rack-aware load balancing within a datacenter.
    #[uniffi(default = None)]
    pub cassandra_rack: Option<String>,

    /// Username for Cassandra authentication.
    #[uniffi(default = None)]
    pub cassandra_user: Option<String>,

    /// Password for Cassandra authentication.
    #[uniffi(default = None)]
    pub cassandra_password: Option<String>,

    /// Retention period for timer data.
    #[uniffi(default = None)]
    pub cassandra_retention: Option<Duration>,

    // Telemetry options
    /// Kafka topic to produce telemetry events to.
    #[uniffi(default = None)]
    pub telemetry_topic: Option<String>,

    /// Enables or disables the telemetry emitter.
    #[uniffi(default = None)]
    pub telemetry_enabled: Option<bool>,

    /// Span linking for message execution spans.
    #[uniffi(default = None)]
    pub message_spans: Option<SpanRelation>,

    /// Span linking for timer execution spans.
    #[uniffi(default = None)]
    pub timer_spans: Option<SpanRelation>,

    // Keyed-state options
    /// Keyed-state collections to register before subscribe.
    #[uniffi(default = None)]
    pub state_collections: Option<Vec<StateCollectionConfig>>,

    /// Directory that holds the local keyed-state caches.
    #[uniffi(default = None)]
    pub state_cache_dir: Option<String>,

    /// Capacity of the owning keyed-state cache, such as `64 MiB`.
    #[uniffi(default = None)]
    pub state_owned_cache_size: Option<String>,

    /// In-memory write bytes for each partition before a flush to disk.
    #[uniffi(default = None)]
    pub state_memtable_size: Option<String>,

    /// Capacity of the published-state read cache, such as `1 MiB`.
    #[uniffi(default = None)]
    pub state_read_cache_size: Option<String>,

    /// Default published-state read cache policy.
    #[uniffi(default = None)]
    pub state_read_cache: Option<ReadCache>,

    /// Subsystem under which published collections are advertised.
    #[uniffi(default = None)]
    pub subsystem: Option<String>,
}
