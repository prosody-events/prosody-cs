//! The [`ClientOptions`] record that configures a Prosody client.

use std::time::Duration;

use super::{ClientMode, SpanRelation, StateCollectionConfig};

/// Configuration options for the Prosody client.
///
/// The C# `Prosody.Configuration.ClientOptions` class fills this record. Every
/// field is optional. `None` means "use the environment variable or library
/// default". Comments group the fields by subsystem.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct ClientOptions {
    // Core options
    /// Kafka bootstrap servers for initial cluster connection.
    ///
    /// Falls back to `PROSODY_BOOTSTRAP_SERVERS` environment variable if unset.
    ///
    /// **Example:** `["localhost:9092"]` or `["broker1:9092", "broker2:9092"]`
    #[uniffi(default = None)]
    pub bootstrap_servers: Option<Vec<String>>,

    /// Consumer group ID, typically your application name.
    ///
    /// Falls back to `PROSODY_GROUP_ID` environment variable if unset.
    #[uniffi(default = None)]
    pub group_id: Option<String>,

    /// Topics to subscribe to for message consumption.
    ///
    /// Falls back to `PROSODY_SUBSCRIBED_TOPICS` environment variable if unset.
    #[uniffi(default = None)]
    pub subscribed_topics: Option<Vec<String>>,

    /// Operating mode controlling failure handling behavior.
    ///
    /// **Default:** [`ClientMode::Pipeline`]
    #[uniffi(default = None)]
    pub mode: Option<ClientMode>,

    /// Event type prefixes to process; `None` allows all events.
    ///
    /// A message whose event type matches no prefix is skipped.
    #[uniffi(default = None)]
    pub allowed_events: Option<Vec<String>>,

    /// Source system identifier attached to outgoing messages.
    ///
    /// Defaults to [`group_id`][Self::group_id] if unset. Set to a different
    /// value to enable consuming your own produced messages (loopback).
    #[uniffi(default = None)]
    pub source_system: Option<String>,

    /// Enables in-memory mock client for testing.
    ///
    /// **Default:** `false`
    #[uniffi(default = None)]
    pub mock: Option<bool>,

    /// Address for the peer listener.
    ///
    /// Core reads `PROSODY_PEER_BIND_ADDRESS` when this value is absent. If
    /// both values are absent, the network router selects a local address on
    /// port 9099.
    #[uniffi(default = None)]
    pub peer_bind_address: Option<String>,

    /// gRPC connect URI that other clients use for this client.
    ///
    /// Core reads `PROSODY_PEER_ADVERTISED_CONNECT` when this value is absent.
    #[uniffi(default = None)]
    pub peer_advertised_connect: Option<String>,

    /// Network name used to identify direct routes.
    ///
    /// Core reads `PROSODY_PEER_NETWORK_NAME` when this value is absent.
    #[uniffi(default = None)]
    pub peer_network_name: Option<String>,

    /// Maximum number of peer channels and registrations in each cache.
    ///
    /// Core reads `PROSODY_PEER_CACHE_CAPACITY` when this value is absent.
    #[uniffi(default = None)]
    pub peer_cache_capacity: Option<u64>,

    /// Duration of each peer registration lease.
    ///
    /// Core reads `PROSODY_PEER_REGISTRATION_TTL` when this value is absent.
    #[uniffi(default = None)]
    pub peer_registration_ttl: Option<Duration>,

    // Consumer options
    /// Maximum messages processed concurrently.
    ///
    /// **Default:** `32`
    #[uniffi(default = None)]
    pub max_concurrency: Option<u32>,

    /// Maximum uncommitted messages before pausing consumption.
    ///
    /// Prevents unbounded memory growth when processing lags behind ingestion.
    ///
    /// **Default:** `64`
    #[uniffi(default = None)]
    pub max_uncommitted: Option<u32>,

    /// Global shared cache capacity across all partitions for deduplication.
    ///
    /// Must be greater than `0`. Falls back to `PROSODY_IDEMPOTENCE_CACHE_SIZE`
    /// if unset.
    ///
    /// **Default:** `8192`
    #[uniffi(default = None)]
    pub idempotence_cache_size: Option<u32>,

    /// Version string for cache-busting deduplication hashes.
    ///
    /// Changing this value invalidates all previously recorded dedup entries,
    /// causing messages to be reprocessed.
    ///
    /// Falls back to `PROSODY_IDEMPOTENCE_VERSION` environment variable if
    /// unset.
    ///
    /// **Default:** `"1"`
    #[uniffi(default = None)]
    pub idempotence_version: Option<String>,

    /// TTL for deduplication records in Cassandra.
    ///
    /// Must be at least 1 minute. Records expire automatically after this
    /// duration.
    ///
    /// Falls back to `PROSODY_IDEMPOTENCE_TTL` environment variable if unset.
    ///
    /// **Default:** 7 days
    #[uniffi(default = None)]
    pub idempotence_ttl: Option<Duration>,

    /// Maximum handler execution time before cancellation.
    ///
    /// Handlers exceeding this duration are cancelled and the message is
    /// retried according to the current [`mode`][Self::mode].
    ///
    /// **Default:** 80% of [`stall_threshold`][Self::stall_threshold]
    #[uniffi(default = None)]
    pub timeout: Option<Duration>,

    /// Duration without progress before reporting unhealthy.
    ///
    /// The `/readyz` health endpoint returns unhealthy when no messages have
    /// been processed within this window.
    ///
    /// **Default:** 5 minutes
    #[uniffi(default = None)]
    pub stall_threshold: Option<Duration>,

    /// Grace period for in-flight work during shutdown.
    ///
    /// After this timeout, remaining handlers are cancelled and uncommitted
    /// work is abandoned.
    ///
    /// **Default:** 30 seconds
    #[uniffi(default = None)]
    pub shutdown_timeout: Option<Duration>,

    /// Interval between Kafka poll operations.
    ///
    /// Lower values reduce latency; higher values reduce CPU usage.
    ///
    /// **Default:** 100ms
    #[uniffi(default = None)]
    pub poll_interval: Option<Duration>,

    /// Interval between offset commits to Kafka.
    ///
    /// More frequent commits reduce duplicate processing on restart but
    /// increase broker load.
    ///
    /// **Default:** 1 second
    #[uniffi(default = None)]
    pub commit_interval: Option<Duration>,

    /// HTTP port for health check endpoints (`/livez`, `/readyz`).
    ///
    /// - `None`: Use default port `8000` or `PROSODY_PROBE_PORT` env var
    /// - `Some(0)`: Disable the probe server entirely
    /// - `Some(1..=65535)`: Use the specified port
    #[uniffi(default = None)]
    pub probe_port: Option<u16>,

    /// Timer storage bucket granularity.
    ///
    /// Controls how timers are partitioned in Cassandra. Smaller values use
    /// more storage but allow finer-grained queries. Rarely needs adjustment.
    ///
    /// **Default:** 1 hour
    #[uniffi(default = None)]
    pub slab_size: Option<Duration>,

    // Producer options
    /// Maximum time to wait for message delivery acknowledgment.
    ///
    /// Messages not acknowledged within this duration are considered failed.
    ///
    /// **Default:** 1 second
    #[uniffi(default = None)]
    pub send_timeout: Option<Duration>,

    // Retry options
    /// Low-latency retries before routing to the failure topic.
    ///
    /// Set to `0` to route the initial low-latency failure without retrying.
    /// Pipeline mode uses deferral and does not use this limit.
    ///
    /// **Default:** `3`
    #[uniffi(default = None)]
    pub max_retries: Option<u32>,

    /// Initial delay for exponential backoff between retries.
    ///
    /// Subsequent retries double this delay up to
    /// [`max_retry_delay`][Self::max_retry_delay].
    ///
    /// **Default:** 20ms
    #[uniffi(default = None)]
    pub retry_base: Option<Duration>,

    /// Maximum delay between retry attempts.
    ///
    /// Caps the exponential backoff to prevent excessively long waits.
    ///
    /// **Default:** 5 minutes
    #[uniffi(default = None)]
    pub max_retry_delay: Option<Duration>,

    /// Dead-letter topic for unprocessable messages.
    ///
    /// Required when using [`LowLatency`][ClientMode::LowLatency] mode.
    /// Messages exceeding [`max_retries`][Self::max_retries] are sent here.
    #[uniffi(default = None)]
    pub failure_topic: Option<String>,

    // Deferral options (Pipeline mode)
    /// Enables message deferral for transient failures.
    ///
    /// When enabled, messages that fail processing are persisted and retried
    /// later with exponential backoff. Only applies to
    /// [`Pipeline`][ClientMode::Pipeline] mode.
    ///
    /// **Default:** `true`
    #[uniffi(default = None)]
    pub defer_enabled: Option<bool>,

    /// Initial delay before retrying a deferred message.
    ///
    /// **Default:** 1 second
    #[uniffi(default = None)]
    pub defer_base: Option<Duration>,

    /// Maximum delay between deferred retry attempts.
    ///
    /// **Default:** 24 hours
    #[uniffi(default = None)]
    pub defer_max_delay: Option<Duration>,

    /// Failure rate threshold for disabling deferral.
    ///
    /// Deferral pauses when the failure rate within
    /// [`defer_failure_window`][Self::defer_failure_window] exceeds this
    /// fraction. This prevents cascading failures.
    ///
    /// **Range:** `0.0` to `1.0`. **Default:** `0.9` (90%)
    #[uniffi(default = None)]
    pub defer_failure_threshold: Option<f64>,

    /// Time window for measuring failure rate.
    ///
    /// **Default:** 5 minutes
    #[uniffi(default = None)]
    pub defer_failure_window: Option<Duration>,

    /// Maximum entries in the write-through cache of each Cassandra defer
    /// store. An entry holds the next offset or timer and the retry count.
    ///
    /// **Default:** `8192`
    #[uniffi(default = None)]
    pub defer_store_cache_size: Option<u32>,

    // Kafka message loader options (all modes)
    /// Maximum messages retained by the shared Kafka loader.
    ///
    /// The loader evicts messages when it reaches this bound.
    ///
    /// **Default:** `1024`
    #[uniffi(default = None)]
    pub loader_cache_size: Option<u32>,

    /// Timeout for Kafka loader seek operations.
    ///
    /// **Default:** 30 seconds
    #[uniffi(default = None)]
    pub loader_seek_timeout: Option<Duration>,

    /// Sequential-read distance before the loader seeks. Rarely needs
    /// adjustment.
    ///
    /// **Default:** `100`
    #[uniffi(default = None)]
    pub loader_discard_threshold: Option<u32>,

    // Monopolization detection options (Pipeline mode)
    /// Enables hot key detection and throttling.
    ///
    /// When enabled, keys consuming excessive processing time are temporarily
    /// rejected to prevent starvation of other keys. Only applies to
    /// [`Pipeline`][ClientMode::Pipeline] mode.
    ///
    /// **Default:** `true`
    #[uniffi(default = None)]
    pub monopolization_enabled: Option<bool>,

    /// Processing time fraction that triggers monopolization throttling.
    ///
    /// Keys using more than this fraction of total processing time within
    /// [`monopolization_window`][Self::monopolization_window] are throttled.
    ///
    /// **Range:** `0.0` to `1.0`. **Default:** `0.9` (90%)
    #[uniffi(default = None)]
    pub monopolization_threshold: Option<f64>,

    /// Time window for measuring key processing time.
    ///
    /// **Default:** 5 minutes
    #[uniffi(default = None)]
    pub monopolization_window: Option<Duration>,

    /// Maximum distinct keys tracked for monopolization detection.
    ///
    /// Limits memory usage for tracking state. Keys beyond this limit are not
    /// tracked individually.
    ///
    /// **Default:** `8192`
    #[uniffi(default = None)]
    pub monopolization_cache_size: Option<u32>,

    // Fair scheduling options (all modes)
    /// Fraction of processing capacity reserved for retry attempts.
    ///
    /// Ensures retries make progress even under high load from new messages.
    ///
    /// **Range:** `0.0` to `1.0`. **Default:** `0.3` (30%)
    #[uniffi(default = None)]
    pub scheduler_failure_weight: Option<f64>,

    /// Wait duration for maximum priority boost.
    ///
    /// Messages waiting this long receive the full priority boost defined by
    /// [`scheduler_wait_weight`][Self::scheduler_wait_weight].
    ///
    /// **Default:** 2 minutes
    #[uniffi(default = None)]
    pub scheduler_max_wait: Option<Duration>,

    /// Priority boost multiplier for waiting messages.
    ///
    /// Higher values more aggressively prioritize older messages. The boost
    /// scales linearly from `0` at enqueue time to this value at
    /// [`scheduler_max_wait`][Self::scheduler_max_wait].
    ///
    /// **Default:** `200.0`
    #[uniffi(default = None)]
    pub scheduler_wait_weight: Option<f64>,

    /// Maximum distinct keys tracked by the fair scheduler.
    ///
    /// **Default:** `8192`
    #[uniffi(default = None)]
    pub scheduler_cache_size: Option<u32>,

    // Cassandra options (required for timers in non-mock mode)
    /// Cassandra contact nodes for timer storage.
    ///
    /// Required for deferral functionality when [`mock`][Self::mock] is
    /// `false`.
    ///
    /// **Example:** `["localhost:9042"]` or `["cass1:9042", "cass2:9042"]`
    #[uniffi(default = None)]
    pub cassandra_nodes: Option<Vec<String>>,

    /// Cassandra keyspace for timer tables.
    ///
    /// **Default:** `"prosody"`
    #[uniffi(default = None)]
    pub cassandra_keyspace: Option<String>,

    /// Cassandra datacenter for query routing.
    ///
    /// Used for datacenter-aware load balancing.
    #[uniffi(default = None)]
    pub cassandra_datacenter: Option<String>,

    /// Cassandra rack for query routing.
    ///
    /// Used for rack-aware load balancing within a datacenter.
    #[uniffi(default = None)]
    pub cassandra_rack: Option<String>,

    /// Username for Cassandra authentication.
    #[uniffi(default = None)]
    pub cassandra_user: Option<String>,

    /// Password for Cassandra authentication.
    #[uniffi(default = None)]
    pub cassandra_password: Option<String>,

    /// Retention period for timer data.
    ///
    /// Timer records older than this are automatically deleted via TTL.
    ///
    /// **Default:** 1 year
    #[uniffi(default = None)]
    pub cassandra_retention: Option<Duration>,

    // Telemetry options
    /// Kafka topic to produce telemetry events to.
    ///
    /// Falls back to `PROSODY_TELEMETRY_TOPIC` environment variable if unset.
    ///
    /// **Default:** `"prosody.telemetry-events"`
    #[uniffi(default = None)]
    pub telemetry_topic: Option<String>,

    /// Enables or disables the telemetry emitter.
    ///
    /// Falls back to `PROSODY_TELEMETRY_ENABLED` environment variable if unset.
    ///
    /// **Default:** `true`
    #[uniffi(default = None)]
    pub telemetry_enabled: Option<bool>,

    /// Span linking for message execution spans.
    ///
    /// Controls how the receive span connects to the `OTel` context propagated
    /// from the Kafka message producer. Falls back to `PROSODY_MESSAGE_SPANS`
    /// environment variable if unset.
    ///
    /// **Default:** `Child`
    #[uniffi(default = None)]
    pub message_spans: Option<SpanRelation>,

    /// Span linking for timer execution spans.
    ///
    /// Controls how timer spans connect to the `OTel` context stored when the
    /// timer was scheduled. Falls back to `PROSODY_TIMER_SPANS` environment
    /// variable if unset.
    ///
    /// **Default:** `FollowsFrom`
    #[uniffi(default = None)]
    pub timer_spans: Option<SpanRelation>,

    // Keyed-state options
    /// Keyed-state collections to register before subscribe.
    ///
    /// Each entry declares one collection by name, kind, and payload. Duplicate
    /// names within this set are rejected.
    #[uniffi(default = None)]
    pub state_collections: Option<Vec<StateCollectionConfig>>,

    /// Root directory for the local keyed-state cache (the committed-value
    /// workspace).
    ///
    /// Each live client needs its own directory. Falls back to the
    /// `PROSODY_STATE_CACHE_DIR` environment variable, then a per-client
    /// temporary directory. Must not be an empty string when set.
    #[uniffi(default = None)]
    pub state_cache_dir: Option<String>,

    /// Capacity of the owning keyed-state cache, such as `64 MiB`.
    #[uniffi(default = None)]
    pub state_owned_cache_size: Option<String>,

    /// Capacity of the published-state read cache, such as `1 MiB`.
    #[uniffi(default = None)]
    pub state_read_cache_size: Option<String>,

    /// Default published-state read cache TTL.
    #[uniffi(default = None)]
    pub state_read_cache_ttl: Option<Duration>,

    /// Bypasses the published-state read cache when true.
    #[uniffi(default = None)]
    pub state_read_cache_disabled: Option<bool>,

    /// Subsystem under which published collections are advertised.
    #[uniffi(default = None)]
    pub subsystem: Option<String>,
}
