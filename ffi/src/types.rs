//! FFI type definitions for the Prosody C# client.
//!
//! This module defines the configuration and data types exposed to C# via
//! `UniFFI`. Types are designed to be idiomatic for C# consumers while mapping
//! cleanly to prosody's builder pattern.
//!
//! # Design Principles
//!
//! - **Idiomatic C# types**: `Duration` → `TimeSpan`, `f64` → `double`, enums →
//!   enums
//! - **Optional fields with defaults**: `None` means "use environment variable
//!   or library default"
//! - **Required fields**: No `#[uniffi(default = None)]` attribute, must be
//!   specified
//! - **Named parameters**: C# users can specify only the fields they want to
//!   override

use std::time::Duration;

/// Client operating mode.
///
/// Determines how the client handles message processing failures.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, uniffi::Enum)]
pub enum ClientMode {
    /// Retry failed messages indefinitely. Uses defer and monopolization
    /// detection. This is the default mode for production workloads.
    #[default]
    Pipeline,

    /// Retry a few times, then send to a dead letter topic.
    /// Use when you need to keep moving and can reprocess failures later.
    /// Requires `failure_topic` to be set.
    LowLatency,

    /// Log failures and move on. No retries.
    /// Use for development or when message loss is acceptable.
    BestEffort,
}

/// Consumer state.
///
/// Represents the current lifecycle state of the consumer.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, uniffi::Enum)]
pub enum ConsumerState {
    /// Consumer has not been configured.
    #[default]
    Unconfigured,

    /// Consumer is configured but not running.
    Configured,

    /// Consumer is actively processing messages.
    Running,
}

