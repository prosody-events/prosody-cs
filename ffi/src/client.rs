//! `ProsodyClient` - Main FFI service for the Prosody client.
//!
//! This module exposes the prosody `HighLevelClient` to C# via `UniFFI`.
//! The client provides an object-oriented API that maps naturally to C# classes.
//!
//! # Usage
//!
//! In C#, the generated class looks like:
//! ```csharp
//! using var client = new ProsodyClient(options);
//! await client.SubscribeAsync(handler);
//! await client.SendAsync(topic, key, payload);
//! ```

use crate::error::{CSharpHandlerError, ProsodyError};
use crate::events::{MessageEvent, TimerEvent};
use crate::handler::{EventHandler, HandlerResultCode};
use crate::types::{ClientOptions, ConsumerState};
use crate::RUNTIME;
use prosody::consumer::event_context::EventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::consumer::middleware::FallibleHandler;
use prosody::consumer::DemandType;
use prosody::high_level::state::ConsumerState as ProsodyConsumerState;
use prosody::high_level::HighLevelClient;
use prosody::timers::{TimerType, Trigger};
use std::sync::Arc;
use tokio::sync::Mutex;

/// Internal handler that bridges C# `EventHandler` to prosody's `FallibleHandler`.
struct UniFFIHandler {
    /// The C# event handler implementation.
    handler: Arc<dyn EventHandler>,
}

impl Clone for UniFFIHandler {
    fn clone(&self) -> Self {
        Self {
            handler: Arc::clone(&self.handler),
        }
    }
}

impl FallibleHandler for UniFFIHandler {
    type Error = CSharpHandlerError;

    async fn on_message<C>(
        &self,
        context: C,
        message: ConsumerMessage,
        _demand_type: DemandType,
    ) -> Result<(), Self::Error>
    where
        C: EventContext,
    {
        // Box the context for the event
        let boxed_context = context.boxed();

        // Create the event (C# will own it via Arc)
        let event = Arc::new(MessageEvent::new(boxed_context, message));

        // Call the C# handler - it returns a result code
        let result = self
            .handler
            .on_message(event)
            .await
            .map_err(|_| CSharpHandlerError::Permanent)?;

        // Map result code to our error type
        match result {
            HandlerResultCode::Success => Ok(()),
            HandlerResultCode::TransientError => Err(CSharpHandlerError::Transient),
            HandlerResultCode::PermanentError => Err(CSharpHandlerError::Permanent),
            HandlerResultCode::Cancelled => Err(CSharpHandlerError::Cancelled),
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

        // Box the context for the event
        let boxed_context = context.boxed();

        // Create the event (C# will own it via Arc)
        let event = Arc::new(TimerEvent::new(boxed_context, trigger));

        // Call the C# handler - it returns a result code
        let result = self
            .handler
            .on_timer(event)
            .await
            .map_err(|_| CSharpHandlerError::Permanent)?;

        // Map result code to our error type
        match result {
            HandlerResultCode::Success => Ok(()),
            HandlerResultCode::TransientError => Err(CSharpHandlerError::Transient),
            HandlerResultCode::PermanentError => Err(CSharpHandlerError::Permanent),
            HandlerResultCode::Cancelled => Err(CSharpHandlerError::Cancelled),
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
    client: Arc<HighLevelClient<UniFFIHandler>>,
    /// Handler registration state (for managing callback lifetime).
    handler: Mutex<Option<Arc<dyn EventHandler>>>,
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
    /// Returns error if the client cannot connect to Kafka or options are invalid.
    #[uniffi::constructor]
    #[expect(
        clippy::needless_pass_by_value,
        reason = "UniFFI Record types are passed by value from C#; ownership transfer is intentional"
    )]
    pub fn new(options: ClientOptions) -> Result<Self, ProsodyError> {
        // TODO: Convert ClientOptions to prosody builder
        // For now, return an error indicating not yet implemented
        let _ = options; // Suppress unused warning until implemented
        Err(ProsodyError::Internal)
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
    pub async fn subscribe(&self, handler: Arc<dyn EventHandler>) -> Result<(), ProsodyError> {
        // Store the handler reference
        let mut guard = self.handler.lock().await;
        *guard = Some(Arc::clone(&handler));
        drop(guard);

        // TODO: Actually subscribe with the client
        // self.client.subscribe(UniFFIHandler { handler }).await;

        Ok(())
    }

    /// Unsubscribe from topics and stop the consumer.
    ///
    /// # Errors
    ///
    /// Returns error if unsubscription fails.
    pub async fn unsubscribe(&self) -> Result<(), ProsodyError> {
        // Clear the handler reference
        let mut guard = self.handler.lock().await;
        *guard = None;
        drop(guard);

        // TODO: Actually unsubscribe
        // self.client.unsubscribe().await;

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
    #[expect(
        clippy::unused_async,
        reason = "TODO stub - real implementation will use await for sending"
    )]
    pub async fn send(
        &self,
        topic: String,
        key: String,
        payload: String,
    ) -> Result<(), ProsodyError> {
        // TODO: Actually send the message
        // self.client.send(&topic, &key, &payload).await;
        let _ = (topic, key, payload);
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
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn consumer_state_default_is_unconfigured() {
        assert_eq!(ConsumerState::default(), ConsumerState::Unconfigured);
    }
}
