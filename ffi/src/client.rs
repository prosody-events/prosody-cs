//! `ProsodyClient` - FFI service for the Prosody client.
//!
//! This module exposes the prosody `HighLevelClient` to C# via `UniFFI`.
//! The client provides an object-oriented API that maps naturally to C#
//! classes.
//!
//! # Architecture
//!
//! This is the low-level FFI client. C# code wraps this in an idiomatic
//! public `ProsodyClient` class that provides:
//! - Typed JSON payloads (`Send<T>()`, `GetPayload<T>()`)
//! - `CancellationToken` support on all async methods
//! - Properties instead of methods for simple getters

use std::collections::HashMap;
use std::sync::Arc;

use arc_swap::ArcSwap;
use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use simd_json::serde::from_slice;
use tracing::field::Empty;
use tracing::{Instrument, debug, info_span};
use tracing_opentelemetry::OpenTelemetrySpanExt;

use crate::cancellation::CancellationSignal;
use crate::config::{
    build_cassandra_config, build_consumer_builders, build_producer_config, get_mode,
};
use crate::context::Context;
use crate::error::{CsHandlerError, FfiError};
use crate::handler::{EventHandler, HandlerResult, HandlerResultCode};
use crate::message::Message;
use crate::timer::Timer;
use crate::types::{ClientOptions, ConsumerState};
use prosody::consumer::DemandType;
use prosody::consumer::event_context::EventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::consumer::middleware::FallibleHandler;
use prosody::high_level::HighLevelClient;
use prosody::high_level::state::ConsumerState as ProsodyConsumerState;
use prosody::propagator::new_propagator;
use prosody::timers::{TimerType, Trigger};

/// Maps a `HandlerResult` from C# to a `Result` for prosody.
///
/// Extracts the error message from the result to preserve it in the error type.
fn map_handler_result(result: HandlerResult) -> Result<(), CsHandlerError> {
    let error_msg = result.error_message.unwrap_or_default();

    match result.code {
        HandlerResultCode::Success => Ok(()),
        HandlerResultCode::TransientError => Err(CsHandlerError::Transient(error_msg)),
        HandlerResultCode::PermanentError => Err(CsHandlerError::Permanent(error_msg)),
    }
}

/// Internal handler that bridges C# `EventHandler` to prosody's
/// `FallibleHandler`.
struct CsHandler {
    /// The C# native event handler implementation.
    handler: Arc<dyn EventHandler>,
    /// OpenTelemetry propagator for distributed tracing context propagation.
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

impl FallibleHandler for CsHandler {
    type Error = CsHandlerError;

    async fn on_message<C>(
        &self,
        context: C,
        message: ConsumerMessage,
        _demand_type: DemandType,
    ) -> Result<(), Self::Error>
    where
        C: EventContext,
    {
        // Get the span from the message for distributed tracing
        let span = message.span();

        // Inject span context into carrier for C#
        let mut carrier = HashMap::with_capacity(2);
        self.propagator
            .inject_context(&span.context(), &mut carrier);

        // Wrap the context and message for C#
        let ctx = Arc::new(Context::new(context.boxed(), Arc::clone(&self.propagator)));
        let msg = Arc::new(Message::new(message)?);

        // Call the C# handler - it returns a result with code and optional error
        // message
        let result = self
            .handler
            .on_message(ctx, msg, carrier)
            .instrument(span)
            .await?;

        // Map result to our error type, preserving error messages
        map_handler_result(result)
    }

    async fn on_timer<C>(
        &self,
        context: C,
        trigger: Trigger,
        _demand_type: DemandType,
    ) -> Result<(), Self::Error>
    where
        C: EventContext,
    {
        // Only process Application timers - other types are internal to prosody
        if trigger.timer_type != TimerType::Application {
            return Ok(());
        }

        // Get the span from the trigger for distributed tracing
        let span = trigger.span();

        // Inject span context into carrier for C#
        let mut carrier = HashMap::with_capacity(2);
        self.propagator
            .inject_context(&span.context(), &mut carrier);

        // Wrap the context and timer for C#
        let ctx = Arc::new(Context::new(context.boxed(), Arc::clone(&self.propagator)));
        let tmr = Arc::new(Timer::new(trigger));

        // Call the C# handler - it returns a result with code and optional error
        // message
        let result = self
            .handler
            .on_timer(ctx, tmr, carrier)
            .instrument(span)
            .await?;

        // Map result to our error type, preserving error messages
        map_handler_result(result)
    }

