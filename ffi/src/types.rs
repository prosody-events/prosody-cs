//! FFI type definitions for the Prosody C# client.
//!
//! This module defines configuration and data types exposed to C# via
//! `BoltFFI`. Types are designed to be idiomatic for C# consumers while mapping
//! cleanly to the underlying Prosody builder pattern.
//!
//! # Design Principles
//!
//! - **Idiomatic C# types**: [`Duration`] maps to `TimeSpan`, `f64` to
//!   `double`, enums to enums
//! - **Optional fields with defaults**: `None` means "use environment variable
//!   or library default"
//! - **Named parameters**: C# consumers can specify only the fields they want
//!   to override

use std::time::Duration;

/// Controls how a new span relates to a propagated OpenTelemetry context.
#[boltffi::data]
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum SpanRelation {
    /// The propagated span becomes this span's `OTel` parent (child-of
    /// relationship).
    #[default]
    Child,
    /// The propagated span is added as an `OTel` link; this span starts a new
    /// trace root (follows-from relationship).
    FollowsFrom,
}

/// Determines how the client handles message processing failures.
///
/// Each mode offers different trade-offs between reliability and throughput:
///
/// - [`Pipeline`][Self::Pipeline]: Maximum reliability with automatic deferral
/// - [`LowLatency`][Self::LowLatency]: Bounded retries with dead-letter queue
/// - [`BestEffort`][Self::BestEffort]: Fire-and-forget for non-critical
///   workloads
#[boltffi::data]
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum ClientMode {
    /// Retries failed messages indefinitely using deferral and monopolization
    /// detection.
    ///
    /// This is the default mode for production workloads where no message loss
    /// is acceptable. Failed messages are deferred and retried with exponential
    /// backoff. Hot keys that monopolize processing are automatically
    /// throttled.
    #[default]
    Pipeline,

    /// Retries a bounded number of times, then sends to a dead-letter topic.
    ///
    /// Use when you need predictable latency and can reprocess failures later.
    /// Requires [`ClientOptions::failure_topic`] to be set.
    LowLatency,

    /// Logs failures and moves on without retrying.
    ///
    /// Use for development, testing, or workloads where occasional message
    /// loss is acceptable.
    BestEffort,
}

/// Represents the current lifecycle state of a consumer.
///
/// The normal lifecycle progresses linearly:
/// [`Unconfigured`][Self::Unconfigured] -> [`Configured`][Self::Configured] ->
/// [`Running`][Self::Running].
///
/// If the consumer configuration fails during build (e.g. invalid mode,
/// missing required fields), the state transitions to
/// [`ConfigurationFailed`][Self::ConfigurationFailed] instead of
/// [`Configured`][Self::Configured].
#[boltffi::data]
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub enum ConsumerState {
    /// Initial state before configuration is applied.
    #[default]
    Unconfigured,

    /// Configuration applied but consumption not yet started.
    Configured,

    /// Actively polling and processing messages.
    Running,

    /// The client is shut down.
    Shutdown,

    /// Configuration failed during build.
    ConfigurationFailed {
        /// The error message describing the configuration failure.
        message: String,
    },
}

/// The kind of a keyed-state collection.
#[boltffi::data]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StateKind {
    /// A single-value collection.
    Value,
    /// A `String`-keyed ordered map.
    Map,
    /// A deque.
    Deque,
}

/// The item payload of a keyed-state collection.
#[boltffi::data]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StatePayload {
    /// JSON documents crossing as raw bytes.
    Json,
    /// The full Kafka message the handler received.
    Message,
}

/// Declares one keyed-state collection to register before subscribe.
#[boltffi::data]
#[derive(Debug, Clone)]
pub struct StateCollectionConfig {
    /// The collection name. Must be non-empty and unique within the client's
    /// definition set.
    pub name: String,

    /// The collection kind.
    pub kind: StateKind,

    /// The item payload.
    pub payload: StatePayload,

    /// Optional per-write TTL. Must be a whole number of seconds of at least 1
    /// (fractional and sub-second values are rejected) and must exceed the
    /// recovery delay (the latter checked at consumer build).
    pub ttl: Option<Duration>,

    /// Optional opt-out of transactional staging (read-uncommitted, at-least
    /// once). Defaults to transactional.
    pub read_uncommitted: Option<bool>,

    /// Optional map-only keyset bound (`0..=4096`; default 128 core-side; `0`
    /// disables ordered-scan tracking). Invalid on value or deque collections.
    pub keyset_limit: Option<u32>,

