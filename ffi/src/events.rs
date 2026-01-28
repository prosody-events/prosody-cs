//! Event types for async C# handler invocation.
//!
//! This module defines event types that wrap prosody's `ConsumerMessage` and `Trigger`.
//! These are passed to C# handlers as owned objects (via Arc) that C# can hold
//! and interact with asynchronously.
//!
//! # Design
//!
//! `UniFFI` handles ownership transfer automatically via Arc:
//! - Rust creates the event and wraps it in `Arc<T>`
//! - C# receives an `IDisposable` object that holds an Arc reference
//! - When C# disposes, the Arc reference count decrements
//! - When all references are dropped, the event is cleaned up
//!
//! # Cancellation
//!
//! Each event has an `await_cancel()` async method that C# can await
//! to be notified when Rust requests cancellation. This enables
//! C# to create a `CancellationToken` that fires immediately.

use prosody::consumer::event_context::BoxEventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::consumer::Keyed;
use prosody::timers::Trigger;

// ============================================================================
// MessageEvent - Wraps ConsumerMessage
// ============================================================================

/// Message event passed to C# handlers.
///
/// Contains the Kafka message data and event context for cancellation.
/// C# receives this via the `EventHandler.on_message()` callback.
///
/// # Ownership
///
/// `UniFFI` wraps this in an Arc automatically. C# receives an `IDisposable`
/// object that properly releases the Arc when disposed.
#[derive(uniffi::Object)]
pub struct MessageEvent {
    /// The event context for cancellation checking.
    context: BoxEventContext,
    /// The wrapped consumer message from prosody.
    message: ConsumerMessage,
    /// Cached topic string (`ConsumerMessage::topic()` returns &str).
    topic: String,
    /// Cached key string.
    key: String,
    /// Cached payload string (JSON).
    payload: String,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks: exported methods must be in #[uniffi::export] block, but associated functions without #[uniffi::constructor] are not supported there"
)]
impl MessageEvent {
    /// Creates a new message event.
    ///
    /// # Arguments
    ///
    /// * `context` - The event context for cancellation
    /// * `message` - The consumer message from prosody
    #[must_use]
    pub fn new(context: BoxEventContext, message: ConsumerMessage) -> Self {
        let topic = message.topic().to_string();
        let key = message.key().to_string();
        let payload = message.payload().to_string();
        Self {
            context,
            message,
            topic,
            key,
            payload,
        }
    }
}

/// `UniFFI` interface implementation for `MessageEvent`.
#[uniffi::export]
impl MessageEvent {
    /// Returns the topic name.
    #[must_use] 
    pub fn topic(&self) -> String {
        self.topic.clone()
    }

    /// Returns the partition number.
    #[must_use] 
    pub fn partition(&self) -> i32 {
        self.message.partition()
    }

    /// Returns the message offset.
    #[must_use] 
    pub fn offset(&self) -> i64 {
        self.message.offset()
    }

    /// Returns the message timestamp in milliseconds since epoch.
    #[must_use] 
    pub fn timestamp(&self) -> i64 {
        self.message.timestamp().timestamp_millis()
    }

    /// Returns the message key as a UTF-8 string.
    #[must_use] 
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// Returns the message payload as a UTF-8 string (JSON).
    #[must_use] 
    pub fn payload(&self) -> String {
        self.payload.clone()
    }

    /// Returns true if cancellation has been requested.
    #[must_use] 
    pub fn should_cancel(&self) -> bool {
        self.context.should_cancel()
    }

    /// Async method that completes when cancellation is requested.
    ///
    /// C# awaits this to get notified of cancellation without polling.
    /// `UniFFI` maps this to a C# `Task` that completes when cancelled.
    ///
    /// # Usage (C#)
    ///
    /// ```csharp
    /// var cancelTask = Task.Run(async () => {
    ///     await event.AwaitCancelAsync();
    ///     cts.Cancel();  // Fire the CancellationToken
    /// });
    /// ```
    pub async fn await_cancel(&self) {
        self.context.on_cancel().await;
    }
}

// ============================================================================
// TimerEvent - Wraps Trigger
// ============================================================================

/// Timer event passed to C# handlers.
///
/// Contains the timer trigger data and event context for cancellation.
/// C# receives this via the `EventHandler.on_timer()` callback.
///
/// # Ownership
///
/// `UniFFI` wraps this in an Arc automatically. C# receives an `IDisposable`
/// object that properly releases the Arc when disposed.
#[derive(uniffi::Object)]
pub struct TimerEvent {
    /// The event context for cancellation checking.
    context: BoxEventContext,
    /// The wrapped trigger from prosody.
    trigger: Trigger,
    /// Cached key string.
    key: String,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks: exported methods must be in #[uniffi::export] block, but associated functions without #[uniffi::constructor] are not supported there"
)]
impl TimerEvent {
    /// Creates a new timer event.
    ///
    /// # Arguments
    ///
    /// * `context` - The event context for cancellation
    /// * `trigger` - The timer trigger from prosody
    #[must_use]
    pub fn new(context: BoxEventContext, trigger: Trigger) -> Self {
        let key = trigger.key.to_string();
        Self {
            context,
            trigger,
            key,
        }
    }
}

/// `UniFFI` interface implementation for `TimerEvent`.
#[uniffi::export]
impl TimerEvent {
    /// Returns the timer key as a UTF-8 string.
    #[must_use] 
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// Returns the timer fire time in milliseconds since epoch.
    #[must_use] 
    pub fn time(&self) -> i64 {
        i64::from(self.trigger.time.epoch_seconds()) * 1000
    }

    /// Returns true if cancellation has been requested.
    #[must_use] 
    pub fn should_cancel(&self) -> bool {
        self.context.should_cancel()
    }

    /// Async method that completes when cancellation is requested.
    ///
    /// C# awaits this to get notified of cancellation without polling.
    /// `UniFFI` maps this to a C# `Task` that completes when cancelled.
    pub async fn await_cancel(&self) {
        self.context.on_cancel().await;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn message_event_is_send_sync() {
        fn assert_send_sync<T: Send + Sync>() {}
        assert_send_sync::<MessageEvent>();
    }

    #[test]
    fn timer_event_is_send_sync() {
        fn assert_send_sync<T: Send + Sync>() {}
        assert_send_sync::<TimerEvent>();
    }
}
