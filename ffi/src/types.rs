//! FFI type definitions for crossing the Rust-C# boundary.
//!
//! This module defines C-compatible structures for FFI operations:
//! - [`FFIClientOptions`]: Configuration for creating a `ProsodyClient` (46 fields)
//! - [`ConsumerState`]: Consumer lifecycle state enum
//!
//! # Reference
//!
//! - Python: `../prosody-py/src/client/config.rs`
//! - JavaScript: `../prosody-js/bindings.d.ts:160-302`
//! - Ruby: `../prosody-rb/lib/prosody/configuration.rb`

use interoptopus::ffi;
use interoptopus::ffi_type;

/// Consumer lifecycle state.
///
/// # C# Mapping
///
/// ```csharp
/// public enum ConsumerState
/// {
///     Unconfigured = 0,
///     Configured = 1,
///     Running = 2,
/// }
/// ```
///
/// # State Transitions
///
/// | From | To | Trigger |
/// |------|-----|---------|
/// | `Unconfigured` | `Configured` | Constructor with valid options |
/// | `Configured` | `Running` | `SubscribeAsync()` called |
/// | `Running` | `Configured` | `UnsubscribeAsync()` called |
#[ffi_type]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ConsumerState {
    /// Consumer has not been configured (no subscription topics set).
    #[default]
    Unconfigured = 0,
    /// Consumer is configured but not actively consuming.
    Configured = 1,
    /// Consumer is actively consuming messages.
    Running = 2,
}