    /// Optional deque-only capacity bound (positive). Runtime-only — never
    /// persisted, not part of identity; enforced lazily on push. Invalid on
    /// value or map collections.
    pub capacity: Option<u32>,

    /// Whether owners advertise this collection for cross-group reads.
    pub published: bool,

    /// Per-reader cache TTL override.
    pub read_cache_ttl: Option<Duration>,

    /// Whether readers bypass their cache for this collection.
    pub read_cache_disabled: bool,
}

/// Configuration options for the Prosody client.
///
/// All fields are optional and default to `null` in C#, meaning "use the
/// environment variable or library default". Configure only the settings you
/// need to override.
///
/// # Sections
///
/// Options are grouped by functionality:
/// - **Core**: Bootstrap servers, group ID, topics, operating mode
/// - **Consumer**: Concurrency, timeouts, polling intervals
/// - **Producer**: Send timeout
/// - **Retry**: Attempt limits and backoff configuration
/// - **Deferral**: Pipeline mode message deferral settings
/// - **Monopolization**: Hot key detection and throttling
/// - **Scheduler**: Fair scheduling weights and limits
/// - **Cassandra**: Timer storage backend configuration
///
/// # Example (C#)
///
/// ```csharp
/// var options = new ClientOptions(
///     bootstrapServers: new[] { "localhost:9092" },
///     groupId: "my-app",
///     subscribedTopics: new[] { "my-topic" },
///     // Override only what you need:
///     stallThreshold: TimeSpan.FromMinutes(5),
///     mode: ClientMode.LowLatency,
///     failureTopic: "dead-letters"
/// );
/// ```
#[boltffi::data]
#[derive(Debug, Clone, Default)]
pub struct ClientOptions {
    // ========================================================================
    // Core options
    // ========================================================================
    /// Kafka bootstrap servers for initial cluster connection.
    ///
    /// Falls back to `PROSODY_BOOTSTRAP_SERVERS` environment variable if unset.
    ///
    /// **Example:** `["localhost:9092"]` or `["broker1:9092", "broker2:9092"]`
    pub bootstrap_servers: Option<Vec<String>>,

    /// Consumer group ID, typically your application name.
    ///
    /// Falls back to `PROSODY_GROUP_ID` environment variable if unset.
    pub group_id: Option<String>,

    /// Topics to subscribe to for message consumption.
    ///
    /// Falls back to `PROSODY_SUBSCRIBED_TOPICS` environment variable if unset.
    ///
    /// **Example:** `["my-topic"]` or `["topic1", "topic2"]`
    pub subscribed_topics: Option<Vec<String>>,

    /// Operating mode controlling failure handling behavior.
    ///
    /// **Default:** [`ClientMode::Pipeline`]
    pub mode: Option<ClientMode>,

    /// Event type prefixes to process; `None` allows all events.
    ///
    /// Messages with event types not matching any prefix are skipped.
    ///
    /// **Example:** `["user.", "account."]` processes only events starting
    /// with those prefixes.
    pub allowed_events: Option<Vec<String>>,

    /// Source system identifier attached to outgoing messages.
    ///
    /// Defaults to [`group_id`][Self::group_id] if unset. Set to a different
    /// value to enable consuming your own produced messages (loopback).
    pub source_system: Option<String>,

    /// Enables in-memory mock client for testing.
    ///
    /// **Default:** `false`
    pub mock: Option<bool>,

    /// Address for the peer listener.
    ///
    /// Core reads `PROSODY_PEER_BIND_ADDRESS` when this value is absent. If
    /// both values are absent, the network router selects a local address on
    /// port 9099.
    pub peer_bind_address: Option<String>,

    /// gRPC connect URI that other clients use for this client.
    ///
    /// Core reads `PROSODY_PEER_ADVERTISED_CONNECT` when this value is absent.
    pub peer_advertised_connect: Option<String>,

    /// Network name used to identify direct routes.
    ///
    /// Core reads `PROSODY_PEER_NETWORK_NAME` when this value is absent.
    pub peer_network_name: Option<String>,

    /// Maximum number of peer channels and registrations in each cache.
    ///
    /// Core reads `PROSODY_PEER_CACHE_CAPACITY` when this value is absent.
    pub peer_cache_capacity: Option<u64>,

    /// Duration of each peer registration lease.
    ///
    /// Core reads `PROSODY_PEER_REGISTRATION_TTL` when this value is absent.
    pub peer_registration_ttl: Option<Duration>,

