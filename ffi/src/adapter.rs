//! Adapts C# event handler calls to Prosody handlers.
//!
//! This module owns handler IDs, trace injection, and cancellation forwarding.

use std::collections::HashMap;
use std::future::Future;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use tracing::Instrument;
use tracing_opentelemetry::OpenTelemetrySpanExt;

use crate::context::Context;
use crate::error::{CsHandlerError, FfiError};
use crate::handler::{EventHandler, HandlerResult, HandlerResultCode};
use crate::message::Message;
use crate::timer::Timer;
use prosody::codec::BinaryPayload;
use prosody::consumer::DemandType;
use prosody::consumer::event_context::EventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::consumer::middleware::FallibleHandler;
use prosody::timers::{TimerType, Trigger};

static NEXT_HANDLER_ID: AtomicU64 = AtomicU64::new(1);

/// Adapts a C# [`EventHandler`] to Prosody's [`FallibleHandler`] trait.
pub(crate) struct CsHandler {
    handler: Arc<dyn EventHandler>,
    propagator: Arc<TextMapCompositePropagator>,
}

impl Clone for CsHandler {
    fn clone(&self) -> Self {
        Self {
            handler: Arc::clone(&self.handler),
            propagator: Arc::clone(&self.propagator),
        }
    }
}

impl CsHandler {
    pub(crate) fn new(
        handler: Arc<dyn EventHandler>,
        propagator: Arc<TextMapCompositePropagator>,
    ) -> Self {
        Self {
            handler,
            propagator,
        }
    }

    /// Awaits a C# handler call and forwards cancellation to it.
    ///
    /// The C# call completes before this function returns. This guarantee keeps
    /// all FFI arguments alive until C# releases them.
    ///
    /// # Errors
    ///
    /// Returns [`CsHandlerError`] when the FFI call fails or the handler fails.
    async fn invoke<C, F>(
        &self,
        context: C,
        handler_id: u64,
        handler_call: F,
    ) -> Result<(), CsHandlerError>
    where
        C: EventContext<Payload = BinaryPayload>,
        F: Future<Output = Result<HandlerResult, FfiError>>,
    {
        tokio::pin!(handler_call);

        let result = tokio::select! {
            // The C# shim runs the handler on the C# thread pool. The bridge
            // reads should_cancel after it rents the slot to cover this race.
            biased;
            result = &mut handler_call => result?,
            () = context.on_cancel() => {
                self.handler.cancel(handler_id);
                handler_call.await?
            }
        };

        map_handler_result(result)
    }
}

impl FallibleHandler for CsHandler {
    type Error = CsHandlerError;
    type Output = ();
    type Payload = BinaryPayload;

    async fn on_message<C>(
        &self,
        context: C,
        message: ConsumerMessage<Self::Payload>,
        _demand_type: DemandType,
    ) -> Result<Self::Output, Self::Error>
    where
        C: EventContext<Payload = Self::Payload>,
    {
        let handler_id = next_handler_id()?;
        let span = message.span();
        let mut carrier = HashMap::with_capacity(2);
        self.propagator
            .inject_context(&span.context(), &mut carrier);

        let cancellation_context = context.clone();
        let context = Arc::new(Context::new(context.boxed(), Arc::clone(&self.propagator)));
        let message = Arc::new(Message::new(message));
        let handler_call = self
            .handler
            .on_message(context, message, carrier, handler_id)
            .instrument(span);

        self.invoke(cancellation_context, handler_id, handler_call)
            .await
    }

    async fn on_timer<C>(
        &self,
        context: C,
        trigger: Trigger,
        _demand_type: DemandType,
    ) -> Result<Self::Output, Self::Error>
    where
        C: EventContext<Payload = Self::Payload>,
    {
        if trigger.timer_type != TimerType::Application {
            return Ok(());
        }

        let handler_id = next_handler_id()?;
        let span = trigger.span();
        let mut carrier = HashMap::with_capacity(2);
        self.propagator
            .inject_context(&span.context(), &mut carrier);

        let cancellation_context = context.clone();
        let context = Arc::new(Context::new(context.boxed(), Arc::clone(&self.propagator)));
        let timer = Arc::new(Timer::new(trigger));
        let handler_call = self
            .handler
            .on_timer(context, timer, carrier, handler_id)
            .instrument(span);

        self.invoke(cancellation_context, handler_id, handler_call)
            .await
    }

    /// `ProsodyClient` owns the C# handler lifetime.
    async fn shutdown(self) {}
}

fn next_handler_id() -> Result<u64, CsHandlerError> {
    NEXT_HANDLER_ID
        .fetch_update(Ordering::Relaxed, Ordering::Relaxed, |id| id.checked_add(1))
        .map_err(|_| CsHandlerError::Transient("handler ID space is exhausted".to_owned()))
}

fn map_handler_result(result: HandlerResult) -> Result<(), CsHandlerError> {
    let error_message = result.error_message.unwrap_or_default();

    match result.code {
        HandlerResultCode::Success => Ok(()),
        HandlerResultCode::TransientError => Err(CsHandlerError::Transient(error_message)),
        HandlerResultCode::PermanentError => Err(CsHandlerError::Permanent(error_message)),
    }
}
