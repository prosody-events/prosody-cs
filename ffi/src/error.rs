//! Error types for FFI boundary crossing.
//!
//! This module defines error types that can be passed across the FFI boundary
//! using `UniFFI`'s error handling mechanism.

use prosody::error::{ClassifyError, ErrorCategory};
use std::ffi::NulError;

/// Error type for Prosody FFI operations.
///
/// This enum maps to the `ProsodyError` error enum in the UDL file.
/// `UniFFI` generates corresponding C# exception types.
#[derive(Debug, thiserror::Error, uniffi::Error)]
pub enum ProsodyError {
    /// Invalid argument provided to FFI function.
    #[error("Invalid argument")]
    InvalidArgument,

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
    #[error("Topic contains null byte")]
    TopicContainsNul,
}

impl From<NulError> for ProsodyError {
    fn from(_: NulError) -> Self {
        Self::TopicContainsNul
    }
}

/// Error type for C# handler errors.
///
/// This is used internally to represent errors from C# callback implementations.
#[derive(Debug, thiserror::Error)]
pub enum CSharpHandlerError {
    /// Transient error - should retry.
    #[error("Transient error")]
    Transient,

    /// Permanent error - should not retry.
    #[error("Permanent error")]
    Permanent,

    /// Handler was cancelled.
    #[error("Handler cancelled")]
    Cancelled,
}

impl ClassifyError for CSharpHandlerError {
    fn classify_error(&self) -> ErrorCategory {
        match self {
            Self::Transient => ErrorCategory::Transient,
            // Cancellation is not retryable
            Self::Permanent | Self::Cancelled => ErrorCategory::Permanent,
        }
    }
}

// Required for UniFFI foreign trait error handling
impl From<uniffi::UnexpectedUniFFICallbackError> for ProsodyError {
    fn from(_: uniffi::UnexpectedUniFFICallbackError) -> Self {
        Self::Internal
    }
}