    // ========================================================================
    // Consumer options
    // ========================================================================
    /// Maximum messages processed concurrently.
    ///
    /// **Default:** `32`
    pub max_concurrency: Option<u32>,

    /// Maximum uncommitted messages before pausing consumption.
    ///
    /// Prevents unbounded memory growth when processing lags behind ingestion.
    ///
    /// **Default:** `64`
    pub max_uncommitted: Option<u32>,

    /// Global shared cache capacity across all partitions for deduplication.
    ///
    /// Set to `0` to disable the deduplication middleware entirely.
    ///
    /// Falls back to `PROSODY_IDEMPOTENCE_CACHE_SIZE` environment variable if
    /// unset.
    ///
    /// **Default:** `8192`
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
    pub idempotence_version: Option<String>,

    /// TTL for deduplication records in Cassandra.
    ///
    /// Must be at least 1 minute. Records expire automatically after this
    /// duration.
    ///
    /// Falls back to `PROSODY_IDEMPOTENCE_TTL` environment variable if unset.
    ///
    /// **Default:** 7 days
    pub idempotence_ttl: Option<Duration>,

    /// Maximum handler execution time before cancellation.
    ///
    /// Handlers exceeding this duration are cancelled and the message is
    /// retried according to the current [`mode`][Self::mode].
    ///
    /// **Default:** 80% of [`stall_threshold`][Self::stall_threshold]
    pub timeout: Option<Duration>,

    /// Duration without progress before reporting unhealthy.
    ///
    /// The `/readyz` health endpoint returns unhealthy when no messages have
    /// been processed within this window.
    ///
    /// **Default:** 5 minutes
    pub stall_threshold: Option<Duration>,

    /// Grace period for in-flight work during shutdown.
    ///
    /// After this timeout, remaining handlers are cancelled and uncommitted
    /// work is abandoned.
    ///
    /// **Default:** 30 seconds
    pub shutdown_timeout: Option<Duration>,

    /// Interval between Kafka poll operations.
    ///
    /// Lower values reduce latency; higher values reduce CPU usage.
    ///
    /// **Default:** 100ms
    pub poll_interval: Option<Duration>,

    /// Interval between offset commits to Kafka.
    ///
    /// More frequent commits reduce duplicate processing on restart but
    /// increase broker load.
    ///
    /// **Default:** 1 second
    pub commit_interval: Option<Duration>,

    /// HTTP port for health check endpoints (`/livez`, `/readyz`).
    ///
    /// - `None`: Use default port `8000` or `PROSODY_PROBE_PORT` env var
    /// - `Some(0)`: Disable the probe server entirely
    /// - `Some(1..=65535)`: Use the specified port
    pub probe_port: Option<u16>,

    /// Timer storage bucket granularity.
    ///
    /// Controls how timers are partitioned in Cassandra. Smaller values use
    /// more storage but allow finer-grained queries. Rarely needs adjustment.
    ///
    /// **Default:** 1 hour
    pub slab_size: Option<Duration>,

    // ========================================================================
    // Producer options
    // ========================================================================
    /// Maximum time to wait for message delivery acknowledgment.
    ///
    /// Messages not acknowledged within this duration are considered failed.
    ///
    /// **Default:** 1 second
    pub send_timeout: Option<Duration>,

    // ========================================================================
    // Retry options
    // ========================================================================
    /// Low-latency retries before routing to the failure topic.
    ///
    /// Set to `0` to route the initial low-latency failure without retrying.
    /// Pipeline mode uses deferral and does not use this limit.
    ///
    /// **Default:** `3`
    pub max_retries: Option<u32>,

    /// Initial delay for exponential backoff between retries.
    ///
    /// Subsequent retries double this delay up to
    /// [`max_retry_delay`][Self::max_retry_delay].
    ///
    /// **Default:** 20ms
    pub retry_base: Option<Duration>,

    /// Maximum delay between retry attempts.
    ///
    /// Caps the exponential backoff to prevent excessively long waits.
    ///
    /// **Default:** 5 minutes
    pub max_retry_delay: Option<Duration>,

    /// Dead-letter topic for unprocessable messages.
    ///
    /// Required when using [`LowLatency`][ClientMode::LowLatency] mode.
    /// Messages exceeding [`max_retries`][Self::max_retries] are sent here.
    pub failure_topic: Option<String>,

    // ========================================================================
    // Deferral options (Pipeline mode)
    // ========================================================================
    /// Enables message deferral for transient failures.
    ///
    /// When enabled, messages that fail processing are persisted and retried
    /// later with exponential backoff. Only applies to
    /// [`Pipeline`][ClientMode::Pipeline] mode.
    ///
    /// **Default:** `true`
    pub defer_enabled: Option<bool>,

