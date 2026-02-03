//! Event context for scheduling timers and checking cancellation.

use std::collections::HashMap;
use std::sync::Arc;
use std::time::SystemTime;

use futures::TryStreamExt;
use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use tracing::{Instrument, debug, info_span};
use tracing_opentelemetry::OpenTelemetrySpanExt;

use prosody::consumer::event_context::BoxEventContext;
use prosody::timers::TimerType;
use prosody::timers::datetime::CompactDateTime;

use crate::error::FfiError;

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
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
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
    /// Returns `FfiError::InvalidArgument` if the timestamp is invalid,
    /// or `FfiError::Internal` if the scheduling fails.
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

    /// Unschedule all existing timers, then schedule exactly one new timer.
    ///
    /// # Arguments
    ///
    /// * `time` - The time to schedule the timer
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Errors
    ///
    /// Returns `FfiError::InvalidArgument` if the timestamp is invalid,
    /// or `FfiError::Internal` if the scheduling fails.
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

    /// Unschedule a specific timer at the given time.
    ///
    /// # Arguments
    ///
    /// * `time` - The time of the timer to unschedule
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Errors
    ///
    /// Returns `FfiError::InvalidArgument` if the timestamp is invalid,
    /// or `FfiError::Internal` if the operation fails.
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

    /// Unschedule all timers for the current key.
    ///
    /// # Arguments
    ///
    /// * `carrier` - OpenTelemetry carrier for context propagation
    ///
    /// # Errors
    ///
    /// Returns `FfiError::Internal` if the operation fails.
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
    /// Returns `FfiError::Internal` if the operation fails.
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
            .map_ok(Into::<SystemTime>::into)
            .try_collect()
            .instrument(span)
            .await?)
    }
}
