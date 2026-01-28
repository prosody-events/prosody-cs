//! Callback types for C# event handlers using Interoptopus patterns.
//!
//! This module defines callbacks using Interoptopus's `callback!` macro, which
//! generates properly-named delegate types in C#. The callbacks allow Rust to
//! invoke C# handler methods when message and timer events arrive.
//!
//! # Design
//!
//! The callback infrastructure uses Interoptopus's high-level `callback!` macro
//! which generates:
//! - A safe Rust wrapper struct around the function pointer
//! - Proper C# delegate types with P/Invoke marshalling
//! - Thread-safe Send + Sync implementations
//!
//! # Async Handler Pattern
//!
//! To support non-blocking async processing on the C# side:
//!
//! 1. Rust creates a `MessageEvent` or `TimerEvent` containing:
//!    - The prosody message/trigger (owned)
//!    - A oneshot sender for completion signaling
//!
//! 2. Rust passes a reference to C# via callback (returns immediately)
//!
//! 3. C# spawns an async task, processes the event, then calls `complete()`
//!
//! 4. Rust awaits the oneshot receiver (non-blocking)
//!
//! This allows Rust's async handler to yield to the Tokio runtime while
//! C# processes asynchronously.
//!
//! # Reference
//!
//! - prosody-js: `src/handler.rs` - Similar async pattern with NAPI-RS
//! - prosody-py: `src/handler.rs` - Similar pattern with `PyO3`
//! - prosody-rb: `ext/prosody/src/handler/mod.rs` - Similar pattern with Magnus
//! - Design doc: `docs/async-handler-design.md`

use crate::error::CSharpHandlerError;
use crate::events::{MessageEvent, TimerEvent};
use interoptopus::ffi_type;
use prosody::consumer::event_context::EventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::consumer::middleware::FallibleHandler;
use prosody::consumer::DemandType;
use prosody::timers::{TimerType, Trigger};
use std::sync::Arc;
use tokio::sync::oneshot;

// ============================================================================
// Handler Result Enum
// ============================================================================

/// Result code returned by C# handler callbacks.
///
/// This enum allows C# handlers to signal success, errors, or cancellation
/// back to Rust for proper error classification.
///
/// # C# Usage
///
/// ```csharp
/// public HandlerResultCode OnMessage(ref FFIMessage message)
/// {
///     try {
///         ProcessMessage(message);
///         return HandlerResultCode.Success;
///     } catch (TransientException) {
///         return HandlerResultCode.TransientError;
///     } catch (Exception) {
///         return HandlerResultCode.PermanentError;
///     }
/// }
/// ```
#[ffi_type]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum HandlerResultCode {
    /// Handler completed successfully.
    #[default]
    Success = 0,
    /// Transient error - should retry.
    TransientError = 1,
    /// Permanent error - should not retry.
    PermanentError = 2,
    /// Handler was cancelled.
    Cancelled = 3,
}

// ============================================================================
// Callback Definitions using Interoptopus callback! macro
// ============================================================================

// The callback! macro generates safe wrapper types around function pointers.
// These become proper delegates in C# with automatic marshalling.
//
// We pass our FFI types directly - Interoptopus handles the marshalling.
//
// Note: The callback! macro generates structs without documentation.
// This is a macro limitation - we cannot add doc comments to the generated types.
// The types are:
// - `OnMessageCallback`: Callback invoked when a Kafka message arrives
// - `OnTimerCallback`: Callback invoked when a timer fires
// - `OnShutdownCallback`: Callback invoked when the handler shuts down

#[expect(missing_docs, reason = "callback! macro does not support documentation")]
mod callback_types {
    use super::{MessageEvent, TimerEvent};
    use interoptopus::callback;

    // Callback for message events - receives a mutable pointer to MessageEvent.
    // The callback should spawn an async task and return immediately.
    // C# calls message_event_complete() when done processing.
    callback!(OnMessageCallback(event: &mut MessageEvent));

    // Callback for timer events - receives a mutable pointer to TimerEvent.
    // The callback should spawn an async task and return immediately.
    // C# calls timer_event_complete() when done processing.
    callback!(OnTimerCallback(event: &mut TimerEvent));

    // Callback for shutdown notification.
    callback!(OnShutdownCallback());
}

pub use callback_types::{OnMessageCallback, OnShutdownCallback, OnTimerCallback};

