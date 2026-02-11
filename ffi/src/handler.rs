//! Event handler trait for FFI callback interface.
//!
//! This module defines the [`EventHandler`] trait that serves as the FFI
//! boundary between Rust and C#. The trait enables Rust to invoke C#
//! callbacks when Kafka messages arrive or timers fire.
//!
//! This is an internal implementation detail. C# users implement the
//! higher-level `IEventHandler` interface, which includes `CancellationToken`
//! support and automatic distributed tracing propagation.

use std::collections::HashMap;
use std::sync::Arc;

use crate::context::Context;
use crate::error::FfiError;
use crate::message::Message;
use crate::timer::Timer;

/// Result code indicating how the event handler completed.
///
/// Handlers return this code to signal success or classify failures. Prosody
/// uses this classification to determine retry behavior and dead-letter queue
/// routing.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, uniffi::Enum)]
pub enum HandlerResultCode {
    /// Handler completed successfully.
    ///
    /// The message or timer was processed without error. Prosody will
    /// acknowledge the message and proceed to the next event.
    #[default]
    Success,

    /// Transient error that may resolve on retry.
    ///
    /// Use this for temporary failures like network timeouts, service
    /// unavailability, or rate limiting. Prosody will retry the event
    /// with exponential backoff.
    TransientError,

    /// Permanent error that will not resolve on retry.
    ///
    /// Use this for unrecoverable failures like malformed data, validation
    /// errors, or business logic violations. Prosody will route the event
    /// to the dead-letter queue without retrying.
    PermanentError,
}

/// Result returned by event handlers.
///
/// Combines a [`HandlerResultCode`] with an optional error message. The C#
/// wrapper populates `error_message` with the exception message when the
/// handler fails, enabling Rust-side logging and diagnostics.
#[derive(Debug, Clone, Default, uniffi::Record)]
pub struct HandlerResult {
    /// Indicates whether the handler succeeded or how it failed.
    pub code: HandlerResultCode,

    /// Error message describing the failure.
    ///
    /// This is `Some` when `code` is [`HandlerResultCode::TransientError`] or
    /// [`HandlerResultCode::PermanentError`], containing the exception message
    /// from the C# handler. It is `None` on success.
    pub error_message: Option<String>,
}

/// Callback trait for handling Kafka messages and timers.
///
/// This trait defines the FFI boundary that enables Rust to invoke C#
/// callbacks. An internal C# wrapper class implements this trait and bridges
/// to the user-facing `IEventHandler` interface. Users never implement this
/// trait directly.
///
/// Both methods receive a `carrier` map for distributed tracing context
/// propagation (e.g., W3C Trace Context headers). The C# wrapper extracts
/// these headers to continue the trace span across the FFI boundary.
#[uniffi::export(with_foreign)]
#[async_trait::async_trait]
pub trait EventHandler: Send + Sync {
    /// Handles an incoming Kafka message.
    ///
    /// Prosody calls this method when a message arrives from a subscribed
    /// topic. The handler should process the message and return a result
    /// indicating success or the type of failure.
    ///
    /// # Parameters
    ///
    /// * `context` - Provides access to Prosody operations like scheduling
    ///   timers, sending messages, and accessing entity state.
    /// * `message` - The Kafka message containing topic, key, value, and
    ///   headers.
    /// * `carrier` - Distributed tracing context headers for span propagation.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError`] if the FFI call itself fails (e.g., the C# runtime
    /// throws an unexpected exception). Handler-level errors should be reported
    /// via [`HandlerResult`] instead.
    async fn on_message(
        &self,
        context: Arc<Context>,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<HandlerResult, FfiError>;

    /// Handles a fired timer.
    ///
    /// Prosody calls this method when a previously scheduled timer fires.
    /// Timers enable delayed processing, periodic tasks, and timeout handling.
    ///
    /// # Parameters
    ///
    /// * `context` - Provides access to Prosody operations like scheduling new
    ///   timers, sending messages, and accessing entity state.
    /// * `timer` - The timer that fired, containing its ID and any associated
    ///   payload.
    /// * `carrier` - Distributed tracing context headers for span propagation.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError`] if the FFI call itself fails (e.g., the C# runtime
    /// throws an unexpected exception). Handler-level errors should be reported
    /// via [`HandlerResult`] instead.
    async fn on_timer(
        &self,
        context: Arc<Context>,
        timer: Arc<Timer>,
        carrier: HashMap<String, String>,
    ) -> Result<HandlerResult, FfiError>;
}
