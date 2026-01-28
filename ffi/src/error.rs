//! Error types for FFI boundary crossing.
//!
//! This module defines the error types used to communicate errors from Rust
//! to C#. Uses Interoptopus 0.15's patterns for safe memory management.
//!
//! # Reference
//!
//! - prosody-js: `src/handler.rs` - `JsHandlerError` enum
//! - prosody-rb: `ext/prosody/src/handler/mod.rs` - `RubyHandlerError` enum
//! - prosody-py: `src/handler.rs` - `PythonHandlerError` enum

use std::io::{Error as IoError, ErrorKind};

use interoptopus::ffi_type;
use prosody::error::{ClassifyError, ErrorCategory};
use prosody::high_level::HighLevelClientError;
use thiserror::Error;

/// Error codes for FFI operations.
///
/// These codes map to specific C# exception types in the wrapper layer.
///
/// # C# Exception Mapping
///
/// | Code | C# Exception |
/// |------|-------------|
/// | `NullPassed` | `ArgumentNullException` |
/// | `Panic` | `ProsodyException` (internal error) |
/// | `InvalidArgument` | `ArgumentException` |
/// | `ConnectionFailed` | `ProsodyConnectionException` |
/// | `Cancelled` | `OperationCanceledException` |
/// | `Internal` | `ProsodyException` |
/// | `InvalidContext` | `InvalidOperationException` |
/// | `AlreadySubscribed` | `InvalidOperationException` |
/// | `NotSubscribed` | `InvalidOperationException` |
#[ffi_type]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum FFIErrorCode {
    /// Success - no error.
    #[default]
    Ok = 0,
    /// A null pointer was passed where a valid pointer was expected.
    NullPassed = 1,
    /// A panic occurred in Rust code.
    Panic = 2,
    /// An invalid argument was provided.
    InvalidArgument = 3,
    /// Failed to connect to Kafka or Cassandra.
    ConnectionFailed = 4,
    /// The operation was cancelled.
    Cancelled = 5,
    /// An internal error occurred.
    Internal = 6,
    /// The context was invalidated (handler returned).
    InvalidContext = 7,
    /// Attempted to subscribe when already subscribed.
    AlreadySubscribed = 8,
    /// Attempted to unsubscribe when not subscribed.
    NotSubscribed = 9,
}

impl FFIErrorCode {
    /// Returns `true` if this is a success code.
    #[must_use]
    pub const fn is_ok(self) -> bool {
        matches!(self, Self::Ok)
    }

    /// Returns `true` if this is an error code.
    #[must_use]
    pub const fn is_error(self) -> bool {
        !self.is_ok()
    }
}

impl From<&HighLevelClientError> for FFIErrorCode {
    fn from(error: &HighLevelClientError) -> Self {
        match error {
            HighLevelClientError::AlreadySubscribed => Self::AlreadySubscribed,
            HighLevelClientError::NotSubscribed => Self::NotSubscribed,
            HighLevelClientError::UnconfiguredConsumer | HighLevelClientError::TopicsNotFound(_) => {
                Self::InvalidArgument
            }
            HighLevelClientError::ProducerConfiguration(_)
            | HighLevelClientError::Producer(_)
            | HighLevelClientError::Consumer(_)
            | HighLevelClientError::SchedulerConfiguration(_) => Self::ConnectionFailed,
        }
    }
}

impl From<IoError> for FFIErrorCode {
    fn from(error: IoError) -> Self {
        match error.kind() {
            ErrorKind::NotFound
            | ErrorKind::PermissionDenied
            | ErrorKind::InvalidInput
            | ErrorKind::InvalidData => Self::InvalidArgument,
            ErrorKind::ConnectionRefused
            | ErrorKind::ConnectionReset
            | ErrorKind::ConnectionAborted
            | ErrorKind::NotConnected
            | ErrorKind::BrokenPipe
            | ErrorKind::TimedOut => Self::ConnectionFailed,
            ErrorKind::Interrupted => Self::Cancelled,
            _ => Self::Internal,
        }
    }
}

impl From<String> for FFIErrorCode {
    fn from(_: String) -> Self {
        Self::Internal
    }
}

impl From<&str> for FFIErrorCode {
    fn from(_: &str) -> Self {
        Self::Internal
    }
}

/// Handler error type for C# callbacks.
///
/// This error type is used internally by the FFI layer to classify errors
/// from C# handlers. It implements `ClassifyError` for prosody middleware
/// integration.
///
/// # Reference
///
/// - prosody-js: `JsHandlerError` in `src/handler.rs`
/// - prosody-rb: `RubyHandlerError` in `ext/prosody/src/handler/mod.rs`
#[derive(Debug, Error)]
pub enum CSharpHandlerError {
    /// A transient error occurred (should be retried).
    #[error("transient error: {message}")]
    Transient {
        /// Error message from C#.
        message: String,
    },

