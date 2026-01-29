//! Event types for async C# handler invocation.
//!
//! This module defines types matching the sibling wrapper pattern (prosody-py,
//! prosody-js, prosody-rb):
//! - `Context`: Wraps `BoxEventContext` for scheduling and cancellation
//! - `Message`: Wraps `ConsumerMessage` for Kafka message data
//! - `Timer`: Wraps `Trigger` for timer data
//!
//! # Design
//!
//! Following the Python high-level handler pattern:
//! ```python
//! async def on_message(self, context: Context, message: Message) -> None
//! ```
//!
//! Each type is a `UniFFI` Object that wraps the underlying prosody type and
//! exposes data via methods.

use std::collections::HashMap;

use futures::TryStreamExt;
use prosody::consumer::Keyed;
use prosody::consumer::event_context::BoxEventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::timers::datetime::CompactDateTime;
use prosody::timers::{TimerType, Trigger};

use crate::error::ProsodyError;

// ============================================================================
// Context - Wraps BoxEventContext
// ============================================================================

/// Event context for scheduling timers and checking cancellation.
///
/// Wraps prosody's `BoxEventContext` and exposes scheduling/cancellation
/// methods. This matches the Python `Context` class.
#[derive(uniffi::Object)]
pub struct Context {
    inner: BoxEventContext,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs non-exported methods"
)]
impl Context {
    /// Creates a new `Context` wrapping a [`BoxEventContext`].
    #[must_use]
    pub fn new(inner: BoxEventContext) -> Self {
        Self { inner }
    }
}

#[uniffi::export]
impl Context {
    /// Returns true if cancellation has been requested.
    #[must_use]
    pub fn should_cancel(&self) -> bool {
        self.inner.should_cancel()
    }

    /// Async method that completes when cancellation is requested.
    pub async fn await_cancel(&self) {
        self.inner.on_cancel().await;
    }

    /// Schedule a new timer at the given time for the current message key.
    ///
    /// # Arguments
    ///
    /// * `time_ms` - Unix timestamp in milliseconds
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::InvalidArgument` if the timestamp is invalid,
    /// or `ProsodyError::Internal` if the scheduling fails.
    pub async fn schedule(&self, time_ms: i64) -> Result<(), ProsodyError> {
        let epoch_secs =
            u32::try_from(time_ms / 1000).map_err(|_| ProsodyError::InvalidArgument)?;
        let time = CompactDateTime::from(epoch_secs);
        self.inner
            .schedule(time, TimerType::Application)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// Unschedule all existing timers, then schedule exactly one new timer.
    ///
    /// # Arguments
    ///
    /// * `time_ms` - Unix timestamp in milliseconds
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::InvalidArgument` if the timestamp is invalid,
    /// or `ProsodyError::Internal` if the scheduling fails.
    pub async fn clear_and_schedule(&self, time_ms: i64) -> Result<(), ProsodyError> {
        let epoch_secs =
            u32::try_from(time_ms / 1000).map_err(|_| ProsodyError::InvalidArgument)?;
        let time = CompactDateTime::from(epoch_secs);
        self.inner
            .clear_and_schedule(time, TimerType::Application)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// Unschedule a specific timer at the given time.
    ///
    /// # Arguments
    ///
    /// * `time_ms` - Unix timestamp in milliseconds
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::InvalidArgument` if the timestamp is invalid,
    /// or `ProsodyError::Internal` if the operation fails.
    pub async fn unschedule(&self, time_ms: i64) -> Result<(), ProsodyError> {
        let epoch_secs =
            u32::try_from(time_ms / 1000).map_err(|_| ProsodyError::InvalidArgument)?;
        let time = CompactDateTime::from(epoch_secs);
        self.inner
            .unschedule(time, TimerType::Application)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// Unschedule all timers for the current key.
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::Internal` if the operation fails.
    pub async fn clear_scheduled(&self) -> Result<(), ProsodyError> {
        self.inner
            .clear_scheduled(TimerType::Application)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// List all scheduled timer times for the current key.
    ///
    /// # Returns
    ///
    /// A list of Unix timestamps in milliseconds.
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::Internal` if the operation fails.
    pub async fn scheduled(&self) -> Result<Vec<i64>, ProsodyError> {
        self.inner
            .scheduled(TimerType::Application)
            .map_ok(|time| i64::from(time.epoch_seconds()) * 1000)
            .try_collect()
            .await
            .map_err(|_| ProsodyError::Internal)
    }
}

// ============================================================================
// Message - Wraps ConsumerMessage
// ============================================================================

/// Kafka message data.
///
/// Wraps prosody's `ConsumerMessage` and exposes message data via methods.
/// This matches the Python `Message` dataclass.
#[derive(uniffi::Object)]
pub struct Message {
    inner: ConsumerMessage,
    topic: String,
    key: String,
    payload: String,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs non-exported methods"
)]
impl Message {
    /// Creates a new Message wrapping a [`ConsumerMessage`].
    #[must_use]
    pub fn new(inner: ConsumerMessage) -> Self {
        let topic = inner.topic().to_string();
        let key = inner.key().to_string();
        let payload = inner.payload().to_string();
        Self {
            inner,
            topic,
            key,
            payload,
        }
    }
}

#[uniffi::export]
impl Message {
    /// Returns the topic name.
    #[must_use]
    pub fn topic(&self) -> String {
        self.topic.clone()
    }

    /// Returns the partition number.
    #[must_use]
    pub fn partition(&self) -> i32 {
        self.inner.partition()
    }

    /// Returns the message offset.
    #[must_use]
    pub fn offset(&self) -> i64 {
        self.inner.offset()
    }

    /// Returns the message timestamp in milliseconds since epoch.
    #[must_use]
    pub fn timestamp(&self) -> i64 {
        self.inner.timestamp().timestamp_millis()
    }

    /// Returns the message key.
    #[must_use]
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// Returns the message payload (JSON string).
    #[must_use]
    pub fn payload(&self) -> String {
        self.payload.clone()
    }
}

// ============================================================================
// Timer - Wraps Trigger
// ============================================================================

/// Timer trigger data.
///
/// Wraps prosody's `Trigger` and exposes timer data via methods.
/// This matches the Python `Timer` dataclass.
#[derive(uniffi::Object)]
pub struct Timer {
    trigger: Trigger,
    key: String,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs non-exported methods"
)]
impl Timer {
    /// Creates a new Timer wrapping a Trigger.
    #[must_use]
    pub fn new(trigger: Trigger) -> Self {
        let key = trigger.key.to_string();
        Self { trigger, key }
    }
}

#[uniffi::export]
impl Timer {
    /// Returns the timer key.
    #[must_use]
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// Returns the timer fire time in milliseconds since epoch.
    #[must_use]
    pub fn time(&self) -> i64 {
        i64::from(self.trigger.time.epoch_seconds()) * 1000
    }
}

// ============================================================================
// Carrier type alias
// ============================================================================

/// OpenTelemetry carrier for context propagation.
///
/// In C#, this becomes `IDictionary<string, string>`.
pub type Carrier = HashMap<String, String>;
