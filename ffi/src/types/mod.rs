//! Records and enums that cross the FFI boundary to C#.
//!
//! Each type maps to an idiomatic C# type: [`Duration`](std::time::Duration)
//! becomes `TimeSpan`, `f64` becomes `double`, and an enum stays an enum. An
//! optional field defaults to `None`, which means "use the environment
//! variable or library default".
//!
//! - `options`: the [`ClientOptions`] record.
//! - `collection`: the declaration of one keyed-state collection and the read
//!   cache policy.

mod collection;
mod options;

pub use collection::{ReadCache, StateCollectionConfig, StateKind, StatePayload};
pub use options::ClientOptions;

/// Controls how a new span relates to a propagated OpenTelemetry context.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, uniffi::Enum)]
pub enum SpanRelation {
    /// The propagated span becomes the parent.
    #[default]
    Child,
    /// The propagated span becomes a link, and a new trace starts.
    FollowsFrom,
}

/// Determines how the client handles message processing failures.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, uniffi::Enum)]
pub enum ClientMode {
    /// Retries failed messages without limit through deferral.
    #[default]
    Pipeline,

    /// Retries a bounded number of times, then sends to the failure topic.
    LowLatency,

    /// Logs failures and does not retry.
    BestEffort,
}

/// The lifecycle state of a consumer.
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

/// Event metadata that C# reads from the typed payload, so Rust does not
/// parse the JSON.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct EventMetadata {
    /// Stable identifier for the event, used by producer idempotence dedup.
    #[uniffi(default = None)]
    pub event_id: Option<String>,

    /// Event-type tag, used by consumer-side `allowed_events` filtering.
    #[uniffi(default = None)]
    pub event_type: Option<String>,
}
