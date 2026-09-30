//! Error types for FFI boundary crossing.
//!
//! This module defines error types that safely cross the FFI boundary using
//! `UniFFI`'s error handling mechanism. Errors are serialized to strings via
//! the `flat_error` attribute, which generates corresponding exception types
//! in C#.
//!
//! # Error Classification
//!
//! The C# layer maps each [`FfiError`] variant to one public exception type.
//! A caller mistake arrives as [`FfiError::InvalidArgument`] or
//! [`FfiError::InvalidOperation`]. A keyed-state failure arrives as
//! [`FfiError::PermanentState`] or [`FfiError::TransientState`]. Every other
//! variant is a broker or runtime failure.
//!
//! [`CsHandlerError`] implements [`ClassifyError`] to distinguish transient
//! errors (which should be retried) from permanent errors (which should not).

use std::ffi::NulError;

use prosody::admin::{ProsodyAdminClientError, TopicConfigurationBuilderError, ValidationErrors};
use prosody::cassandra::config::CassandraConfigurationBuilderError;
use prosody::codec::{BinaryCodecError, JsonExtractError};
use prosody::consumer::event_context::{BoxEventContextError, ErasedCategory, ErasedStateError};
use prosody::consumer::middleware::defer::DeferInitError;
use prosody::consumer::{ConsumerConfigurationBuilderError, ConsumerError};
use prosody::error::{ClassifyError, ErrorCategory};
use prosody::high_level::HighLevelClientError;
use prosody::high_level::erased::{ErasedClientBuildError, ErasedReaderBuildError};
use prosody::loader::KafkaLoaderConfigError;
use prosody::producer::ProducerError;
use prosody::requester::RequestError;
use prosody::state_reader::StateReaderError;
use prosody::telemetry::emitter::TelemetryEmitterConfigurationBuilderError;
use prosody::timers::datetime::CompactDateTimeError;
use prosody::tracing::TracingError;

/// The codec error type of every client operation.
type Codec = BinaryCodecError<JsonExtractError>;

