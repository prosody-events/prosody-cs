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
use prosody::consumer::event_context::BoxEventContextError;
use prosody::error::{ClassifyError, ErrorCategory};
use prosody::high_level::HighLevelClientError;
use prosody::producer::ProducerError;
use prosody::telemetry::emitter::TelemetryEmitterConfigurationBuilderError;
use prosody::timers::datetime::CompactDateTimeError;
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

    /// JSON serialization or deserialization failed.
    ///
    /// Occurs when event payloads cannot be converted to/from JSON.
    #[error("JSON serialization failed: {0:#}")]
    Json(#[from] simd_json::Error),

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

    /// Topic configuration is invalid or incomplete.
    #[error("topic configuration failed: {0:#}")]
    TopicConfiguration(#[from] TopicConfigurationBuilderError),

    /// A high-level client operation failed.
    ///
    /// Wraps errors from the main Prosody client API.
    #[error("client operation failed: {0:#}")]
    Client(#[from] HighLevelClientError),

    /// A producer operation failed.
    ///
    /// Occurs when publishing messages to Kafka fails.
    #[error("producer operation failed: {0:#}")]
    Producer(#[from] ProducerError),

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
}

/// Represents errors from C# event handler callbacks.
///
/// This type wraps errors that originate in C# code and cross back into Rust.
/// Error messages from C# exceptions are preserved for logging and diagnostics.
///
/// # Error Classification
///
/// This type implements [`ClassifyError`] to support retry logic:
/// - [`Transient`][Self::Transient], [`Ffi`][Self::Ffi], and
///   [`Json`][Self::Json] are classified as transient (retriable).
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

    /// An FFI infrastructure error occurred.
    ///
    /// Classified as transient since infrastructure issues are often temporary.
    #[error(transparent)]
    Ffi(#[from] FfiError),

    /// JSON serialization failed during callback processing.
    ///
    /// Classified as transient to allow retry with potentially different data.
    #[error(transparent)]
    Json(#[from] simd_json::Error),
}

/// Classifies errors for retry decisions.
///
/// Returns [`ErrorCategory::Transient`] for temporary failures that should be
/// retried, and [`ErrorCategory::Permanent`] for failures that will not succeed
/// on retry.
impl ClassifyError for CsHandlerError {
    fn classify_error(&self) -> ErrorCategory {
        match self {
            Self::Transient(_) | Self::Ffi(_) | Self::Json(_) => ErrorCategory::Transient,
            Self::Permanent(_) => ErrorCategory::Permanent,
        }
    }
}
