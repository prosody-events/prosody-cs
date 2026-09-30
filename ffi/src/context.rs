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

use opentelemetry::propagation::TextMapCompositePropagator;
use tracing::{Instrument, info_span};

use prosody::codec::BinaryPayload;
use prosody::consumer::DemandType as CoreDemandType;
use prosody::consumer::event_context::BoxEventContext;
use prosody::timers::TimerType;
use prosody::timers::datetime::CompactDateTime;

use crate::error::FfiError;
use crate::json_deque::JsonDequeStateHandle;
use crate::map::{JsonMapStateHandle, MessageMapStateHandle};
use crate::message_deque::MessageDequeStateHandle;
use crate::runtime::run;
use crate::set::SetStateHandle;
use crate::state::with_parent;
use crate::value::{JsonValueStateHandle, MessageValueStateHandle};

/// The demand that one handler call serves.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum DemandType {
    /// The first attempt at an event.
    Normal,
    /// An attempt after one or more failures.
    Failure {
        /// The retry ordinal. It is 1 on the first retry. It is an estimate.
        retry: u32,
    },
}

impl From<CoreDemandType> for DemandType {
    fn from(demand: CoreDemandType) -> Self {
        match demand {
            CoreDemandType::Normal => Self::Normal,
            CoreDemandType::Failure { retry } => Self::Failure { retry },
        }
    }
}

/// Event context passed to message handlers during event processing.
///
/// This type wraps Prosody's [`BoxEventContext`]. Timer operations apply to
/// the current message key and take an OpenTelemetry carrier for the parent
/// span.
///
/// The `*_state` methods vend state handles. Vending checks the registration
/// in core and opens no span, because each handle operation opens its own.
#[derive(uniffi::Object)]
pub struct Context {
    inner: BoxEventContext<BinaryPayload>,
    propagator: Arc<TextMapCompositePropagator>,
    demand: DemandType,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
)]
impl Context {
    /// Creates a context for one handler call with the demand it serves.
    #[must_use]
    pub fn new(
        inner: BoxEventContext<BinaryPayload>,
        propagator: Arc<TextMapCompositePropagator>,
        demand: CoreDemandType,
    ) -> Self {
        Self {
            inner,
            propagator,
            demand: demand.into(),
        }
    }
}

#[uniffi::export]
impl Context {
    /// Returns true when the handler should stop.
    #[must_use]
    pub fn should_cancel(&self) -> bool {
        self.inner.should_cancel()
    }

    /// Returns the demand that this handler call serves.
    #[must_use]
    pub fn demand(&self) -> DemandType {
        self.demand
    }

    /// Completes when cancellation is requested.
    pub async fn on_cancel(self: Arc<Self>) {
        run(async move { self.inner.on_cancel().await }).await;
    }