    /// A permanent error occurred (should not be retried).
    #[error("permanent error: {message}")]
    Permanent {
        /// Error message from C#.
        message: String,
    },

    /// The handler was cancelled (terminal, abort processing).
    #[error("cancelled")]
    Cancelled,
}

impl ClassifyError for CSharpHandlerError {
    fn classify_error(&self) -> ErrorCategory {
        match self {
            CSharpHandlerError::Transient { .. } => ErrorCategory::Transient,
            CSharpHandlerError::Permanent { .. } => ErrorCategory::Permanent,
            CSharpHandlerError::Cancelled => ErrorCategory::Terminal,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn error_code_ok_is_ok() {
        assert!(FFIErrorCode::Ok.is_ok());
        assert!(!FFIErrorCode::Ok.is_error());
    }

    #[test]
    fn error_code_error_is_error() {
        assert!(!FFIErrorCode::InvalidArgument.is_ok());
        assert!(FFIErrorCode::InvalidArgument.is_error());
    }

    #[test]
    fn error_code_default_is_ok() {
        assert_eq!(FFIErrorCode::default(), FFIErrorCode::Ok);
    }

    #[test]
    fn from_high_level_client_error_already_subscribed() {
        let error = HighLevelClientError::AlreadySubscribed;
        assert_eq!(FFIErrorCode::from(&error), FFIErrorCode::AlreadySubscribed);
    }

    #[test]
    fn from_high_level_client_error_not_subscribed() {
        let error = HighLevelClientError::NotSubscribed;
        assert_eq!(FFIErrorCode::from(&error), FFIErrorCode::NotSubscribed);
    }

    #[test]
    fn csharp_handler_error_transient_classifies_correctly() {
        let error = CSharpHandlerError::Transient {
            message: "retry me".to_owned(),
        };
        assert!(matches!(error.classify_error(), ErrorCategory::Transient));
    }

    #[test]
    fn csharp_handler_error_permanent_classifies_correctly() {
        let error = CSharpHandlerError::Permanent {
            message: "don't retry".to_owned(),
        };
        assert!(matches!(error.classify_error(), ErrorCategory::Permanent));
    }

    #[test]
    fn csharp_handler_error_cancelled_classifies_correctly() {
        let error = CSharpHandlerError::Cancelled;
        assert!(matches!(error.classify_error(), ErrorCategory::Terminal));
    }

    #[test]
    fn ffi_error_code_values() {
        assert_eq!(FFIErrorCode::Ok as i32, 0_i32);
        assert_eq!(FFIErrorCode::NullPassed as i32, 1_i32);
        assert_eq!(FFIErrorCode::Panic as i32, 2_i32);
        assert_eq!(FFIErrorCode::InvalidArgument as i32, 3_i32);
        assert_eq!(FFIErrorCode::ConnectionFailed as i32, 4_i32);
        assert_eq!(FFIErrorCode::Cancelled as i32, 5_i32);
        assert_eq!(FFIErrorCode::Internal as i32, 6_i32);
        assert_eq!(FFIErrorCode::InvalidContext as i32, 7_i32);
        assert_eq!(FFIErrorCode::AlreadySubscribed as i32, 8_i32);
        assert_eq!(FFIErrorCode::NotSubscribed as i32, 9_i32);
    }

    #[test]
    fn from_io_error_not_found_maps_to_invalid_argument() {
        let error = IoError::new(ErrorKind::NotFound, "not found");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::InvalidArgument);
    }

    #[test]
    fn from_io_error_permission_denied_maps_to_invalid_argument() {
        let error = IoError::new(ErrorKind::PermissionDenied, "denied");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::InvalidArgument);
    }

    #[test]
    fn from_io_error_connection_refused_maps_to_connection_failed() {
        let error = IoError::new(ErrorKind::ConnectionRefused, "refused");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::ConnectionFailed);
    }

    #[test]
    fn from_io_error_connection_reset_maps_to_connection_failed() {
        let error = IoError::new(ErrorKind::ConnectionReset, "reset");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::ConnectionFailed);
    }

    #[test]
    fn from_io_error_timed_out_maps_to_connection_failed() {
        let error = IoError::new(ErrorKind::TimedOut, "timeout");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::ConnectionFailed);
    }

    #[test]
    fn from_io_error_interrupted_maps_to_cancelled() {
        let error = IoError::new(ErrorKind::Interrupted, "interrupted");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::Cancelled);
    }

    #[test]
    fn from_io_error_other_maps_to_internal() {
        let error = IoError::other("other");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::Internal);
    }

    #[test]
    fn from_string_maps_to_internal() {
        let error = String::from("some error");
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::Internal);
    }

    #[test]
    fn from_str_maps_to_internal() {
        let error = "some error";
        assert_eq!(FFIErrorCode::from(error), FFIErrorCode::Internal);
    }
}