/// Configuration options for the Prosody client.
///
/// All optional fields default to `null` in C#, which means "use the
/// environment variable or library default". Required fields must be specified.
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
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct ClientOptions {
    // ========================================================================
    // Core options
    // ========================================================================
    /// Kafka bootstrap servers to connect to.
    /// Falls back to `PROSODY_BOOTSTRAP_SERVERS` environment variable.
    ///
    /// Example: `["localhost:9092"]` or `["broker1:9092", "broker2:9092"]`
    #[uniffi(default = None)]
    pub bootstrap_servers: Option<Vec<String>>,

    /// Consumer group ID. Should be set to your application name.
    /// Falls back to `PROSODY_GROUP_ID` environment variable.
    #[uniffi(default = None)]
    pub group_id: Option<String>,

    /// Topics to subscribe to.
    /// Falls back to `PROSODY_SUBSCRIBED_TOPICS` environment variable.
    ///
    /// Example: `["my-topic"]` or `["topic1", "topic2"]`
    #[uniffi(default = None)]
    pub subscribed_topics: Option<Vec<String>>,

    /// Client operating mode. Default: `Pipeline`.
    #[uniffi(default = None)]
    pub mode: Option<ClientMode>,

    /// Allowed event type prefixes. `null` = all events allowed.
    ///
    /// Example: `["user.", "account."]` to only process events starting with
    /// those prefixes.
    #[uniffi(default = None)]
    pub allowed_events: Option<Vec<String>>,

    /// Source system identifier for outgoing messages.
    /// `null` = defaults to `group_id`.
    ///
    /// Set this to a different value than `group_id` if you need to allow
    /// your application to consume its own produced messages (loopback).
    #[uniffi(default = None)]
    pub source_system: Option<String>,

    /// Use in-memory mock client for testing. Default: `false`.
    #[uniffi(default = None)]
    pub mock: Option<bool>,

    // ========================================================================
    // Consumer options
    // ========================================================================
    /// Maximum number of messages being processed simultaneously.
    /// Default: 32.
    #[uniffi(default = None)]
    pub max_concurrency: Option<u32>,

    /// Maximum queued messages before pausing consumption.
    /// Default: 64.
    #[uniffi(default = None)]
    pub max_uncommitted: Option<u32>,

    /// Maximum queued messages per key before pausing.
    /// Default: 8.
    #[uniffi(default = None)]
    pub max_enqueued_per_key: Option<u32>,

    /// Size of LRU cache for message deduplication. Set to 0 to disable.
    /// Default: 4096.
    #[uniffi(default = None)]
    pub idempotence_cache_size: Option<u32>,

    /// Handler timeout. Handlers running longer than this are cancelled.
    /// Default: 80% of `stall_threshold`.
    #[uniffi(default = None)]
    pub timeout: Option<Duration>,

    /// Report unhealthy if no progress for this long.
    /// Default: 5 minutes.
    #[uniffi(default = None)]
    pub stall_threshold: Option<Duration>,

    /// Wait this long for in-flight work before force-quit on shutdown.
    /// Default: 30 seconds.
    #[uniffi(default = None)]
    pub shutdown_timeout: Option<Duration>,

    /// How often to fetch new messages from Kafka.
    /// Default: 100ms.
    #[uniffi(default = None)]
    pub poll_interval: Option<Duration>,

    /// How often to save progress (commit offsets) to Kafka.
    /// Default: 1 second.
    #[uniffi(default = None)]
    pub commit_interval: Option<Duration>,

    /// HTTP port for health check probes (`/livez`, `/readyz`).
    /// - `null`: use default (8000) or environment variable
    /// - `0`: explicitly disable the probe server
    /// - `1-65535`: use this port
    #[uniffi(default = None)]
    pub probe_port: Option<u16>,

    /// Timer storage granularity. Rarely needs changing.
    /// Default: 1 hour.
    #[uniffi(default = None)]
    pub slab_size: Option<Duration>,

    // ========================================================================
    // Producer options
    // ========================================================================
    /// Give up sending after this long.
    /// Default: 1 second.
    #[uniffi(default = None)]
    pub send_timeout: Option<Duration>,

    // ========================================================================
    // Retry options
    // ========================================================================
    /// Maximum retry attempts. Set to 0 for unlimited retries.
    /// Default: 3.
    #[uniffi(default = None)]
    pub max_retries: Option<u32>,

    /// Wait this long before first retry (exponential backoff base).
    /// Default: 20ms.
    #[uniffi(default = None)]
    pub retry_base: Option<Duration>,

    /// Never wait longer than this between retries.
    /// Default: 5 minutes.
    #[uniffi(default = None)]
    pub max_retry_delay: Option<Duration>,

    /// Topic for unprocessable messages (dead letter queue).
    /// Required for `LowLatency` mode.
    #[uniffi(default = None)]
    pub failure_topic: Option<String>,

    // ========================================================================
    // Deferral options (Pipeline mode)
    // ========================================================================
    /// Enable deferral for failing messages.
    /// Default: `true`.
    #[uniffi(default = None)]
    pub defer_enabled: Option<bool>,

    /// Wait this long before first deferred retry.
    /// Default: 1 second.
    #[uniffi(default = None)]
    pub defer_base: Option<Duration>,

    /// Never wait longer than this for deferred retries.
    /// Default: 24 hours.
    #[uniffi(default = None)]
    pub defer_max_delay: Option<Duration>,

    /// Disable deferral when failure rate exceeds this threshold (0.0-1.0).
    /// Default: 0.9 (90%).
    #[uniffi(default = None)]
    pub defer_failure_threshold: Option<f64>,

    /// Measure failure rate over this time window.
    /// Default: 5 minutes.
    #[uniffi(default = None)]
    pub defer_failure_window: Option<Duration>,

    /// Track this many deferred keys in memory.
    /// Default: 1024.
    #[uniffi(default = None)]
    pub defer_cache_size: Option<u32>,

    /// Timeout when loading deferred messages from Kafka.
    /// Default: 30 seconds.
    #[uniffi(default = None)]
    pub defer_seek_timeout: Option<Duration>,

    /// Read optimization threshold. Rarely needs changing.
    /// Default: 100.
    #[uniffi(default = None)]
    pub defer_discard_threshold: Option<u32>,

    // ========================================================================
    // Monopolization detection options (Pipeline mode)
    // ========================================================================
    /// Enable hot key protection.
    /// Default: `true`.
    #[uniffi(default = None)]
    pub monopolization_enabled: Option<bool>,

    /// Reject keys using more than this fraction of window time (0.0-1.0).
    /// Default: 0.9 (90%).
    #[uniffi(default = None)]
    pub monopolization_threshold: Option<f64>,

    /// Measurement window for monopolization detection.
    /// Default: 5 minutes.
    #[uniffi(default = None)]
    pub monopolization_window: Option<Duration>,

    /// Maximum distinct keys to track for monopolization.
    /// Default: 8192.
    #[uniffi(default = None)]
    pub monopolization_cache_size: Option<u32>,

    // ========================================================================
    // Fair scheduling options (all modes)
    // ========================================================================
    /// Fraction of processing time reserved for retries (0.0-1.0).
    /// Default: 0.3 (30%).
    #[uniffi(default = None)]
    pub scheduler_failure_weight: Option<f64>,

    /// Messages waiting this long get maximum priority boost.
    /// Default: 2 minutes.
    #[uniffi(default = None)]
    pub scheduler_max_wait: Option<Duration>,

    /// Priority boost multiplier for waiting messages. Higher = more
    /// aggressive. Default: 200.0.
    #[uniffi(default = None)]
    pub scheduler_wait_weight: Option<f64>,

    /// Maximum distinct keys to track in scheduler.
    /// Default: 8192.
    #[uniffi(default = None)]
    pub scheduler_cache_size: Option<u32>,

    // ========================================================================
    // Cassandra options (required for timers in non-mock mode)
    // ========================================================================
    /// Cassandra contact nodes.
    ///
    /// Example: `["localhost:9042"]` or `["cass1:9042", "cass2:9042"]`
    #[uniffi(default = None)]
    pub cassandra_nodes: Option<Vec<String>>,

    /// Cassandra keyspace name.
    /// Default: "prosody".
    #[uniffi(default = None)]
    pub cassandra_keyspace: Option<String>,

    /// Cassandra datacenter for queries.
    #[uniffi(default = None)]
    pub cassandra_datacenter: Option<String>,

    /// Cassandra rack for queries.
    #[uniffi(default = None)]
    pub cassandra_rack: Option<String>,

    /// Cassandra username.
    #[uniffi(default = None)]
    pub cassandra_user: Option<String>,

    /// Cassandra password.
    #[uniffi(default = None)]
    pub cassandra_password: Option<String>,

    /// Delete timer data older than this.
    /// Default: 1 year.
    #[uniffi(default = None)]
    pub cassandra_retention: Option<Duration>,
}
