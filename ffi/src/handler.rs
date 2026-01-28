//! Event handler trait for C# to implement.
//!
//! This module defines the `EventHandler` trait that C# code implements
//! to handle messages and timer events from Prosody.
//!
//! # Design
//!
//! `UniFFI`'s `[Trait, WithForeign]` pattern allows C# to implement Rust traits.
//! When a message arrives:
//! 1. Rust creates a `MessageEvent` containing the message data
//! 2. Rust calls `handler.on_message(event)` via FFI
//! 3. C# receives ownership of the event (`UniFFI` handles Arc management)
//! 4. C# processes asynchronously and returns a `HandlerResultCode`
//! 5. Rust maps the result to success/retry/DLQ behavior

use crate::error::ProsodyError;
use crate::events::{MessageEvent, TimerEvent};
use std::sync::Arc;

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

/// Event handler trait implemented by C#.
///
/// C# implements this trait to handle messages and timer events.
/// The trait is marked with `[Trait, WithForeign]` in the UDL file,
/// allowing C# to provide implementations that Rust can call.
///
/// # Async
///
/// All handler methods are async, allowing C# to process events
/// asynchronously without blocking the Rust runtime.
///
/// # Ownership
///
/// Events are passed by `Arc<T>`, giving C# ownership. `UniFFI` handles
/// the Arc reference counting automatically - C# gets an `IDisposable`
/// object that properly releases the Arc when disposed.
#[uniffi::export(with_foreign)]
#[async_trait::async_trait]
pub trait EventHandler: Send + Sync {
    /// Called when a Kafka message arrives.
    ///
    /// # Arguments
    ///
    /// * `event` - The message event containing message data and context
    ///
    /// # Returns
    ///
    /// A `HandlerResultCode` indicating how Prosody should handle the message:
    /// - `Success`: Commit the offset
    /// - `TransientError`: Retry with backoff
    /// - `PermanentError`: Send to dead letter queue
    /// - `Cancelled`: Abort processing
    async fn on_message(&self, event: Arc<MessageEvent>) -> Result<HandlerResultCode, ProsodyError>;

    /// Called when a timer fires.
    ///
    /// # Arguments
    ///
    /// * `event` - The timer event containing trigger data and context
    ///
    /// # Returns
    ///
    /// A `HandlerResultCode` indicating how Prosody should handle the timer.
    async fn on_timer(&self, event: Arc<TimerEvent>) -> Result<HandlerResultCode, ProsodyError>;

    /// Called when the handler is shutting down.
    ///
    /// This is a synchronous notification that allows C# to perform cleanup.
    fn on_shutdown(&self);
}
