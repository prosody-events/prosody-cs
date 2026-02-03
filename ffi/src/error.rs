//! Error types for FFI boundary crossing.
//!
//! This module defines error types that can be passed across the FFI boundary
//! using `UniFFI`'s error handling mechanism.

use prosody::error::{ClassifyError, ErrorCategory};
use std::ffi::NulError;

/// Error type for FFI boundary operations.
///
/// `UniFFI` generates corresponding C# exception types.
#[derive(Debug, thiserror::Error, uniffi::Error)]
pub enum FfiError {
    /// Invalid argument provided (e.g., invalid timestamp).
    #[error("Invalid argument: {0}")]
    InvalidArgument(String),

    /// Invalid context (e.g., using an already-completed event).
    #[error("Invalid context")]
    InvalidContext,

    /// Internal error in the Prosody library.
    #[error("Internal error")]
    Internal,

    /// Operation was cancelled.
    #[error("Operation cancelled")]
    Cancelled,

    /// Topic name contains a null byte.
    #[error("Topic contains null byte: {0}")]
    TopicContainsNul(String),

    /// JSON serialization/deserialization error.
    #[error("JSON error: {0}")]
    Json(String),
}

impl From<NulError> for FfiError {
    fn from(err: NulError) -> Self {
        Self::TopicContainsNul(format!("{err:#}"))
    }
}

impl From<simd_json::Error> for FfiError {
    fn from(err: simd_json::Error) -> Self {
        Self::Json(format!("{err:#}"))
    }
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

// Required for UniFFI foreign trait error handling
impl From<uniffi::UnexpectedUniFFICallbackError> for FfiError {
    fn from(_: uniffi::UnexpectedUniFFICallbackError) -> Self {
        Self::Internal
    }
}