/// The error of every exported call, which C# receives as `FfiException`.
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

    /// The admin client configuration is invalid, such as an empty server
    /// list.
    #[error("admin configuration failed: {0:#}")]
    AdminConfiguration(#[from] ValidationErrors),

    /// A Kafka admin operation failed.
    ///
    /// Wraps errors from topic creation, deletion, and metadata operations.
    #[error("admin operation failed: {0:#}")]
    Admin(#[from] ProsodyAdminClientError),

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

    /// A high-level client operation failed at the broker or at run time.
    ///
    /// The conversion from [`HighLevelClientError`] routes configuration and
    /// call-order errors to [`InvalidOperation`](Self::InvalidOperation).
    #[error("client operation failed: {0:#}")]
    Client(HighLevelClientError<Codec>),

    /// Kafka did not accept a request.
    ///
    /// The conversion from [`RequestError`] routes invalid arguments to
    /// [`InvalidArgument`](Self::InvalidArgument).
    #[error("request failed: {0:#}")]
    Request(RequestError<Codec>),

    /// The caller passed an argument that Prosody cannot accept.
    #[error("{0}")]
    InvalidArgument(String),

    /// The options are invalid, or the call does not suit the client state.
    #[error("{0}")]
    InvalidOperation(String),

    /// A producer operation failed.
    ///
    /// Occurs when publishing messages to Kafka fails.
    #[error("producer operation failed: {0:#}")]
    Producer(#[from] ProducerError<Codec>),

    /// An event context operation failed.
    ///
    /// Wraps errors from event acknowledgment and state management.
    #[error("event context operation failed: {0:#}")]
    EventContext(#[from] BoxEventContextError),

    /// A timestamp value is invalid or out of range.
    #[error("invalid timestamp: {0:#}")]
    CompactDateTime(#[from] CompactDateTimeError),

    /// A permanent keyed-state failure that must not be retried.
    ///
    /// Recovered structurally from the erased seam's
    /// [`ErasedCategory::Permanent`]: configuration or deployment mistakes
    /// (unregistered name, identity mismatch, duplicate name, invalid TTL) and
    /// a JSON `null` write. The
    /// `flat_error` attribute generates a distinct `FfiException` subclass, so
    /// the C# layer recovers the category from the exception type, never by
    /// parsing the message.
    #[error("permanent state error: {0}")]
    PermanentState(String),

    /// A transient keyed-state failure that may succeed on retry.
    ///
    /// Recovered structurally from the erased seam's
    /// [`ErasedCategory::Transient`]. A caller mistake that the glue detects,
    /// such as an invalid index, also folds into this category, so the event
    /// retries.
    #[error("transient state error: {0}")]
    TransientState(String),
}

/// Routes configuration and call-order errors to
/// [`FfiError::InvalidOperation`]. A state reader error keeps its category.
impl From<HighLevelClientError<Codec>> for FfiError {
    fn from(error: HighLevelClientError<Codec>) -> Self {
        match error {
            HighLevelClientError::StateReader(error) => error.into(),
            HighLevelClientError::ProducerConfiguration(_)
            | HighLevelClientError::SchedulerConfiguration(_)
            | HighLevelClientError::ConsumerConfiguration(_)
            | HighLevelClientError::StateRegistration(_)
            | HighLevelClientError::AlreadySubscribed
            | HighLevelClientError::UnconfiguredConsumer
            | HighLevelClientError::NotSubscribed
            | HighLevelClientError::Closed => Self::InvalidOperation(error.to_string()),
            HighLevelClientError::Consumer(ref consumer) if consumer_configuration(consumer) => {
                Self::InvalidOperation(error.to_string())
            }
            error @ (HighLevelClientError::Producer(_)
            | HighLevelClientError::Consumer(_)
            | HighLevelClientError::ShutdownFailed(_)
            | HighLevelClientError::TopicsNotFound(_)
            | HighLevelClientError::TelemetryEmitter(_)) => Self::Client(error),
        }
    }
}

/// Routes invalid request arguments to [`FfiError::InvalidArgument`].
impl From<RequestError<Codec>> for FfiError {
    fn from(error: RequestError<Codec>) -> Self {
        match error {
            RequestError::NoSubsystems
            | RequestError::DuplicateSubsystem { .. }
            | RequestError::ReservedHeader { .. }
            | RequestError::DeadlineOutOfRange => Self::InvalidArgument(error.to_string()),
            RequestError::ShuttingDown => Self::InvalidOperation(error.to_string()),
            error @ RequestError::Produce(_) => Self::Request(error),
        }
    }
}

/// Routes configuration errors to [`FfiError::InvalidOperation`].
impl From<ErasedClientBuildError<Codec>> for FfiError {
    fn from(error: ErasedClientBuildError<Codec>) -> Self {
        match error {
            ErasedClientBuildError::Client(error) => error.into(),
            error @ (ErasedClientBuildError::MockConfiguration(_)
            | ErasedClientBuildError::CassandraConfiguration(_)) => {
                Self::InvalidOperation(error.to_string())
            }
        }
    }
}

/// Recovers the state-error category structurally from [`ErasedStateError`].
///
/// The category is read from [`ErasedStateError::category`] — never by parsing
/// the message — and mapped to the matching flat variant. The match is
/// exhaustive over [`ErasedCategory`], which has no `Terminal`, so a state
/// error is never surfaced as terminal.
///
/// This fold forwards core's category verbatim. A JSON `null` write therefore
/// surfaces as `Permanent`, the category that core gives it.
impl From<ErasedStateError> for FfiError {
    fn from(error: ErasedStateError) -> Self {
        match error.category() {
            ErasedCategory::Permanent => Self::PermanentState(error.message().to_owned()),
            ErasedCategory::Transient => Self::TransientState(error.message().to_owned()),
        }
    }
}

/// Classifies a failure to open a published reader.
///
/// An empty subsystem name is a caller mistake. A state reader error keeps
/// the category that Prosody gives it. Any other client failure, such as a
/// broker that is not available, is transient.
impl From<ErasedReaderBuildError<Codec>> for FfiError {
    fn from(error: ErasedReaderBuildError<Codec>) -> Self {
        match error {
            ErasedReaderBuildError::InvalidSubsystem(error) => {
                Self::InvalidArgument(error.to_string())
            }
            ErasedReaderBuildError::Client(HighLevelClientError::StateReader(error)) => {
                error.into()
            }
            ErasedReaderBuildError::Client(error) => Self::TransientState(error.to_string()),
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

/// Reports whether a consumer start failed because an option is invalid.
fn consumer_configuration(error: &ConsumerError) -> bool {
    matches!(
        error,
        ConsumerError::Configuration(_)
            | ConsumerError::AllowedEventsPattern(_)
            | ConsumerError::InvalidSlabSize(_)
            | ConsumerError::Scheduler(_)
            | ConsumerError::Timeout(_)
            | ConsumerError::Monopolization(_)
            | ConsumerError::Defer(DeferInitError::Validation(_))
    )
}

/// An error that a C# event handler callback returns to Rust.
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
            // A permanent state error that escaped the handler stays permanent.
            Self::Ffi(error) if matches!(error.as_ref(), FfiError::PermanentState(_)) => {
                ErrorCategory::Permanent
            }
            Self::Permanent(_) => ErrorCategory::Permanent,
            Self::Transient(_) | Self::Ffi(_) => ErrorCategory::Transient,
        }
    }
}
