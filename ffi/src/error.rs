//! Error types for FFI boundary crossing.
//!
//! This module defines error types that safely cross the FFI boundary using
//! `UniFFI`'s error handling mechanism. Errors are serialized to strings via
//! the `flat_error` attribute, which generates corresponding exception types
//! in C#.
//!
//! # Error Classification
//!
//! [`CsHandlerError`] implements [`ClassifyError`] to distinguish transient
//! errors (which should be retried) from permanent errors (which should not).

use std::ffi::NulError;

use prosody::admin::{ProsodyAdminClientError, TopicConfigurationBuilderError, ValidationErrors};
use prosody::cassandra::config::CassandraConfigurationBuilderError;
use prosody::codec::{BinaryCodecError, JsonExtractError};
use prosody::consumer::ConsumerConfigurationBuilderError;
use prosody::consumer::event_context::{BoxEventContextError, ErasedCategory, ErasedStateError};
use prosody::error::{ClassifyError, ErrorCategory};
use prosody::high_level::HighLevelClientError;
use prosody::high_level::erased::ErasedClientBuildError;
use prosody::loader::KafkaLoaderConfigError;
use prosody::producer::ProducerError;
use prosody::requester::RequestError;
use prosody::state_reader::StateReaderError;
use prosody::telemetry::emitter::TelemetryEmitterConfigurationBuilderError;
use prosody::timers::datetime::CompactDateTimeError;
use prosody::tracing::TracingError;
use tokio::task::JoinError;

/// Primary error type for FFI boundary operations.
///
/// `UniFFI` generates a corresponding `FfiException` type in C#. The
/// `flat_error` attribute serializes all variants to strings via their
/// `Display` implementation, preserving error messages across the language
/// boundary.
///
/// All variants support automatic conversion via [`From`] implementations,
/// allowing use of the `?` operator in FFI functions.
#[derive(Debug, thiserror::Error, uniffi::Error)]
#[uniffi(flat_error)]
pub enum FfiError {
    /// The operation was cancelled before completion.
    #[error("operation cancelled")]
    Cancelled,