    /// Schedules a timer for the current key.
    ///
    /// # Errors
    ///
    /// Returns an error if the time is not valid or the store operation fails.
    pub async fn schedule(
        self: Arc<Self>,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            let compact_time = CompactDateTime::try_from(time)?;
            let span = info_span!("Schedule", time = %compact_time);
            let span = with_parent(span, &self.propagator, &carrier);

            self.inner
                .schedule(compact_time, TimerType::Application)
                .instrument(span)
                .await?;

            Ok(())
        })
        .await
    }

    /// Replaces all timers for the current key with one timer.
    ///
    /// # Errors
    ///
    /// Returns an error if the time is not valid or the store operation fails.
    pub async fn clear_and_schedule(
        self: Arc<Self>,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            let compact_time = CompactDateTime::try_from(time)?;
            let span = info_span!("ClearAndSchedule", time = %compact_time);
            let span = with_parent(span, &self.propagator, &carrier);

            self.inner
                .clear_and_schedule(compact_time, TimerType::Application)
                .instrument(span)
                .await?;

            Ok(())
        })
        .await
    }

    /// Cancels the timer at the given time for the current key.
    ///
    /// # Errors
    ///
    /// Returns an error if the time is not valid or the store operation fails.
    pub async fn unschedule(
        self: Arc<Self>,
        time: SystemTime,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            let compact_time = CompactDateTime::try_from(time)?;
            let span = info_span!("Unschedule", time = %compact_time);
            let span = with_parent(span, &self.propagator, &carrier);

            self.inner
                .unschedule(compact_time, TimerType::Application)
                .instrument(span)
                .await?;

            Ok(())
        })
        .await
    }

    /// Cancels all timers for the current key.
    ///
    /// # Errors
    ///
    /// Returns an error if the store operation fails.
    pub async fn clear_scheduled(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            let span = with_parent(info_span!("ClearScheduled"), &self.propagator, &carrier);

            self.inner
                .clear_scheduled(TimerType::Application)
                .instrument(span)
                .await?;

            Ok(())
        })
        .await
    }

    /// Returns the scheduled timer times for the current key, in no order.
    ///
    /// # Errors
    ///
    /// Returns an error if the store operation fails.
    pub async fn scheduled(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<SystemTime>, FfiError> {
        run(async move {
            let span = with_parent(info_span!("Scheduled"), &self.propagator, &carrier);

            Ok(self
                .inner
                .scheduled(TimerType::Application)
                .instrument(span)
                .await?
                .into_iter()
                .map(Into::<SystemTime>::into)
                .collect())
        })
        .await
    }

    /// Vends the state handle for the named JSON value collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error if no collection has this name and kind.
    pub fn value_state(&self, name: &str) -> Result<Arc<JsonValueStateHandle>, FfiError> {
        let handle = self.inner.value_state(name)?;
        Ok(Arc::new(JsonValueStateHandle {
            state: handle,
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Vends the state handle for the named JSON map collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error if no collection has this name and kind.
    pub fn map_state(&self, name: &str) -> Result<Arc<JsonMapStateHandle>, FfiError> {
        let handle = self.inner.map_state(name)?;
        Ok(Arc::new(JsonMapStateHandle {
            state: handle,
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Vends the state handle for the named set collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error if no collection has this name and kind.
    pub fn set_state(&self, name: &str) -> Result<Arc<SetStateHandle>, FfiError> {
        let handle = self.inner.set_state(name)?;
        Ok(Arc::new(SetStateHandle {
            state: handle,
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Vends the state handle for the named JSON deque collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error if no collection has this name and kind.
    pub fn deque_state(&self, name: &str) -> Result<Arc<JsonDequeStateHandle>, FfiError> {
        let handle = self.inner.deque_state(name)?;
        Ok(Arc::new(JsonDequeStateHandle {
            state: handle,
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Vends the state handle for the named Kafka-message value collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error if no collection has this name and kind.
    pub fn message_value_state(
        &self,
        name: &str,
    ) -> Result<Arc<MessageValueStateHandle>, FfiError> {
        let handle = self.inner.message_value_state(name)?;
        Ok(Arc::new(MessageValueStateHandle {
            state: handle,
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Vends the state handle for the named Kafka-message map collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error if no collection has this name and kind.
    pub fn message_map_state(&self, name: &str) -> Result<Arc<MessageMapStateHandle>, FfiError> {
        let handle = self.inner.message_map_state(name)?;
        Ok(Arc::new(MessageMapStateHandle {
            state: handle,
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Vends the state handle for the named Kafka-message deque collection.
    ///
    /// # Errors
    ///
    /// Returns a permanent state error if no collection has this name and kind.
    pub fn message_deque_state(
        &self,
        name: &str,
    ) -> Result<Arc<MessageDequeStateHandle>, FfiError> {
        let handle = self.inner.message_deque_state(name)?;
        Ok(Arc::new(MessageDequeStateHandle {
            state: handle,
            propagator: Arc::clone(&self.propagator),
        }))
    }
}
