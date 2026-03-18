//! Event context for Prosody message handlers.
//!
//! This module provides the [`Context`] type, which is passed to message
//! handlers during event processing. It enables handlers to:
//!
//! - Schedule, reschedule, and cancel timers for the current message key
//! - Check for and respond to cancellation requests
//! - Propagate OpenTelemetry tracing context across service boundaries

use std::collections::HashMap;
use std::sync::Arc;
use std::time::SystemTime;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use tracing::{Instrument, debug, info_span};
use tracing_opentelemetry::OpenTelemetrySpanExt;

use prosody::consumer::event_context::BoxEventContext;
use prosody::timers::TimerType;
use prosody::timers::datetime::CompactDateTime;

use crate::error::FfiError;

/// Event context passed to message handlers during event processing.
///
/// This type wraps Prosody's [`BoxEventContext`] and provides FFI-safe methods
/// for timer management and cancellation handling. All timer operations are
/// scoped to the current message key.
///
/// Timer operations accept an OpenTelemetry carrier for distributed tracing
/// context propagation, allowing traces to span across service boundaries.
#[derive(uniffi::Object)]
pub struct Context {
    inner: BoxEventContext,
    propagator: Arc<TextMapCompositePropagator>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
)]
impl Context {
    /// Creates a new context wrapping the given event context and propagator.
    #[must_use]
    pub fn new(inner: BoxEventContext, propagator: Arc<TextMapCompositePropagator>) -> Self {
        Self { inner, propagator }
    }
}

#[uniffi::export(async_runtime = "tokio")]
impl Context {
    /// Checks whether the handler should stop processing.
    ///
    /// Handlers should periodically check this flag during long-running
    /// operations and exit gracefully when it returns `true`. This enables
    /// cooperative cancellation during consumer shutdown or rebalancing.
    #[must_use]
    pub fn should_cancel(&self) -> bool {
        self.inner.should_cancel()
    }

    /// Waits until cancellation is requested.
    ///
    /// Use this in a `select!` or similar construct to respond to cancellation
    /// while awaiting other operations. Completes immediately if cancellation
    /// has already been requested.
    pub async fn on_cancel(&self) {
        self.inner.on_cancel().await;
    }

    /// Schedules a new timer to fire at the specified time.
    ///
    /// The timer is associated with the current message key. When the timer
    /// fires, the handler will be invoked with a timer event for that key.
    ///
    /// Multiple timers can be scheduled for the same key at different times.
    ///
    /// # Errors
    ///
    /// Returns an error if `time` cannot be converted to a valid timestamp
    /// or if the scheduling operation fails.
    pub async fn schedule(
        &self,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        let compact_time = CompactDateTime::try_from(time)?;

        // Create span with extracted context as parent (matches C# ScheduleAsync)
        let span = info_span!("Schedule", time = %compact_time);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .schedule(compact_time, TimerType::Application)
            .instrument(span)
            .await?;

        Ok(())
    }

    /// Clears all timers for the current key, then schedules a new one.
    ///
    /// This is an atomic operation that ensures exactly one timer exists for
    /// the key after completion. Useful for "snooze" or "reschedule" patterns
    /// where previous timers should be replaced.
    ///
    /// # Errors
    ///
    /// Returns an error if `time` cannot be converted to a valid timestamp
    /// or if the operation fails.
    pub async fn clear_and_schedule(
        &self,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        let compact_time = CompactDateTime::try_from(time)?;

        // Create span with extracted context as parent (matches C#
        // ClearAndScheduleAsync)
        let span = info_span!("ClearAndSchedule", time = %compact_time);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .clear_and_schedule(compact_time, TimerType::Application)
            .instrument(span)
            .await?;

        Ok(())
    }

    /// Cancels a timer scheduled for the specified time.
    ///
    /// If no timer exists at the given time for the current key, this is a
    /// no-op.
    ///
    /// # Errors
    ///
    /// Returns an error if `time` cannot be converted to a valid timestamp
    /// or if the operation fails.
    pub async fn unschedule(
        &self,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        let compact_time = CompactDateTime::try_from(time)?;

        // Create span with extracted context as parent (matches C# UnscheduleAsync)
        let span = info_span!("Unschedule", time = %compact_time);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        self.inner
            .unschedule(compact_time, TimerType::Application)
            .instrument(span)
            .await?;

        Ok(())
    }

    /// Cancels all timers for the current key.
    ///
    /// After this call, no timers will be scheduled for the key until new ones
    /// are explicitly scheduled.
    ///
    /// # Errors
    ///
    /// Returns an error if the operation fails.
    pub async fn clear_scheduled(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
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
            .await?;

        Ok(())
    }

    /// Returns all scheduled timer times for the current key.
    ///
    /// The returned times are not guaranteed to be in any particular order.
    ///
    /// # Errors
    ///
    /// Returns an error if the operation fails.
    pub async fn scheduled(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<SystemTime>, FfiError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.propagator.extract(&carrier);

        // Create span with extracted context as parent (matches C# ScheduledAsync)
        let span = info_span!("Scheduled");
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        Ok(self
            .inner
            .scheduled(TimerType::Application)
            .instrument(span)
            .await?
            .into_iter()
            .map(Into::<SystemTime>::into)
            .collect())
    }
}