    /// A topic name contains an invalid null byte.
    ///
    /// Kafka topic names must be valid C strings for interop with librdkafka.
    #[error("topic name contains null byte: {0:#}")]
    TopicContainsNul(#[from] NulError),

    /// An unexpected error occurred in a `UniFFI` callback.
    ///
    /// This typically indicates a bug in the generated bindings or a panic
    /// in callback code.
    #[error("unexpected callback error: {0:#}")]
    UnexpectedCallback(#[from] uniffi::UnexpectedUniFFICallbackError),

    /// A Kafka admin operation failed.
    ///
    /// Wraps errors from topic creation, deletion, and metadata operations.
    #[error("admin operation failed: {0:#}")]
    Admin(#[from] ProsodyAdminClientError),

    /// Configuration validation failed.
    ///
    /// One or more configuration values did not pass validation rules.
    #[error("configuration validation failed: {0:#}")]
    Validation(#[from] ValidationErrors),

    /// A telemetry emitter configuration builder could not be finalized.
    ///
    /// Occurs when an environment variable contains an invalid value for its
    /// corresponding configuration field (e.g. `PROSODY_TELEMETRY_ENABLED`
    /// is not a valid boolean).
    #[error("telemetry configuration build failed: {0:#}")]
    TelemetryConfig(#[from] TelemetryEmitterConfigurationBuilderError),

    /// Flushing or shutting down the telemetry pipeline failed.
    ///
    /// Wraps errors from exporting buffered OpenTelemetry spans/metrics or from
    /// tearing the export pipeline down at process exit.
    #[error("telemetry operation failed: {0:#}")]
    Tracing(#[from] TracingError),

    /// A Kafka message loader configuration could not be finalized.
    ///
    /// Occurs when the Kafka loader tuning derived from
    /// `loader_cache_size`, `loader_seek_timeout`, or
    /// `loader_discard_threshold` fails validation (e.g. a zero cache
    /// size).
    #[error("loader configuration build failed: {0:#}")]
    LoaderConfig(#[from] KafkaLoaderConfigError),

    /// Kafka consumer configuration is invalid or incomplete.
    #[error("consumer configuration failed: {0:#}")]
    ConsumerConfiguration(#[from] ConsumerConfigurationBuilderError),

    /// Cassandra configuration is invalid or incomplete.
    #[error("Cassandra configuration failed: {0:#}")]
    CassandraConfiguration(#[from] CassandraConfigurationBuilderError),

    /// Topic configuration is invalid or incomplete.
    #[error("topic configuration failed: {0:#}")]
    TopicConfiguration(#[from] TopicConfigurationBuilderError),

    /// A high-level client operation failed.
    ///
    /// Wraps errors from the main Prosody client API.
    #[error("client operation failed: {0:#}")]
    Client(#[from] HighLevelClientError<BinaryCodecError<JsonExtractError>>),

    /// A peer request failed before it returned subsystem results.
    #[error("request failed: {0:#}")]
    Request(#[from] RequestError<BinaryCodecError<JsonExtractError>>),

    /// Construction of the backend-erased FFI client failed.
    #[error("client construction failed: {0:#}")]
    ClientBuild(#[from] ErasedClientBuildError<BinaryCodecError<JsonExtractError>>),

    /// A producer operation failed.
    ///
    /// Occurs when publishing messages to Kafka fails.
    #[error("producer operation failed: {0:#}")]
    Producer(#[from] ProducerError<BinaryCodecError<JsonExtractError>>),

    /// An event context operation failed.
    ///
    /// Wraps errors from event acknowledgment and state management.
    #[error("event context operation failed: {0:#}")]
    EventContext(#[from] BoxEventContextError),

    /// A timestamp value is invalid or out of range.
    #[error("invalid timestamp: {0:#}")]
    CompactDateTime(#[from] CompactDateTimeError),

    /// A background task failed or panicked.
    ///
    /// Indicates that an async task did not complete successfully.
    #[error("task join failed: {0:#}")]
    Join(#[from] JoinError),

    /// A permanent keyed-state failure that must not be retried.
    ///
    /// Recovered structurally from the erased seam's
    /// [`ErasedCategory::Permanent`]: configuration or deployment mistakes
    /// (unregistered name, identity mismatch, duplicate name, invalid TTL). The
    /// `flat_error` attribute generates a distinct `FfiException` subclass, so
    /// the C# layer recovers the category from the exception type, never by
    /// parsing the message.
    #[error("permanent state error: {0}")]
    PermanentState(String),

    /// A transient keyed-state failure that may succeed on retry.
    ///
    /// Recovered structurally from the erased seam's
    /// [`ErasedCategory::Transient`], and the classification every caller/input
    /// mistake the glue detects folds into (null or unrepresentable writes,
    /// invalid values or indices) so a data-dependent handler bug
    /// retries rather than silently committing the offset and losing the
    /// message.
    #[error("transient state error: {0}")]
    TransientState(String),
}

/// Recovers the state-error category structurally from [`ErasedStateError`].
///
/// The category is read from [`ErasedStateError::category`] — never by parsing
/// the message — and mapped to the matching flat variant. The match is
/// exhaustive over [`ErasedCategory`], which has no `Terminal`, so a state
/// error is never surfaced as terminal.
///
/// This fold forwards core's category verbatim, including cases core hard-codes
/// as `Permanent` (e.g. `ErasedStateError::null_write`). Because this client
/// requires every caller mistake (null or unrepresentable writes, wrong item
/// shapes, invalid indices, invalid direction tokens) to classify transient,
/// all such validation must be performed in the glue (`crate::state`) before a
/// value crosses into core and reaches this conversion; the pre-checks there
/// are what uphold that invariant, not this generic mapping.
impl From<ErasedStateError> for FfiError {
    fn from(error: ErasedStateError) -> Self {
        match error.category() {
            ErasedCategory::Permanent => Self::PermanentState(error.message().to_owned()),
            ErasedCategory::Transient => Self::TransientState(error.message().to_owned()),
        }
    }
}

impl From<StateReaderError> for FfiError {
    fn from(error: StateReaderError) -> Self {
        match error.classify_error() {
            ErrorCategory::Permanent => Self::PermanentState(error.to_string()),
            ErrorCategory::Transient | ErrorCategory::Terminal => {
                Self::TransientState(error.to_string())
            }
        }
    }
}

/// Represents errors from C# event handler callbacks.
///
/// This type wraps errors that originate in C# code and cross back into Rust.
/// Error messages from C# exceptions are preserved for logging and diagnostics.
///
/// # Error Classification
///
/// This type implements [`ClassifyError`] to support retry logic:
/// - [`Transient`][Self::Transient] is classified as transient (retriable).
/// - An [`Ffi`][Self::Ffi]-wrapped [`FfiError::PermanentState`] classifies as
///   permanent (a config/deploy state error that escaped the handler and
///   round-tripped back through the FFI boundary); all other [`Ffi`][Self::Ffi]
///   variants are infrastructure failures and classify as transient.
/// - [`Permanent`][Self::Permanent] errors should not be retried.
#[derive(Debug, thiserror::Error)]
pub enum CsHandlerError {
    /// A transient error that may succeed on retry.
    ///
    /// The C# handler indicated the failure is temporary (e.g., network
    /// timeout, resource temporarily unavailable).
    #[error("transient error: {0}")]
    Transient(String),

    /// A permanent error that should not be retried.
    ///
    /// The C# handler indicated the failure is not recoverable (e.g., invalid
    /// data, business logic violation).
    #[error("permanent error: {0}")]
    Permanent(String),

    /// An FFI error occurred.
    ///
    /// Most variants are infrastructure failures classified as transient since
    /// they are often temporary. The exception is a wrapped
    /// [`FfiError::PermanentState`], which carries a config/deploy state error
    /// that escaped the handler and round-tripped back across the FFI boundary;
    /// it classifies as permanent so the offset is committed rather than
    /// retried forever.
    #[error(transparent)]
    Ffi(Box<FfiError>),
}

impl From<FfiError> for CsHandlerError {
    fn from(error: FfiError) -> Self {
        Self::Ffi(Box::new(error))
    }
}

/// Classifies errors for retry decisions.
///
/// Returns [`ErrorCategory::Transient`] for temporary failures that should be
/// retried, and [`ErrorCategory::Permanent`] for failures that will not succeed
/// on retry.
impl ClassifyError for CsHandlerError {
    fn classify_error(&self) -> ErrorCategory {
        match self {
            Self::Ffi(error) if matches!(error.as_ref(), FfiError::PermanentState(_)) => {
                ErrorCategory::Permanent
            }
            Self::Permanent(_) => ErrorCategory::Permanent,
            Self::Transient(_) | Self::Ffi(_) => ErrorCategory::Transient,
        }
    }
}
