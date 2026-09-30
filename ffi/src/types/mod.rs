//! Records and enums that cross the FFI boundary to C#.
//!
//! Each type maps to an idiomatic C# type: [`Duration`](std::time::Duration)
//! becomes `TimeSpan`, `f64` becomes `double`, and an enum stays an enum. An
//! optional field defaults to `None`, which means "use the environment
//! variable or library default".
//!
//! - `options`: the [`ClientOptions`] record.
//! - `collection`: the declaration of one keyed-state collection.

mod collection;
mod options;

pub use collection::{StateCollectionConfig, StateKind, StatePayload};
pub use options::ClientOptions;

/// Controls how a new span relates to a propagated OpenTelemetry context.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, uniffi::Enum)]
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
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, uniffi::Enum)]
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
#[derive(Debug, Clone, Default, PartialEq, Eq, uniffi::Enum)]
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

/// Optional event metadata supplied by the caller on send.
///
/// Both fields are optional. `event_id`, when present, participates in
/// producer idempotence dedup. `event_type` is carried alongside the payload
/// for downstream consumers that filter on `allowed_events`. Pulling these
/// from the typed object on the C# side avoids re-parsing the JSON payload
/// in Rust.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct EventMetadata {
    /// Stable identifier for the event, used by producer idempotence dedup.
    #[uniffi(default = None)]
    pub event_id: Option<String>,

    /// Event-type tag, used by consumer-side `allowed_events` filtering.
    #[uniffi(default = None)]
    pub event_type: Option<String>,
}
