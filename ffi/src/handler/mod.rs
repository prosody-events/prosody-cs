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
use std::time::Duration;

use crate::error::FfiError;
use crate::types::EventMetadata;

mod bridge;

pub(crate) use bridge::CsHandler;

/// Result returned by event handlers.
#[boltffi::data]
#[derive(Debug, Clone)]
pub enum HandlerResult {
    /// The handler returned encoded JSON.
    Success {
        /// Encoded JSON response.
        response: Vec<u8>,
    },
    /// The handler returned a transient error.
    TransientError {
        /// Error text.
        message: String,
    },
    /// The handler returned a permanent error.
    PermanentError {
        /// Error text.
        message: String,
    },
}

/// Values needed to send one request.
#[boltffi::data]
#[derive(Debug, Clone)]
pub struct NativeRequest {
    /// Kafka topic.
    pub topic: String,
    /// Kafka key.
    pub key: String,
    /// Encoded JSON payload.
    pub payload: Vec<u8>,
    /// Event metadata extracted by the host.
    pub metadata: EventMetadata,
    /// Requested subsystem names.
    pub subsystems: Vec<String>,
    /// Request timeout.
    pub timeout: Duration,
    /// Trace propagation fields.
    pub carrier: HashMap<String, String>,
}

/// Values needed to send one excise subsystem request.
#[boltffi::data]
#[derive(Debug, Clone)]
pub struct NativeExciseRequest {
    /// Kafka topic.
    pub topic: String,
    /// Kafka key.
    pub key: String,
    /// Requested subsystem names.
    pub subsystems: Vec<String>,
    /// Request timeout.
    pub timeout: Duration,
    /// Trace propagation fields.
    pub carrier: HashMap<String, String>,
}

/// One subsystem outcome returned by a request.
#[boltffi::data]
#[derive(Debug, Clone)]
pub enum NativeRequestResult {
    /// The handler returned encoded JSON.
    Ok {
        /// Encoded JSON response.
        value: Vec<u8>,
    },
    /// The handler returned an error.
    HandlerError {
        /// Handler error text.
        message: String,
    },
    /// No response arrived before the deadline.
    Timeout {
        /// Rust error display text.
        message: String,
    },
    /// The responder used another response format.
    FormatMismatch {
        /// Rust error display text.
        message: String,
    },
    /// The response payload did not decode.
    Malformed {
        /// Rust error display text.
        message: String,
    },
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
#[async_trait::async_trait]
#[boltffi::export]
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
        event_id: u64,
        carrier: HashMap<String, String>,
    ) -> Result<HandlerResult, FfiError>;

    /// Handles an excise record.
    async fn on_excise(
        &self,
        event_id: u64,
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
        event_id: u64,
        carrier: HashMap<String, String>,
    ) -> Result<HandlerResult, FfiError>;
}
