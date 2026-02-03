//! Error types for FFI boundary crossing.
//!
//! This module defines error types that can be passed across the FFI boundary
//! using `UniFFI`'s error handling mechanism.

use std::ffi::NulError;

use prosody::admin::{ProsodyAdminClientError, TopicConfigurationBuilderError, ValidationErrors};
use prosody::consumer::event_context::BoxEventContextError;
use prosody::error::{ClassifyError, ErrorCategory};
use prosody::high_level::HighLevelClientError;
use prosody::producer::ProducerError;
use prosody::timers::datetime::CompactDateTimeError;
use tokio::task::JoinError;

/// Error type for FFI boundary operations.
///
/// `UniFFI` generates corresponding C# exception types.
/// Uses `flat_error` to serialize errors via `ToString`.
#[derive(Debug, thiserror::Error, uniffi::Error)]
#[uniffi(flat_error)]
pub enum FfiError {
    /// Operation was cancelled.
    #[error("operation cancelled")]
    Cancelled,

    /// Topic name contains a null byte.
    #[error("topic name contains null byte: {0:#}")]
    TopicContainsNul(#[from] NulError),

    /// JSON serialization/deserialization error.
    #[error("JSON serialization failed: {0:#}")]
    Json(#[from] simd_json::Error),

    /// Unexpected callback error from `UniFFI`.
    #[error("unexpected callback error: {0:#}")]
    UnexpectedCallback(#[from] uniffi::UnexpectedUniFFICallbackError),

    /// Admin client error.
    #[error("admin operation failed: {0:#}")]
    Admin(#[from] ProsodyAdminClientError),

    /// Configuration validation error.
    #[error("configuration validation failed: {0:#}")]
    Validation(#[from] ValidationErrors),

    /// Topic configuration builder error.
    #[error("topic configuration failed: {0:#}")]
    TopicConfiguration(#[from] TopicConfigurationBuilderError),

    /// High-level client error.
    #[error("client operation failed: {0:#}")]
    Client(#[from] HighLevelClientError),

    /// Producer error.
    #[error("producer operation failed: {0:#}")]
    Producer(#[from] ProducerError),

    /// Event context error.
    #[error("event context operation failed: {0:#}")]
    EventContext(#[from] BoxEventContextError),

    /// Compact datetime error.
    #[error("invalid timestamp: {0:#}")]
    CompactDateTime(#[from] CompactDateTimeError),

    /// Tokio join error.
    #[error("task join failed: {0:#}")]
    Join(#[from] JoinError),
}

/// Error type for C# handler errors.
///
/// This is used internally to represent errors from C# callback
/// implementations. Error messages from C# exceptions are preserved
/// for logging and diagnostics.
#[derive(Debug, thiserror::Error)]
pub enum CsHandlerError {
    /// Transient error - should retry.
    #[error("transient error: {0}")]
    Transient(String),

    /// Permanent error - should not retry.
    #[error("permanent error: {0}")]
    Permanent(String),

    /// FFI infrastructure error - should retry.
    #[error(transparent)]
    Ffi(#[from] FfiError),

    /// JSON serialization error - should retry.
    #[error(transparent)]
    Json(#[from] simd_json::Error),
}

impl ClassifyError for CsHandlerError {
    fn classify_error(&self) -> ErrorCategory {
        match self {
            Self::Transient(_) | Self::Ffi(_) | Self::Json(_) => ErrorCategory::Transient,
            Self::Permanent(_) => ErrorCategory::Permanent,
        }
    }
}
