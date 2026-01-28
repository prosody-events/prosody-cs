//! FFI type definitions for crossing the Rust-C# boundary.
//!
//! This module defines types that are passed across the FFI boundary:
//! - [`ClientOptions`]: Configuration for creating a `ProsodyClient`
//! - [`ConsumerState`]: Consumer lifecycle state enum

/// Consumer lifecycle state.
///
/// # State Transitions
///
/// | From | To | Trigger |
/// |------|-----|---------|
/// | `Unconfigured` | `Configured` | Constructor with valid options |
/// | `Configured` | `Running` | `subscribe()` called |
/// | `Running` | `Configured` | `unsubscribe()` called |
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, uniffi::Enum)]
pub enum ConsumerState {
    /// Consumer has not been configured (no subscription topics set).
    #[default]
    Unconfigured,
    /// Consumer is configured but not actively consuming.
    Configured,
    /// Consumer is actively consuming messages.
    Running,
}

/// Configuration for creating a `ProsodyClient`.
///
/// Contains fields across 10 categories, matching all sibling wrappers
/// (prosody-js, prosody-py, prosody-rb) for full configuration parity.
///
/// # Categories
///
/// 1. Core Kafka Configuration (3 required, 3 optional)
/// 2. Operating Mode
/// 3. Concurrency & Limits
/// 4. Timing Configuration
/// 5. Retry Configuration
/// 6. Health Probe
/// 7. Cassandra Configuration
/// 8. Scheduler Configuration
/// 9. Monopolization Configuration
/// 10. Defer Configuration
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct ClientOptions {
    // ============================================================
    // Category 1: Core Kafka Configuration (3 required, 3 optional)
    // ============================================================
    /// Kafka bootstrap servers (comma-separated). **REQUIRED**.
    pub bootstrap_servers: String,

    /// Consumer group ID. **REQUIRED**.
    pub group_id: String,

    /// Subscribed topics (comma-separated). **REQUIRED**.
    pub subscribed_topics: String,

    /// Allowed event type prefixes (comma-separated).
    /// `None` = all events allowed.
    pub allowed_events: Option<String>,

    /// Source system identifier. `None` = defaults to `group_id`.
    pub source_system: Option<String>,

    /// Use mock client for testing.
    pub mock: Option<bool>,

    // ============================================================
    // Category 2: Operating Mode
    // ============================================================
    /// Mode: `0` = Pipeline, `1` = `LowLatency`, `2` = `BestEffort`.
    pub mode: Option<i32>,

    // ============================================================
    // Category 3: Concurrency & Limits
    // ============================================================
    /// Maximum global concurrency limit.
    pub max_concurrency: Option<u32>,

    /// Max uncommitted messages.
    pub max_uncommitted: Option<u32>,

    /// Max enqueued messages per key.
    pub max_enqueued_per_key: Option<u32>,

    /// Size of idempotence cache. `0` = disable.
    pub idempotence_cache_size: Option<u32>,

    // ============================================================
    // Category 4: Timing Configuration
    // ============================================================
    /// Send timeout in milliseconds.
    pub send_timeout_ms: Option<u64>,

    /// Stall threshold in milliseconds.
    pub stall_threshold_ms: Option<u64>,

    /// Shutdown timeout in milliseconds.
    pub shutdown_timeout_ms: Option<u64>,

    /// Poll interval in milliseconds.
    pub poll_interval_ms: Option<u64>,

    /// Commit interval in milliseconds.
    pub commit_interval_ms: Option<u64>,

    /// Handler timeout in milliseconds.
    pub timeout_ms: Option<u64>,

    /// Timer slab size in milliseconds.
    pub slab_size_ms: Option<u64>,

    // ============================================================
    // Category 5: Retry Configuration
    // ============================================================
    /// Initial retry backoff in milliseconds.
    pub retry_base_ms: Option<u64>,

    /// Maximum retry delay in milliseconds.
    pub max_retry_delay_ms: Option<u64>,

    /// Maximum retry attempts. `0` = unlimited.
    pub max_retries: Option<u32>,

    /// Failure topic for `LowLatency` mode.
    pub failure_topic: Option<String>,

    // ============================================================
    // Category 6: Health Probe
    // ============================================================
    /// Probe port. `-1` = disabled.
    pub probe_port: Option<i32>,

    // ============================================================
    // Category 7: Cassandra Configuration
    // ============================================================
    /// Cassandra contact nodes (comma-separated).
    pub cassandra_nodes: Option<String>,

    /// Cassandra keyspace.
    pub cassandra_keyspace: Option<String>,

    /// Cassandra datacenter.
    pub cassandra_datacenter: Option<String>,

    /// Cassandra rack.
    pub cassandra_rack: Option<String>,

    /// Cassandra username.
    pub cassandra_user: Option<String>,

    /// Cassandra password.
    pub cassandra_password: Option<String>,

    /// Cassandra retention in seconds.
    pub cassandra_retention_seconds: Option<u64>,

    // ============================================================
    // Category 8: Scheduler Configuration
    // ============================================================
    /// Failure task bandwidth weight (0-10000, representing 0.0-1.0).
    pub scheduler_failure_weight: Option<u32>,

    /// Max wait urgency ramp-up in milliseconds.
    pub scheduler_max_wait_ms: Option<u64>,

    /// Max urgency boost weight (0-10000, representing 0.0-1.0).
    pub scheduler_wait_weight: Option<u32>,

    /// Virtual time cache size.
    pub scheduler_cache_size: Option<u32>,

    // ============================================================
    // Category 9: Monopolization Configuration
    // ============================================================
    /// Enable monopolization detection.
    pub monopolization_enabled: Option<bool>,

    /// Monopolization threshold (0-10000, representing 0.0-1.0).
    pub monopolization_threshold: Option<u32>,

    /// Monopolization window in milliseconds.
    pub monopolization_window_ms: Option<u64>,

    /// Monopolization cache size.
    pub monopolization_cache_size: Option<u32>,

    // ============================================================
    // Category 10: Defer Configuration
    // ============================================================
    /// Enable deferral.
    pub defer_enabled: Option<bool>,

    /// Defer base backoff in milliseconds.
    pub defer_base_ms: Option<u64>,

    /// Defer max delay in milliseconds.
    pub defer_max_delay_ms: Option<u64>,

    /// Defer failure threshold (0-10000, representing 0.0-1.0).
    pub defer_failure_threshold: Option<u32>,

    /// Defer failure window in milliseconds.
    pub defer_failure_window_ms: Option<u64>,

    /// Defer cache size.
    pub defer_cache_size: Option<u32>,

    /// Defer seek timeout in milliseconds.
    pub defer_seek_timeout_ms: Option<u64>,

    /// Defer discard threshold.
    pub defer_discard_threshold: Option<i64>,
}

// Note: ClientOptions is defined in the UDL as a dictionary.
// UniFFI scaffolding handles the FFI conversion automatically.

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn consumer_state_default_is_unconfigured() {
        assert_eq!(ConsumerState::default(), ConsumerState::Unconfigured);
    }
}
