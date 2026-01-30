//! Event handler trait for FFI callback interface.
//!
//! This module defines the `EventHandler` trait that serves as the FFI
//! boundary between Rust and C#. This is an internal implementation detail -
//! C# users implement the higher-level `IEventHandler` interface which includes
//! `CancellationToken` support.

use std::collections::HashMap;
use std::sync::Arc;

use crate::error::ProsodyError;
use crate::events::{Context, Message, Timer};

/// Result code returned by event handlers.
///
/// This enum allows handlers to signal success, errors, or cancellation
/// back to Prosody for proper error classification.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, uniffi::Enum)]
pub enum HandlerResultCode {
    /// Handler completed successfully.
    #[default]
    Success,
    /// Transient error - should retry.
    TransientError,
    /// Permanent error - should not retry (send to DLQ).
    PermanentError,
    /// Handler was cancelled.
    Cancelled,
}

/// Event handler trait for FFI boundary.
///
/// This trait is implemented by an internal C# wrapper class that bridges
/// to the user-facing `IEventHandler` interface. Users never implement this
/// trait directly.
#[uniffi::export(with_foreign)]
#[async_trait::async_trait]
pub trait EventHandler: Send + Sync {
    /// Called when a Kafka message arrives.
    async fn on_message(
        &self,
        context: Arc<Context>,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<HandlerResultCode, ProsodyError>;

    /// Called when a timer fires.
    async fn on_timer(
        &self,
        context: Arc<Context>,
        timer: Arc<Timer>,
        carrier: HashMap<String, String>,
    ) -> Result<HandlerResultCode, ProsodyError>;
}