/// Configuration for creating a `ProsodyClient`.
///
/// Contains 46 fields across 10 categories, matching all sibling wrappers
/// (prosody-js, prosody-py, prosody-rb) for full configuration parity.
///
/// # Optionality Strategy
///
/// All optional fields use `ffi::Option<T>`:
/// - `ffi::Option::None` → C# null → Rust uses env var / system default
/// - `ffi::Option::Some(v)` → C# value → Rust uses provided value
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
///
/// # Reference
///
/// - Python: `../prosody-py/src/client/config.rs` (lines 1-510)
/// - JavaScript: `../prosody-js/bindings.d.ts` (lines 160-302)
/// - Ruby: `../prosody-rb/lib/prosody/configuration.rb` (lines 1-306)
#[ffi_type]
pub struct FFIClientOptions<'a> {
    // ============================================================
    // Category 1: Core Kafka Configuration (3 required, 3 optional)
    // ============================================================
    /// Kafka bootstrap servers (comma-separated, ASCII). **REQUIRED**.
    pub bootstrap_servers: ffi::CStrPtr<'a>,

    /// Consumer group ID (ASCII). **REQUIRED**.
    pub group_id: ffi::CStrPtr<'a>,

    /// Subscribed topics (comma-separated, ASCII). **REQUIRED**.
    pub subscribed_topics: ffi::CStrPtr<'a>,

    /// Allowed event type prefixes (comma-separated).
    /// `None` = all events allowed. Prefix matching: event passes if
    /// `event_type` starts with any allowed prefix (e.g., `"order."` matches `"order.created"`).
    pub allowed_events: ffi::Option<ffi::CStrPtr<'a>>,

    /// Source system identifier. `None` = defaults to `group_id`.
    pub source_system: ffi::Option<ffi::CStrPtr<'a>>,

    /// Use mock client for testing. `None` = use env var `PROSODY_MOCK`.
    pub mock: ffi::Option<bool>,

    // ============================================================
    // Category 2: Operating Mode
    // ============================================================
    /// Mode: `None` = use env var, `Some(0)` = Pipeline, `Some(1)` = `LowLatency`, `Some(2)` = `BestEffort`.
    pub mode: ffi::Option<i32>,

    // ============================================================
    // Category 3: Concurrency & Limits
    // ============================================================
    /// Maximum global concurrency limit. `None` = use env var `PROSODY_MAX_CONCURRENCY`.
    pub max_concurrency: ffi::Option<u32>,

    /// Max uncommitted messages. `None` = use env var.
    pub max_uncommitted: ffi::Option<u32>,

    /// Max enqueued messages per key. `None` = use env var.
    pub max_enqueued_per_key: ffi::Option<u32>,

    /// Size of idempotence cache. `None` = use env var. `Some(0)` = disable.
    pub idempotence_cache_size: ffi::Option<u32>,

    // ============================================================
    // Category 4: Timing Configuration
    // ============================================================
    /// Send timeout in milliseconds. `None` = use env var `PROSODY_SEND_TIMEOUT`.
    pub send_timeout_ms: ffi::Option<u64>,

    /// Stall threshold in milliseconds. `None` = use env var `PROSODY_STALL_THRESHOLD`.
    pub stall_threshold_ms: ffi::Option<u64>,

    /// Shutdown timeout in milliseconds. `None` = use env var.
    pub shutdown_timeout_ms: ffi::Option<u64>,

    /// Poll interval in milliseconds. `None` = use env var.
    pub poll_interval_ms: ffi::Option<u64>,

    /// Commit interval in milliseconds. `None` = use env var.
    pub commit_interval_ms: ffi::Option<u64>,

    /// Handler timeout in milliseconds. `None` = use env var (default: 80% of `stall_threshold`).
    pub timeout_ms: ffi::Option<u64>,

    /// Timer slab size in milliseconds. `None` = use env var.
    pub slab_size_ms: ffi::Option<u64>,

    // ============================================================
    // Category 5: Retry Configuration
    // ============================================================
    /// Initial retry backoff in milliseconds. `None` = use env var.
    pub retry_base_ms: ffi::Option<u64>,

    /// Maximum retry delay in milliseconds. `None` = use env var.
    pub max_retry_delay_ms: ffi::Option<u64>,

    /// Maximum retry attempts. `None` = use env var. `Some(0)` = unlimited.
    pub max_retries: ffi::Option<u32>,

    /// Failure topic for `LowLatency` mode.
    /// Only applicable when `mode = LowLatency`. `None` = no failure topic.
    pub failure_topic: ffi::Option<ffi::CStrPtr<'a>>,

    // ============================================================
    // Category 6: Health Probe
    // ============================================================
    /// Probe port. `None` = use env var. `Some(-1)` = disabled.
    pub probe_port: ffi::Option<i32>,

    // ============================================================
    // Category 7: Cassandra Configuration
    // ============================================================
    /// Cassandra contact nodes (comma-separated). `None` = use env var.
    pub cassandra_nodes: ffi::Option<ffi::CStrPtr<'a>>,

    /// Cassandra keyspace. `None` = use env var (default: `"prosody"`).
    pub cassandra_keyspace: ffi::Option<ffi::CStrPtr<'a>>,

    /// Cassandra datacenter. `None` = use env var.
    pub cassandra_datacenter: ffi::Option<ffi::CStrPtr<'a>>,

    /// Cassandra rack. `None` = use env var.
    pub cassandra_rack: ffi::Option<ffi::CStrPtr<'a>>,

    /// Cassandra username. `None` = use env var.
    pub cassandra_user: ffi::Option<ffi::CStrPtr<'a>>,

    /// Cassandra password. `None` = use env var.
    pub cassandra_password: ffi::Option<ffi::CStrPtr<'a>>,

    /// Cassandra retention in seconds. `None` = use env var (default: 30 days).
    pub cassandra_retention_seconds: ffi::Option<u64>,

    // ============================================================
    // Category 8: Scheduler Configuration
    // ============================================================
    /// Failure task bandwidth weight (0.0-1.0, encoded as `u32`: `value * 10000`).
    /// `None` = use env var.
    pub scheduler_failure_weight: ffi::Option<u32>,

    /// Max wait urgency ramp-up in milliseconds. `None` = use env var.
    pub scheduler_max_wait_ms: ffi::Option<u64>,

    /// Max urgency boost weight (encoded as `u32`: `value * 10000`). `None` = use env var.
    pub scheduler_wait_weight: ffi::Option<u32>,

    /// Virtual time cache size. `None` = use env var.
    pub scheduler_cache_size: ffi::Option<u32>,

    // ============================================================
    // Category 9: Monopolization Configuration
    // ============================================================
    /// Enable monopolization detection. `None` = use env var.
    pub monopolization_enabled: ffi::Option<bool>,

    /// Monopolization threshold (0.0-1.0, encoded as `u32`: `value * 10000`). `None` = use env var.
    pub monopolization_threshold: ffi::Option<u32>,

    /// Monopolization window in milliseconds. `None` = use env var.
    pub monopolization_window_ms: ffi::Option<u64>,

    /// Monopolization cache size. `None` = use env var.
    pub monopolization_cache_size: ffi::Option<u32>,

    // ============================================================
    // Category 10: Defer Configuration
    // ============================================================
    /// Enable deferral. `None` = use env var.
    pub defer_enabled: ffi::Option<bool>,

    /// Defer base backoff in milliseconds. `None` = use env var.
    pub defer_base_ms: ffi::Option<u64>,

    /// Defer max delay in milliseconds. `None` = use env var.
    pub defer_max_delay_ms: ffi::Option<u64>,

    /// Defer failure threshold (0.0-1.0, encoded as `u32`: `value * 10000`). `None` = use env var.
    pub defer_failure_threshold: ffi::Option<u32>,

    /// Defer failure window in milliseconds. `None` = use env var.
    pub defer_failure_window_ms: ffi::Option<u64>,

    /// Defer cache size. `None` = use env var.
    pub defer_cache_size: ffi::Option<u32>,

    /// Defer seek timeout in milliseconds. `None` = use env var.
    pub defer_seek_timeout_ms: ffi::Option<u64>,

    /// Defer discard threshold. `None` = use env var.
    pub defer_discard_threshold: ffi::Option<i64>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn consumer_state_default_is_unconfigured() {
        assert_eq!(ConsumerState::default(), ConsumerState::Unconfigured);
    }

    #[test]
    fn consumer_state_values() {
        assert_eq!(ConsumerState::Unconfigured as i32, 0_i32);
        assert_eq!(ConsumerState::Configured as i32, 1_i32);
        assert_eq!(ConsumerState::Running as i32, 2_i32);
    }
}