// ============================================================================
// Handler Callbacks Struct
// ============================================================================

/// Holds the C# callback functions for event handling.
///
/// This struct is stored in `CSharpHandler` and used to invoke C# code
/// when message and timer events arrive.
#[ffi_type]
#[derive(Clone, Copy, Default)]
pub struct HandlerCallbacks {
    /// Called when a message event arrives.
    pub on_message: OnMessageCallback,
    /// Called when a timer event fires.
    pub on_timer: OnTimerCallback,
    /// Called when the handler is shutting down.
    pub on_shutdown: OnShutdownCallback,
}

// ============================================================================
// CSharpHandler - FallibleHandler Implementation
// ============================================================================

/// Handler implementation that invokes C# callbacks.
///
/// This struct implements the prosody `FallibleHandler` trait, bridging
/// between the Rust async world and C# callbacks.
///
/// # Thread Safety
///
/// The handler is `Clone + Send + Sync` as required by prosody's middleware.
/// Callbacks are invoked synchronously within the Rust async context.
#[derive(Clone)]
pub struct CSharpHandler {
    callbacks: Arc<HandlerCallbacks>,
}

impl CSharpHandler {
    /// Creates a new C# handler with the given callbacks.
    #[must_use]
    pub fn new(callbacks: HandlerCallbacks) -> Self {
        Self {
            callbacks: Arc::new(callbacks),
        }
    }

    /// Creates a handler from an Arc'd callbacks struct.
    #[must_use]
    pub fn from_arc(callbacks: Arc<HandlerCallbacks>) -> Self {
        Self { callbacks }
    }
}

impl FallibleHandler for CSharpHandler {
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
        // Create oneshot channel for completion signaling
        let (tx, rx) = oneshot::channel();

        // Box the context for C# - allows C# to check should_cancel() for cancellation
        let boxed_context = context.boxed();

        // Create the event with context, message data and completion sender
        let mut event = MessageEvent::new(boxed_context, message, tx)?;

        // Call the C# callback - it should spawn an async task and return immediately
        self.callbacks.on_message.call(&mut event);

        // Wait for C# to signal completion via message_event_complete().
        // C# can await message_event_await_cancel() to detect cancellation.
        // The boxed context shares the cancellation state with the original context.
        let result = rx.await.unwrap_or(HandlerResultCode::Cancelled);

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

        // Create oneshot channel for completion signaling
        let (tx, rx) = oneshot::channel();

        // Box the context for C# - allows C# to check should_cancel() for cancellation
        let boxed_context = context.boxed();

        // Create the event with context, timer data and completion sender
        let mut event = TimerEvent::new(boxed_context, trigger, tx);

        // Call the C# callback - it should spawn an async task and return immediately
        self.callbacks.on_timer.call(&mut event);

        // Wait for C# to signal completion via timer_event_complete().
        // C# can await timer_event_await_cancel() to detect cancellation.
        // The boxed context shares the cancellation state with the original context.
        let result = rx.await.unwrap_or(HandlerResultCode::Cancelled);

        match result {
            HandlerResultCode::Success => Ok(()),
            HandlerResultCode::TransientError => Err(CSharpHandlerError::Transient),
            HandlerResultCode::PermanentError => Err(CSharpHandlerError::Permanent),
            HandlerResultCode::Cancelled => Err(CSharpHandlerError::Cancelled),
        }
    }

    async fn shutdown(self) {
        self.callbacks.on_shutdown.call_if_some();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn handler_result_code_values() {
        assert_eq!(HandlerResultCode::Success as i32, 0_i32);
        assert_eq!(HandlerResultCode::TransientError as i32, 1_i32);
        assert_eq!(HandlerResultCode::PermanentError as i32, 2_i32);
        assert_eq!(HandlerResultCode::Cancelled as i32, 3_i32);
    }

    #[test]
    fn handler_result_code_default_is_success() {
        assert_eq!(HandlerResultCode::default(), HandlerResultCode::Success);
    }

    #[test]
    fn csharp_handler_is_clone() {
        fn assert_clone<T: Clone>() {}
        assert_clone::<CSharpHandler>();
    }

    #[test]
    fn csharp_handler_is_send_sync() {
        fn assert_send_sync<T: Send + Sync>() {}
        assert_send_sync::<CSharpHandler>();
    }

    #[test]
    fn handler_callbacks_is_default() {
        let _ = HandlerCallbacks::default();
    }
}
