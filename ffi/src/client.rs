//! FFI bindings for the Prosody client.
//!
//! This module exposes prosody's [`HighLevelClient`] to C# via `UniFFI`.
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
//!
//! # Error Handling
//!
//! All fallible operations return [`FfiError`], which maps to C# exceptions.
//! Handler errors from C# are represented as [`CsHandlerError`] and classified
//! as either transient (retriable) or permanent.
//!
//! [`HighLevelClient`]: prosody::high_level::HighLevelClient
//! [`FfiError`]: crate::error::FfiError
//! [`CsHandlerError`]: crate::error::CsHandlerError

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
use crate::logging::ensure_tracing_initialized;
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

/// Converts a [`HandlerResult`] from C# into a Rust `Result`.
///
/// Extracts and preserves the error message from the result when mapping
/// to [`CsHandlerError`].
///
/// # Errors
///
/// Returns [`CsHandlerError::Transient`] for retriable failures.
/// Returns [`CsHandlerError::Permanent`] for non-retriable failures.
fn map_handler_result(result: HandlerResult) -> Result<(), CsHandlerError> {
    let error_msg = result.error_message.unwrap_or_default();

    match result.code {
        HandlerResultCode::Success => Ok(()),
        HandlerResultCode::TransientError => Err(CsHandlerError::Transient(error_msg)),
        HandlerResultCode::PermanentError => Err(CsHandlerError::Permanent(error_msg)),
    }
}

/// Adapter bridging C# [`EventHandler`] to prosody's [`FallibleHandler`] trait.
///
/// This struct wraps a C# event handler and handles:
/// - Distributed tracing context propagation via OpenTelemetry
/// - Conversion between prosody message types and FFI-friendly wrappers
/// - Error classification for retry logic
struct CsHandler {
    /// C# event handler implementation receiving messages and timers.
    handler: Arc<dyn EventHandler>,
    /// OpenTelemetry propagator for distributed tracing context injection.
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

/// [`FallibleHandler`] implementation that delegates to the C# handler.
///
/// Handles both message and timer events, injecting OpenTelemetry context
/// for distributed tracing continuity across the FFI boundary.
impl FallibleHandler for CsHandler {
    type Error = CsHandlerError;

    /// Processes an incoming Kafka message by delegating to the C# handler.
    ///
    /// Injects the message's tracing span context into a carrier map that
    /// C# can use to continue the distributed trace.
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

    /// Processes a timer event by delegating to the C# handler.
    ///
    /// Only application timers are forwarded to C#; internal prosody timers
    /// (e.g., heartbeat, rebalance) are silently acknowledged.
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

    /// Called when the handler is being shut down.
    ///
    /// No cleanup is needed since the C# handler lifetime is managed by
    /// [`ProsodyClient::handler`] field via `ArcSwap`.
    async fn shutdown(self) {}
}

/// Native Prosody client exposed to C# via `UniFFI`.
///
/// This is the low-level FFI client. C# wraps this in `Prosody.ProsodyClient`
/// which provides typed JSON, `CancellationToken` support, and idiomatic
/// properties.
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
///
/// # Thread Safety
///
/// This type is `Send + Sync` and can be safely shared across threads.
/// The internal state is protected by atomic operations and async-aware locks.
#[derive(uniffi::Object)]
pub struct ProsodyClient {
    /// Underlying prosody high-level client instance.
    client: HighLevelClient<CsHandler>,
    /// Holds the C# handler reference to prevent premature deallocation.
    ///
    /// Uses [`ArcSwap`] for lock-free updates during subscribe/unsubscribe.
    handler: ArcSwap<Option<Arc<dyn EventHandler>>>,
}

/// UniFFI-exported methods for [`ProsodyClient`].
#[uniffi::export(async_runtime = "tokio")]
impl ProsodyClient {
    /// Creates a new client with the specified configuration.
    ///
    /// Initializes the tracing subsystem if not already initialized, then
    /// builds and connects the underlying Kafka producer and consumer.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if:
    /// - Kafka bootstrap servers are unreachable
    /// - Configuration options are invalid
    /// - Cassandra connection fails (when persistence is enabled)
    #[uniffi::constructor]
    #[expect(
        clippy::needless_pass_by_value,
        reason = "UniFFI Record types are passed by value from C#; ownership transfer is \
                  intentional"
    )]
    pub fn new(options: ClientOptions) -> Result<Self, FfiError> {
        // Ensure tracing is initialized (idempotent)
        ensure_tracing_initialized();

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

    /// Subscribes to configured topics and begins consuming messages.
    ///
    /// The handler receives messages and timer events asynchronously until
    /// [`unsubscribe`](Self::unsubscribe) is called. The handler reference is
    /// retained internally to prevent garbage collection on the C# side.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if the consumer fails to start or
    /// topic subscription fails.
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

    /// Stops consuming messages and unsubscribes from all topics.
    ///
    /// In-flight messages are allowed to complete before this method returns.
    /// The handler reference is released, allowing C# garbage collection.
    ///
    /// # Errors
    ///
    /// Returns [`FfiError::Client`] if the consumer fails to stop cleanly.
    pub async fn unsubscribe(&self) -> Result<(), FfiError> {
        // Unsubscribe from the client
        self.client.unsubscribe().await?;

        // Clear the handler reference
        self.handler.store(Arc::new(None));

        Ok(())
    }

    /// Sends a message to a Kafka topic.
    ///
    /// The payload must be valid UTF-8 encoded JSON. OpenTelemetry tracing
    /// context is extracted from the carrier to link the send operation with
    /// the parent span from C#.
    ///
    /// # Errors
    ///
    /// - [`FfiError::Json`] if the payload is not valid JSON.
    /// - [`FfiError::Cancelled`] if the cancellation signal was triggered.
    /// - [`FfiError::Client`] if the Kafka producer fails to deliver.
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