    async fn shutdown(self) {}
}

/// Native Prosody client service exposed to C#.
///
/// This is the low-level FFI client. C# wraps this in `Prosody.ProsodyClient`
/// which provides typed JSON, `CancellationToken`, and idiomatic properties.
///
/// # Lifecycle
///
/// ```text
///       ┌──────────┐
///       │  Created │
///       └────┬─────┘
///            │ new()
///            ▼
///       ┌──────────┐
///       │   Idle   │◄────────────────┐
///       └────┬─────┘                 │
///            │ subscribe()           │ unsubscribe()
///            ▼                       │
///       ┌──────────┐                 │
///       │Subscribed├─────────────────┘
///       └────┬─────┘
///            │ drop / dispose
///            ▼
///       ┌──────────┐
///       │ Disposed │
///       └──────────┘
/// ```
#[derive(uniffi::Object)]
pub struct ProsodyClient {
    /// The wrapped high-level client.
    client: HighLevelClient<CsHandler>,
    /// Handler registration state (for managing callback lifetime).
    handler: ArcSwap<Option<Arc<dyn EventHandler>>>,
}

/// `UniFFI` interface implementation for `ProsodyClient`.
#[uniffi::export(async_runtime = "tokio")]
impl ProsodyClient {
    /// Creates a new `ProsodyClient` with the given options.
    ///
    /// # Arguments
    ///
    /// * `options` - Configuration options for the client
    ///
    /// # Errors
    ///
    /// Returns error if the client cannot connect to Kafka or options are
    /// invalid.
    #[uniffi::constructor]
    #[expect(
        clippy::needless_pass_by_value,
        reason = "UniFFI Record types are passed by value from C#; ownership transfer is \
                  intentional"
    )]
    pub fn new(options: ClientOptions) -> Result<Self, FfiError> {
        // Build all configuration from ClientOptions
        let mut producer_config = build_producer_config(&options);
        let consumer_builders = build_consumer_builders(&options);
        let cassandra_config = build_cassandra_config(&options);
        let mode = get_mode(&options);

        // Create the high-level client
        let client = HighLevelClient::new(
            mode,
            &mut producer_config,
            &consumer_builders,
            &cassandra_config,
        )?;

        Ok(Self {
            client,
            handler: ArcSwap::new(Arc::new(None)),
        })
    }

    /// Subscribe to topics with the given event handler.
    ///
    /// The handler will receive messages and timer events asynchronously.
    ///
    /// # Arguments
    ///
    /// * `handler` - The C# event handler implementation
    ///
    /// # Errors
    ///
    /// Returns error if subscription fails.
    pub async fn subscribe(&self, handler: Arc<dyn EventHandler>) -> Result<(), FfiError> {
        // Store the handler reference to keep it alive
        self.handler.store(Arc::new(Some(Arc::clone(&handler))));

        // Create the internal handler with propagator for distributed tracing
        let cs_handler = CsHandler {
            handler,
            propagator: Arc::new(new_propagator()),
        };
        self.client.subscribe(cs_handler).await?;

        Ok(())
    }

    /// Unsubscribe from topics and stop the consumer.
    ///
    /// # Errors
    ///
    /// Returns error if unsubscription fails.
    pub async fn unsubscribe(&self) -> Result<(), FfiError> {
        // Unsubscribe from the client
        self.client.unsubscribe().await?;

        // Clear the handler reference
        self.handler.store(Arc::new(None));

        Ok(())
    }

    /// Send a message to a topic.
    ///
    /// # Arguments
    ///
    /// * `topic` - The topic to send to
    /// * `key` - The message key
    /// * `payload` - The message payload (UTF-8 JSON bytes)
    /// * `carrier` - OpenTelemetry carrier for context propagation
    /// * `cancel` - Optional cancellation signal to abort the operation
    ///
    /// # Errors
    ///
    /// Returns `Json` if the payload is not valid JSON.
    /// Returns `Cancelled` if the operation was cancelled.
    /// Returns `Internal` if the message cannot be sent.
    pub async fn send(
        &self,
        topic: String,
        key: String,
        mut payload: Vec<u8>,
        carrier: HashMap<String, String>,
        cancel: Option<Arc<CancellationSignal>>,
    ) -> Result<(), FfiError> {
        // Extract OpenTelemetry context from carrier passed by C#
        let context = self.client.propagator().extract(&carrier);

        // Create span with extracted context as parent (matches C#
        // SendAsync/SendRawAsync)
        let span = info_span!("csharp-Send", %topic, %key, aborted = Empty);
        if let Err(err) = span.set_parent(context) {
            debug!("failed to set parent span: {err:#}");
        }

        // Parse the payload as JSON using simd_json's serde integration.
        // This deserializes into serde_json::Value which prosody expects.
        let json_value: serde_json::Value = from_slice(&mut payload)?;

        // Send the message with tracing, with optional cancellation
        let send_future = self
            .client
            .send(topic.as_str().into(), &key, &json_value)
            .instrument(span.clone());

        if let Some(signal) = cancel {
            tokio::select! {
                result = send_future => {
                    span.record("aborted", false);
                    result?;
                }
                () = signal.cancelled() => {
                    span.record("aborted", true);
                    return Err(FfiError::Cancelled);
                }
            }
        } else {
            send_future.await?;
            span.record("aborted", false);
        }

        Ok(())
    }

    /// Returns the current consumer state.
    pub async fn consumer_state(&self) -> ConsumerState {
        let state_view = self.client.consumer_state().await;
        match &*state_view {
            ProsodyConsumerState::Unconfigured => ConsumerState::Unconfigured,
            ProsodyConsumerState::Configured(_) => ConsumerState::Configured,
            ProsodyConsumerState::Running { .. } => ConsumerState::Running,
        }
    }

    /// Returns the number of partitions currently assigned to this consumer.
    pub async fn assigned_partition_count(&self) -> u32 {
        self.client.assigned_partition_count().await
    }

    /// Returns `true` if the consumer is currently stalled.
    pub async fn is_stalled(&self) -> bool {
        self.client.is_stalled().await
    }

    /// Returns the source system identifier configured for this client.
    pub fn source_system(&self) -> String {
        self.client.source_system().to_owned()
    }
}
