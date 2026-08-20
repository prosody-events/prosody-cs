use super::{Arc, EventHandler, HandlerResult, HashMap};
use crate::context::Context;
use crate::error::CsHandlerError;
use crate::event::{EventRegistry, NativeEvent};
use crate::timer::Timer;
use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use prosody::codec::BinaryPayload;
use prosody::consumer::DemandType;
use prosody::consumer::event_context::EventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::consumer::middleware::FallibleHandler;
use prosody::high_level::{ClientHandler, JsonBinaryCodecs};
use prosody::timers::{TimerType, Trigger};
use tracing::Instrument;
use tracing_opentelemetry::OpenTelemetrySpanExt;

/// Converts a [`HandlerResult`] from C# into a Rust `Result`.
///
/// Extracts and preserves the error message from the result when mapping
/// to [`CsHandlerError`].
///
/// # Errors
///
/// Returns [`CsHandlerError::Transient`] for retriable failures.
/// Returns [`CsHandlerError::Permanent`] for non-retriable failures.
fn map_handler_result(result: HandlerResult) -> Result<BinaryPayload, CsHandlerError> {
    match result {
        HandlerResult::Success { response } => {
            Ok(BinaryPayload::new(response, None::<String>, None::<String>))
        }
        HandlerResult::TransientError { message } => Err(CsHandlerError::Transient(message)),
        HandlerResult::PermanentError { message } => Err(CsHandlerError::Permanent(message)),
    }
}
/// Adapter bridging C# [`EventHandler`] to prosody's [`FallibleHandler`] trait.
///
/// This struct wraps a C# event handler and handles:
/// - Distributed tracing context propagation via OpenTelemetry
/// - Conversion between prosody message types and FFI-friendly wrappers
/// - Error classification for retry logic
pub(crate) struct CsHandler {
    /// C# event handler implementation receiving messages and timers.
    handler: Arc<dyn EventHandler>,
    /// OpenTelemetry propagator for distributed tracing context injection.
    propagator: Arc<TextMapCompositePropagator>,
    registry: Arc<EventRegistry>,
}

impl Clone for CsHandler {
    fn clone(&self) -> Self {
        Self {
            handler: Arc::clone(&self.handler),
            propagator: Arc::clone(&self.propagator),
            registry: Arc::clone(&self.registry),
        }
    }
}

impl CsHandler {
    pub(crate) fn new(
        handler: Arc<dyn EventHandler>,
        propagator: Arc<TextMapCompositePropagator>,
        registry: Arc<EventRegistry>,
    ) -> Self {
        Self {
            handler,
            propagator,
            registry,
        }
    }

    fn record_args<C, P, M>(
        &self,
        context: C,
        message: ConsumerMessage<P>,
    ) -> (tracing::Span, Context, M, HashMap<String, String>)
    where
        C: EventContext<Payload = BinaryPayload>,
        M: From<ConsumerMessage<P>>,
        P: Send + Sync + 'static,
    {
        let span = message.span();
        let mut carrier = HashMap::with_capacity(2);
        self.propagator
            .inject_context(&span.context(), &mut carrier);
        let context = Context::new(context.boxed(), Arc::clone(&self.propagator));
        (span, context, message.into(), carrier)
    }
}

/// [`FallibleHandler`] implementation that delegates to the C# handler.
///
/// Handles both message and timer events, injecting OpenTelemetry context
/// for distributed tracing continuity across the FFI boundary.
impl FallibleHandler for CsHandler {
    type Error = CsHandlerError;
    type Output = BinaryPayload;
    type Payload = BinaryPayload;

    /// Processes an incoming Kafka message by delegating to the C# handler.
    ///
    /// Injects the message's tracing span context into a carrier map that
    /// C# can use to continue the distributed trace.
    async fn on_message<C>(
        &self,
        context: C,
        message: ConsumerMessage<Self::Payload>,
        _demand_type: DemandType,
    ) -> Result<Self::Output, Self::Error>
    where
        C: EventContext<Payload = Self::Payload>,
    {
        let (span, context, message, carrier) = self.record_args(context, message);
        let event = self
            .registry
            .insert(NativeEvent::message(context, message))?;
        let result = self
            .handler
            .on_message(event.id(), carrier)
            .instrument(span)
            .await;
        let result = result?;

        // Map result to our error type, preserving error messages
        map_handler_result(result)
    }

    async fn on_excise<C>(
        &self,
        context: C,
        message: ConsumerMessage<()>,
        _demand_type: DemandType,
    ) -> Result<Self::Output, Self::Error>
    where
        C: EventContext<Payload = Self::Payload>,
    {
        let (span, context, message, carrier) = self.record_args(context, message);
        let event = self
            .registry
            .insert(NativeEvent::excise(context, message))?;
        let result = self
            .handler
            .on_excise(event.id(), carrier)
            .instrument(span)
            .await;
        let result = result?;
        map_handler_result(result)
    }

    /// Processes a timer event by delegating to the C# handler.
    ///
    /// Only application timers are forwarded to C#; internal prosody timers
    /// (e.g., heartbeat, rebalance) are silently acknowledged.
    async fn on_timer<C>(
        &self,
        context: C,
        trigger: Trigger,
        _demand_type: DemandType,
    ) -> Result<Self::Output, Self::Error>
    where
        C: EventContext<Payload = Self::Payload>,
    {
        // Only process Application timers - other types are internal to prosody
        if trigger.timer_type != TimerType::Application {
            return Ok(BinaryPayload::new(
                b"null".to_vec(),
                None::<String>,
                None::<String>,
            ));
        }

        // Get the span from the trigger for distributed tracing
        let span = trigger.span();

        // Inject span context into carrier for C#
        let mut carrier = HashMap::with_capacity(2);
        self.propagator
            .inject_context(&span.context(), &mut carrier);

        // Wrap the context and timer for C#
        let ctx = Context::new(context.boxed(), Arc::clone(&self.propagator));
        let tmr = Timer::new(trigger);

        // Call the C# handler - it returns a result with code and optional error
        // message
        let event = self.registry.insert(NativeEvent::timer(ctx, tmr))?;
        let result = self
            .handler
            .on_timer(event.id(), carrier)
            .instrument(span)
            .await;
        let result = result?;

        // Map result to our error type, preserving error messages
        map_handler_result(result)
    }

    /// Called when the handler is being shut down.
    ///
    /// No cleanup is needed since the C# handler lifetime is managed by
    /// [`ProsodyClient::handler`] field via `ArcSwap`.
    async fn shutdown(self) {}
}

impl ClientHandler for CsHandler {
    type Codecs = JsonBinaryCodecs;
}