    /// Initial delay before retrying a deferred message.
    ///
    /// **Default:** 1 second
    pub defer_base: Option<Duration>,

    /// Maximum delay between deferred retry attempts.
    ///
    /// **Default:** 24 hours
    pub defer_max_delay: Option<Duration>,

    /// Failure rate threshold for disabling deferral.
    ///
    /// When the failure rate within
    /// [`defer_failure_window`][Self::defer_failure_window] exceeds this
    /// fraction, deferral is temporarily disabled to prevent
    /// cascading failures.
    ///
    /// **Range:** `0.0` to `1.0`
    ///
    /// **Default:** `0.9` (90%)
    pub defer_failure_threshold: Option<f64>,

    /// Time window for measuring failure rate.
    ///
    /// **Default:** 5 minutes
    pub defer_failure_window: Option<Duration>,

    /// Maximum deferred store cache entries per Cassandra defer store.
    ///
    /// Controls the size of the built-in write-through cache for deferred store
    /// entries (next offset/timer + retry count).
    ///
    /// **Default:** `8192`
    pub defer_store_cache_size: Option<u32>,

    // ========================================================================
    // Kafka message loader options (all modes)
    // ========================================================================
    /// Maximum messages retained by the shared Kafka loader.
    ///
    /// The loader evicts messages when it reaches this bound.
    ///
    /// **Default:** `1024`
    pub loader_cache_size: Option<u32>,

    /// Timeout for Kafka loader seek operations.
    ///
    /// **Default:** 30 seconds
    pub loader_seek_timeout: Option<Duration>,

    /// Sequential-read distance before the loader seeks.
    ///
    /// Advanced tuning parameter; rarely needs adjustment.
    ///
    /// **Default:** `100`
    pub loader_discard_threshold: Option<u32>,

    // ========================================================================
    // Monopolization detection options (Pipeline mode)
    // ========================================================================
    /// Enables hot key detection and throttling.
    ///
    /// When enabled, keys consuming excessive processing time are temporarily
    /// rejected to prevent starvation of other keys. Only applies to
    /// [`Pipeline`][ClientMode::Pipeline] mode.
    ///
    /// **Default:** `true`
    pub monopolization_enabled: Option<bool>,

    /// Processing time fraction that triggers monopolization throttling.
    ///
    /// Keys using more than this fraction of total processing time within
    /// [`monopolization_window`][Self::monopolization_window] are throttled.
    ///
    /// **Range:** `0.0` to `1.0`
    ///
    /// **Default:** `0.9` (90%)
    pub monopolization_threshold: Option<f64>,

    /// Time window for measuring key processing time.
    ///
    /// **Default:** 5 minutes
    pub monopolization_window: Option<Duration>,

    /// Maximum distinct keys tracked for monopolization detection.
    ///
    /// Limits memory usage for tracking state. Keys beyond this limit are not
    /// tracked individually.
    ///
    /// **Default:** `8192`
    pub monopolization_cache_size: Option<u32>,

    // ========================================================================
    // Fair scheduling options (all modes)
    // ========================================================================
    /// Fraction of processing capacity reserved for retry attempts.
    ///
    /// Ensures retries make progress even under high load from new messages.
    ///
    /// **Range:** `0.0` to `1.0`
    ///
    /// **Default:** `0.3` (30%)
    pub scheduler_failure_weight: Option<f64>,

    /// Wait duration for maximum priority boost.
    ///
    /// Messages waiting this long receive the full priority boost defined by
    /// [`scheduler_wait_weight`][Self::scheduler_wait_weight].
    ///
    /// **Default:** 2 minutes
    pub scheduler_max_wait: Option<Duration>,

    /// Priority boost multiplier for waiting messages.
    ///
    /// Higher values more aggressively prioritize older messages. The boost
    /// scales linearly from `0` at enqueue time to this value at
    /// [`scheduler_max_wait`][Self::scheduler_max_wait].
    ///
    /// **Default:** `200.0`
    pub scheduler_wait_weight: Option<f64>,

    /// Maximum distinct keys tracked by the fair scheduler.
    ///
    /// Limits memory usage for scheduling state.
    ///
    /// **Default:** `8192`
    pub scheduler_cache_size: Option<u32>,

