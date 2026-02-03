//! Event types for async C# handler invocation.
//!
//! This module defines types matching the sibling wrapper pattern (prosody-py,
//! prosody-js, prosody-rb):
//! - `Context`: Wraps `BoxEventContext` for scheduling and cancellation
//! - `Message`: Wraps `ConsumerMessage` for Kafka message data
//! - `Timer`: Wraps `Trigger` for timer data
//! - `CancellationSignal`: Allows C# to signal cancellation to async operations
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
use std::sync::Arc;
use std::time::SystemTime;

use futures::TryStreamExt;
use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use tokio::sync::Notify;
use tracing::{Instrument, debug, info_span};
use tracing_opentelemetry::OpenTelemetrySpanExt;

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
    /// OpenTelemetry propagator for distributed tracing context propagation.
    propagator: Arc<TextMapCompositePropagator>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs non-exported methods"
)]
impl Context {
    /// Creates a new `Context` wrapping a [`BoxEventContext`].
    #[must_use]
    pub fn new(inner: BoxEventContext, propagator: Arc<TextMapCompositePropagator>) -> Self {
        Self { inner, propagator }
    }
}

#[uniffi::export(async_runtime = "tokio")]
impl Context {
    /// Returns true if cancellation has been requested.
    #[must_use]
    pub fn should_cancel(&self) -> bool {
        self.inner.should_cancel()
    }

    /// Async method that completes when cancellation is requested.
    pub async fn on_cancel(&self) {
        self.inner.on_cancel().await;
    }

    /// Schedule a new timer at the given time for the current message key.
    ///
    /// # Arguments
    ///
    /// * `time` - The time to schedule the timer
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::InvalidArgument` if the timestamp is invalid,
    /// or `ProsodyError::Internal` if the scheduling fails.
    pub async fn schedule(
        &self,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), ProsodyError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        let compact_time = CompactDateTime::try_from(time)
            .map_err(|e| ProsodyError::InvalidArgument(format!("{e:#}")))?;

        // Create span with extracted context as parent (matches C# ScheduleAsync)
        let span = info_span!("Schedule", time = %compact_time);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .schedule(compact_time, TimerType::Application)
            .instrument(span)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// Unschedule all existing timers, then schedule exactly one new timer.
    ///
    /// # Arguments
    ///
    /// * `time` - The time to schedule the timer
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::InvalidArgument` if the timestamp is invalid,
    /// or `ProsodyError::Internal` if the scheduling fails.
    pub async fn clear_and_schedule(
        &self,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), ProsodyError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        let compact_time = CompactDateTime::try_from(time)
            .map_err(|e| ProsodyError::InvalidArgument(format!("{e:#}")))?;

        // Create span with extracted context as parent (matches C# ClearAndScheduleAsync)
        let span = info_span!("ClearAndSchedule", time = %compact_time);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .clear_and_schedule(compact_time, TimerType::Application)
            .instrument(span)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// Unschedule a specific timer at the given time.
    ///
    /// # Arguments
    ///
    /// * `time` - The time of the timer to unschedule
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::InvalidArgument` if the timestamp is invalid,
    /// or `ProsodyError::Internal` if the operation fails.
    pub async fn unschedule(
        &self,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), ProsodyError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        let compact_time = CompactDateTime::try_from(time)
            .map_err(|e| ProsodyError::InvalidArgument(format!("{e:#}")))?;

        // Create span with extracted context as parent (matches C# UnscheduleAsync)
        let span = info_span!("Unschedule", time = %compact_time);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .unschedule(compact_time, TimerType::Application)
            .instrument(span)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// Unschedule all timers for the current key.
    ///
    /// # Arguments
    ///
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::Internal` if the operation fails.
    pub async fn clear_scheduled(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<(), ProsodyError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        // Create span with extracted context as parent (matches C# ClearScheduledAsync)
        let span = info_span!("ClearScheduled");
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .clear_scheduled(TimerType::Application)
            .instrument(span)
            .await
            .map_err(|_| ProsodyError::Internal)
    }

    /// List all scheduled timer times for the current key.
    ///
    /// # Arguments
    ///
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Returns
    ///
    /// A list of scheduled times.
    ///
    /// # Errors
    ///
    /// Returns `ProsodyError::Internal` if the operation fails.
    pub async fn scheduled(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<SystemTime>, ProsodyError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        // Create span with extracted context as parent (matches C# ScheduledAsync)
        let span = info_span!("Scheduled");
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .scheduled(TimerType::Application)
            .map_ok(Into::<SystemTime>::into)
            .try_collect()
            .instrument(span)
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
    payload: Vec<u8>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs non-exported methods"
)]
impl Message {
    /// Creates a new Message wrapping a [`ConsumerMessage`].
    ///
    /// # Errors
    ///
    /// Returns an error if the payload cannot be serialized to JSON bytes.
    pub fn new(inner: ConsumerMessage) -> Result<Self, simd_json::Error> {
        let topic = inner.topic().to_string();
        let key = inner.key().to_string();
        // Serialize JSON Value back to UTF-8 bytes using simd_json
        let payload = simd_json::to_vec(inner.payload())?;
        Ok(Self {
            inner,
            topic,
            key,
            payload,
        })
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

    /// Returns the message timestamp.
    #[must_use]
    pub fn timestamp(&self) -> SystemTime {
        (*self.inner.timestamp()).into()
    }

    /// Returns the message key.
    #[must_use]
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// Returns the message payload (UTF-8 JSON bytes).
    #[must_use]
    pub fn payload(&self) -> Vec<u8> {
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

    /// Returns the timer fire time.
    #[must_use]
    pub fn time(&self) -> SystemTime {
        self.trigger.time.into()
    }
}

// ============================================================================
// CancellationSignal - Allows C# to signal cancellation to async operations
// ============================================================================

/// A cancellation signal that can be created by C# and passed to Rust async
/// operations.
///
/// C# creates this object, passes it to an async method, and can call
/// `cancel()` to signal that the operation should be aborted. Rust code awaits
/// `cancelled()` to detect when cancellation has been requested.
#[derive(uniffi::Object)]
pub struct CancellationSignal {
    notify: Arc<Notify>,
}

impl Default for CancellationSignal {
    fn default() -> Self {
        Self::new()
    }
}

#[uniffi::export(async_runtime = "tokio")]
impl CancellationSignal {
    /// Creates a new cancellation signal.
    #[uniffi::constructor]
    #[must_use]
    pub fn new() -> Self {
        Self {
            notify: Arc::new(Notify::new()),
        }
    }

    /// Signals cancellation. Any async operation waiting on this signal will be
    /// notified.
    pub fn cancel(&self) {
        self.notify.notify_waiters();
    }

    /// Waits until cancellation is signaled.
    ///
    /// This is used internally by Rust async operations to detect cancellation.
    pub async fn cancelled(&self) {
        self.notify.notified().await;
    }
}

// ============================================================================
// Carrier type alias
// ============================================================================

/// OpenTelemetry carrier for context propagation.
///
/// In C#, this becomes `IDictionary<string, string>`.
pub type Carrier = HashMap<String, String>;
