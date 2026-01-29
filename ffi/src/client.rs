//! `ProsodyClient` - Main FFI service for the Prosody client.
//!
//! This module exposes the prosody `HighLevelClient` to C# via `UniFFI`.
//! The client provides an object-oriented API that maps naturally to C#
//! classes.
//!
//! # Usage
//!
//! In C#, the generated class looks like:
//! ```csharp
//! using var client = ProsodyClient.New(options);
//! await client.SubscribeAsync(handler);
//! await client.SendAsync(topic, key, payload);
//! ```

use std::collections::HashMap;
use std::sync::Arc;

use arc_swap::ArcSwap;
use serde_json::Value;

use crate::RUNTIME;
use crate::config::{
    build_cassandra_config, build_consumer_builders, build_producer_config, get_mode,
};
use crate::error::{CsHandlerError, ProsodyError};
use crate::events::{Context, Message, Timer};
use crate::handler::{HandlerResultCode, NativeEventHandler};
use crate::types::{ClientOptions, ConsumerState};
use prosody::consumer::DemandType;
use prosody::consumer::event_context::EventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::consumer::middleware::FallibleHandler;
use prosody::high_level::HighLevelClient;
use prosody::high_level::state::ConsumerState as ProsodyConsumerState;
use prosody::timers::{TimerType, Trigger};

/// Internal handler that bridges C# `NativeEventHandler` to prosody's
/// `FallibleHandler`.
struct CsHandler {
    /// The C# native event handler implementation.
    handler: Arc<dyn NativeEventHandler>,
}

impl Clone for CsHandler {
    fn clone(&self) -> Self {
        Self {
            handler: Arc::clone(&self.handler),
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
        // Wrap the context and message for C#
        let ctx = Arc::new(Context::new(context.boxed()));
        let msg = Arc::new(Message::new(message));

        // TODO: Extract OpenTelemetry carrier from prosody context
        // For now, pass an empty carrier - C# can populate it if needed
        let carrier: HashMap<String, String> = HashMap::new();

        // Call the C# handler - it returns a result code
        let result = self
            .handler
            .on_message(ctx, msg, carrier)
            .await
            .map_err(|_| CsHandlerError::Permanent)?;

        // Map result code to our error type
        match result {
            HandlerResultCode::Success => Ok(()),
            HandlerResultCode::TransientError => Err(CsHandlerError::Transient),
            HandlerResultCode::PermanentError => Err(CsHandlerError::Permanent),
            HandlerResultCode::Cancelled => Err(CsHandlerError::Cancelled),
        }
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

        // Wrap the context and timer for C#
        let ctx = Arc::new(Context::new(context.boxed()));
        let tmr = Arc::new(Timer::new(trigger));

        // TODO: Extract OpenTelemetry carrier from prosody context
        // For now, pass an empty carrier - C# can populate it if needed
        let carrier: HashMap<String, String> = HashMap::new();

        // Call the C# handler - it returns a result code
        let result = self
            .handler
            .on_timer(ctx, tmr, carrier)
            .await
            .map_err(|_| CsHandlerError::Permanent)?;

        // Map result code to our error type
        match result {
            HandlerResultCode::Success => Ok(()),
            HandlerResultCode::TransientError => Err(CsHandlerError::Transient),
            HandlerResultCode::PermanentError => Err(CsHandlerError::Permanent),
            HandlerResultCode::Cancelled => Err(CsHandlerError::Cancelled),
        }
    }

    async fn shutdown(self) {
        self.handler.on_shutdown();
    }
}

/// Main Prosody client service exposed to C#.
///
/// This service wraps the prosody `HighLevelClient` and provides:
/// - Message sending to Kafka topics
/// - Subscribing to topics with handlers
/// - Consumer state management
/// - Stall detection
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
    handler: ArcSwap<Option<Arc<dyn NativeEventHandler>>>,
}

/// `UniFFI` interface implementation for `ProsodyClient`.
#[uniffi::export]
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
    pub fn new(options: ClientOptions) -> Result<Self, ProsodyError> {
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
        )
        .map_err(|_| ProsodyError::Internal)?;

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
    pub async fn subscribe(
        &self,
        handler: Arc<dyn NativeEventHandler>,
    ) -> Result<(), ProsodyError> {
        // Store the handler reference to keep it alive
        self.handler.store(Arc::new(Some(Arc::clone(&handler))));

        // Create the internal handler and subscribe
        let cs_handler = CsHandler { handler };
        self.client
            .subscribe(cs_handler)
            .await
            .map_err(|_| ProsodyError::Internal)?;

        Ok(())
    }

    /// Unsubscribe from topics and stop the consumer.
    ///
    /// # Errors
    ///
    /// Returns error if unsubscription fails.
    pub async fn unsubscribe(&self) -> Result<(), ProsodyError> {
        // Unsubscribe from the client
        self.client
            .unsubscribe()
            .await
            .map_err(|_| ProsodyError::Internal)?;

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
    /// * `payload` - The message payload (JSON string)
    ///
    /// # Errors
    ///
    /// Returns error if the message cannot be sent.
    pub async fn send(
        &self,
        topic: String,
        key: String,
        payload: String,
    ) -> Result<(), ProsodyError> {
        // Parse the payload as JSON
        let json_value: Value =
            serde_json::from_str(&payload).map_err(|_| ProsodyError::InvalidArgument)?;

        // Send the message
        self.client
            .send(topic.as_str().into(), &key, &json_value)
            .await
            .map_err(|_| ProsodyError::Internal)?;

        Ok(())
    }

    /// Returns the current consumer state.
    pub fn consumer_state(&self) -> ConsumerState {
        RUNTIME.block_on(async {
            let state_view = self.client.consumer_state().await;
            match &*state_view {
                ProsodyConsumerState::Unconfigured => ConsumerState::Unconfigured,
                ProsodyConsumerState::Configured(_) => ConsumerState::Configured,
                ProsodyConsumerState::Running { .. } => ConsumerState::Running,
            }
        })
    }

    /// Returns the number of partitions currently assigned to this consumer.
    pub fn assigned_partition_count(&self) -> u32 {
        RUNTIME.block_on(async { self.client.assigned_partition_count().await })
    }

    /// Returns `true` if the consumer is currently stalled.
    pub fn is_stalled(&self) -> bool {
        RUNTIME.block_on(async { self.client.is_stalled().await })
    }

    /// Returns the source system identifier configured for this client.
    pub fn source_system(&self) -> String {
        self.client.source_system().to_owned()
    }
}