    // ========================================================================
    // Cassandra options (required for timers in non-mock mode)
    // ========================================================================
    /// Cassandra contact nodes for timer storage.
    ///
    /// Required for deferral functionality when [`mock`][Self::mock] is
    /// `false`.
    ///
    /// **Example:** `["localhost:9042"]` or `["cass1:9042", "cass2:9042"]`
    pub cassandra_nodes: Option<Vec<String>>,

    /// Cassandra keyspace for timer tables.
    ///
    /// **Default:** `"prosody"`
    pub cassandra_keyspace: Option<String>,

    /// Cassandra datacenter for query routing.
    ///
    /// Used for datacenter-aware load balancing.
    pub cassandra_datacenter: Option<String>,

    /// Cassandra rack for query routing.
    ///
    /// Used for rack-aware load balancing within a datacenter.
    pub cassandra_rack: Option<String>,

    /// Username for Cassandra authentication.
    pub cassandra_user: Option<String>,

    /// Password for Cassandra authentication.
    pub cassandra_password: Option<String>,

    /// Retention period for timer data.
    ///
    /// Timer records older than this are automatically deleted via TTL.
    ///
    /// **Default:** 1 year
    pub cassandra_retention: Option<Duration>,

    // ========================================================================
    // Telemetry options
    // ========================================================================
    /// Kafka topic to produce telemetry events to.
    ///
    /// Falls back to `PROSODY_TELEMETRY_TOPIC` environment variable if unset.
    ///
    /// **Default:** `"prosody.telemetry-events"`
    pub telemetry_topic: Option<String>,

    /// Enables or disables the telemetry emitter.
    ///
    /// Falls back to `PROSODY_TELEMETRY_ENABLED` environment variable if unset.
    ///
    /// **Default:** `true`
    pub telemetry_enabled: Option<bool>,

    /// Span linking for message execution spans.
    ///
    /// Controls how the receive span connects to the `OTel` context propagated
    /// from the Kafka message producer. Falls back to `PROSODY_MESSAGE_SPANS`
    /// environment variable if unset.
    ///
    /// **Default:** `Child`
    pub message_spans: Option<SpanRelation>,

    /// Span linking for timer execution spans.
    ///
    /// Controls how timer spans connect to the `OTel` context stored when the
    /// timer was scheduled. Falls back to `PROSODY_TIMER_SPANS` environment
    /// variable if unset.
    ///
    /// **Default:** `FollowsFrom`
    pub timer_spans: Option<SpanRelation>,

    // ========================================================================
    // Keyed-state options
    // ========================================================================
    /// Keyed-state collections to register before subscribe.
    ///
    /// Each entry declares one collection by name, kind, and payload. Duplicate
    /// names within this set are rejected.
    pub state_collections: Option<Vec<StateCollectionConfig>>,

    /// Root directory for the local keyed-state cache (the committed-value
    /// workspace).
    ///
    /// Each live client needs its own directory. Falls back to the
    /// `PROSODY_STATE_CACHE_DIR` environment variable, then a per-client
    /// temporary directory. Must not be an empty string when set.
    pub state_cache_dir: Option<String>,

    /// Capacity of the owning keyed-state cache, such as `64 MiB`.
    pub state_owned_cache_size: Option<String>,

    /// Capacity of the published-state read cache, such as `1 MiB`.
    pub state_read_cache_size: Option<String>,

    /// Default published-state read cache TTL.
    pub state_read_cache_ttl: Option<Duration>,

    /// Bypasses the published-state read cache when true.
    pub state_read_cache_disabled: Option<bool>,

    /// Subsystem under which published collections are advertised.
    pub subsystem: Option<String>,

    /// Delay between staging a provisional cell and the keyed-state recovery
    /// sweep.
    ///
    /// Every registered TTL must strictly exceed this. Falls back to the
    /// `PROSODY_STATE_RECOVERY_DELAY` environment variable, then to 30
    /// seconds. Must be a whole number of seconds of at least 1 when set
    /// (fractional and sub-second values are rejected).
    pub state_recovery_delay: Option<Duration>,
}

/// Optional event metadata supplied by the caller on send.
///
/// Both fields are optional. `event_id`, when present, participates in
/// producer idempotence dedup. `event_type` is carried alongside the payload
/// for downstream consumers that filter on `allowed_events`. Pulling these
/// from the typed object on the C# side avoids re-parsing the JSON payload
/// in Rust.
#[boltffi::data]
#[derive(Debug, Clone, Default)]
pub struct EventMetadata {
    /// Stable identifier for the event, used by producer idempotence dedup.
    pub event_id: Option<String>,

    /// Event-type tag, used by consumer-side `allowed_events` filtering.
    pub event_type: Option<String>,
}
