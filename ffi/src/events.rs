//! Event types for async C# handler invocation.
//!
//! This module defines opaque event types that wrap prosody's `ConsumerMessage` and `Trigger`
//! along with a oneshot channel for completion signaling. These are passed to C# handlers
//! as owned objects that C# can hold and interact with asynchronously.
//!
//! # Design
//!
//! The event types use Interoptopus's `#[ffi_type(opaque)]` pattern with standalone
//! `#[ffi_function]` accessors:
//! - Rust creates the event with message/timer data + oneshot sender
//! - Rust passes the event to C# via callback (C# receives a reference)
//! - C# processes asynchronously, accessing data via getter functions
//! - C# calls the `complete` function when done, which sends the result through the channel
//! - Rust awaits the oneshot receiver (non-blocking)
//!
//! This allows Rust's async handler to await C#'s async processing without blocking.
//!
//! # Reference
//!
//! - prosody-js: `src/handler.rs` - Similar async pattern with NAPI-RS `ThreadsafeFunction`
//! - prosody-py: `src/handler.rs` - Similar pattern with `PyO3`
//! - Design doc: `docs/async-handler-design.md`

use crate::HandlerResultCode;
use crate::RUNTIME;
use interoptopus::pattern::asynk::AsyncCallback;
use interoptopus::{ffi, ffi_type};
use prosody::consumer::event_context::BoxEventContext;
use prosody::consumer::message::ConsumerMessage;
use prosody::timers::Trigger;
use std::ffi::{CString, NulError as FfiNulError};
use tokio::sync::oneshot;

// ============================================================================
// MessageEvent - Wraps ConsumerMessage + completion channel
// ============================================================================

/// Opaque message event passed to C# handlers.
///
/// Contains:
/// - The event context for timer operations and cancellation
/// - The Kafka message data
/// - A completion channel for async signaling
///
/// C# receives this via callback, processes it asynchronously, then calls
/// `message_event_complete()` to signal the result back to Rust.
#[ffi_type(opaque)]
pub struct MessageEvent {
    /// The event context for timer operations and cancellation checking.
    context: BoxEventContext,
    /// The wrapped consumer message from prosody.
    message: ConsumerMessage,
    /// Cached `CString` for topic (needed for lifetime of `CStrPtr`).
    topic_cstring: CString,
    /// Cached payload bytes (`ConsumerMessage::payload()` returns a temporary).
    payload_bytes: Vec<u8>,
    /// Oneshot sender for completion signaling.
    sender: Option<oneshot::Sender<HandlerResultCode>>,
}

impl MessageEvent {
    /// Creates a new message event.
    ///
    /// # Arguments
    ///
    /// * `context` - The event context for timer operations and cancellation
    /// * `message` - The consumer message from prosody
    /// * `sender` - The oneshot sender for completion signaling
    ///
    /// # Errors
    ///
    /// Returns an error if the topic contains a null byte.
    pub fn new(
        context: BoxEventContext,
        message: ConsumerMessage,
        sender: oneshot::Sender<HandlerResultCode>,
    ) -> Result<Self, FfiNulError> {
        let topic_cstring = CString::new(message.topic().to_string())?;
        let payload_bytes = message.payload().to_string().into_bytes();
        Ok(Self {
            context,
            message,
            topic_cstring,
            payload_bytes,
            sender: Some(sender),
        })
    }

    /// Takes the sender out of this event, returning it.
    /// Used internally when the event is being dropped or cancelled.
    pub fn take_sender(&mut self) -> Option<oneshot::Sender<HandlerResultCode>> {
        self.sender.take()
    }
}

// ============================================================================
// MessageEvent FFI Functions
// ============================================================================

// Note: The ffi_function macro generates internal structs without documentation.
// This is a macro limitation - we wrap in a module with expect attribute.
#[expect(missing_docs, reason = "ffi_function macro generates undocumented internal types")]
mod message_event_ffi {
    use super::{ffi, AsyncCallback, MessageEvent, RUNTIME};
    use crate::error::FFIErrorCode;
    use crate::HandlerResultCode;
    use interoptopus::ffi_function;
    use prosody::consumer::Keyed;

    /// Returns the topic name as a null-terminated string.
    #[ffi_function]
    pub fn message_event_topic(msg_event: &MessageEvent) -> ffi::CStrPtr<'_> {
        ffi::CStrPtr::from_cstr(&msg_event.topic_cstring)
    }

    /// Returns the partition number.
    #[ffi_function]
    pub fn message_event_partition(msg_event: &MessageEvent) -> i32 {
        msg_event.message.partition()
    }

    /// Returns the message offset.
    #[ffi_function]
    pub fn message_event_offset(msg_event: &MessageEvent) -> i64 {
        msg_event.message.offset()
    }

    /// Returns the message timestamp in milliseconds since epoch.
    #[ffi_function]
    pub fn message_event_timestamp(msg_event: &MessageEvent) -> i64 {
        msg_event.message.timestamp().timestamp_millis()
    }

    /// Returns the message key as a byte slice.
    #[ffi_function]
    pub fn message_event_key(msg_event: &MessageEvent) -> ffi::Slice<'_, u8> {
        ffi::Slice::from_slice(msg_event.message.key().as_bytes())
    }

    /// Returns the message payload as a byte slice.
    #[ffi_function]
    pub fn message_event_payload(msg_event: &MessageEvent) -> ffi::Slice<'_, u8> {
        ffi::Slice::from_slice(&msg_event.payload_bytes)
    }

    /// Signals completion of message processing.
    ///
    /// C# must call this exactly once when done processing the message.
    /// The result code determines how prosody handles the message:
    /// - `Success`: Message processed successfully
    /// - `TransientError`: Temporary error, message will be retried
    /// - `PermanentError`: Permanent error, message sent to dead letter queue
    /// - `Cancelled`: Processing was cancelled
    ///
    /// Returns `Ok` on success, or `InvalidContext` if already completed.
    #[ffi_function]
    pub fn message_event_complete(
        msg_event: &mut MessageEvent,
        result: HandlerResultCode,
    ) -> ffi::Result<(), FFIErrorCode> {
        if let Some(sender) = msg_event.sender.take() {
            // Ignore send error - receiver may have been dropped if cancelled
            let _ = sender.send(result);
            ffi::Ok(())
        } else {
            // Already completed - this is an error
            ffi::Err(FFIErrorCode::InvalidContext)
        }
    }

    // === Context Operations ===
    // These delegate to the embedded BoxEventContext

    /// Returns true if cancellation has been requested.
    #[ffi_function]
    pub fn message_event_should_cancel(msg_event: &MessageEvent) -> bool {
        msg_event.context.should_cancel()
    }

    /// Async function that completes when cancellation is requested.
    ///
    /// C# awaits this to get notified of cancellation without polling.
    /// Interoptopus generates TaskCompletionSource-based async code on C# side.
    ///
    /// # Usage (C# wrapper)
    ///
    /// ```csharp
    /// var cancelTask = Task.Run(async () => {
    ///     await ProsodyFFI.MessageEventAwaitCancelAsync(evtHandle);
    ///     cts.Cancel();  // Fire the CancellationToken
    /// });
    /// ```
    #[ffi_function]
    pub fn message_event_await_cancel(
        msg_event: &MessageEvent,
        callback: AsyncCallback<ffi::Result<(), FFIErrorCode>>,
    ) {
        // Clone the context's cancellation token to await on
        let context = msg_event.context.clone();

        // Spawn a task on the global runtime that awaits cancellation
        RUNTIME.spawn(async move {
            context.on_cancel().await;
            callback.call(&ffi::Ok(()));
        });
    }
}

pub use message_event_ffi::{
    message_event_await_cancel, message_event_complete, message_event_key, message_event_offset,
    message_event_partition, message_event_payload, message_event_should_cancel,
    message_event_timestamp, message_event_topic,
};

// ============================================================================
// TimerEvent - Wraps Trigger + completion channel
// ============================================================================

/// Opaque timer event passed to C# handlers.
///
/// Contains:
/// - The event context for timer operations and cancellation
/// - The timer trigger data
/// - A completion channel for async signaling
///
/// C# receives this via callback, processes it asynchronously, then calls
/// `timer_event_complete()` to signal the result back to Rust.
#[ffi_type(opaque)]
pub struct TimerEvent {
    /// The event context for timer operations and cancellation checking.
    context: BoxEventContext,
    /// The wrapped trigger from prosody.
    trigger: Trigger,
    /// Oneshot sender for completion signaling.
    sender: Option<oneshot::Sender<HandlerResultCode>>,
}

impl TimerEvent {
    /// Creates a new timer event.
    ///
    /// # Arguments
    ///
    /// * `context` - The event context for timer operations and cancellation
    /// * `trigger` - The timer trigger from prosody
    /// * `sender` - The oneshot sender for completion signaling
    #[must_use]
    pub fn new(
        context: BoxEventContext,
        trigger: Trigger,
        sender: oneshot::Sender<HandlerResultCode>,
    ) -> Self {
        Self {
            context,
            trigger,
            sender: Some(sender),
        }
    }

    /// Takes the sender out of this event, returning it.
    /// Used internally when the event is being dropped or cancelled.
    pub fn take_sender(&mut self) -> Option<oneshot::Sender<HandlerResultCode>> {
        self.sender.take()
    }
}

// ============================================================================
// TimerEvent FFI Functions
// ============================================================================

#[expect(missing_docs, reason = "ffi_function macro generates undocumented internal types")]
mod timer_event_ffi {
    use super::{ffi, AsyncCallback, TimerEvent, RUNTIME};
    use crate::error::FFIErrorCode;
    use crate::HandlerResultCode;
    use interoptopus::ffi_function;

    /// Returns the timer key as a byte slice.
    #[ffi_function]
    pub fn timer_event_key(timer_evt: &TimerEvent) -> ffi::Slice<'_, u8> {
        ffi::Slice::from_slice(timer_evt.trigger.key.as_bytes())
    }

    /// Returns the timer fire time in milliseconds since epoch.
    #[ffi_function]
    pub fn timer_event_time(timer_evt: &TimerEvent) -> i64 {
        i64::from(timer_evt.trigger.time.epoch_seconds()) * 1000
    }

    /// Signals completion of timer processing.
    ///
    /// C# must call this exactly once when done processing the timer.
    /// The result code determines how prosody handles the timer:
    /// - `Success`: Timer processed successfully
    /// - `TransientError`: Temporary error, timer will be retried
    /// - `PermanentError`: Permanent error
    /// - `Cancelled`: Processing was cancelled
    ///
    /// Returns `Ok` on success, or `InvalidContext` if already completed.
    #[ffi_function]
    pub fn timer_event_complete(
        timer_evt: &mut TimerEvent,
        result: HandlerResultCode,
    ) -> ffi::Result<(), FFIErrorCode> {
        if let Some(sender) = timer_evt.sender.take() {
            // Ignore send error - receiver may have been dropped if cancelled
            let _ = sender.send(result);
            ffi::Ok(())
        } else {
            // Already completed - this is an error
            ffi::Err(FFIErrorCode::InvalidContext)
        }
    }

    // === Context Operations ===
    // These delegate to the embedded BoxEventContext

    /// Returns true if cancellation has been requested.
    #[ffi_function]
    pub fn timer_event_should_cancel(timer_evt: &TimerEvent) -> bool {
        timer_evt.context.should_cancel()
    }

    /// Async function that completes when cancellation is requested.
    ///
    /// C# awaits this to get notified of cancellation without polling.
    /// Interoptopus generates TaskCompletionSource-based async code on C# side.
    #[ffi_function]
    pub fn timer_event_await_cancel(
        timer_evt: &TimerEvent,
        callback: AsyncCallback<ffi::Result<(), FFIErrorCode>>,
    ) {
        // Clone the context's cancellation token to await on
        let context = timer_evt.context.clone();

        // Spawn a task on the global runtime that awaits cancellation
        RUNTIME.spawn(async move {
            context.on_cancel().await;
            callback.call(&ffi::Ok(()));
        });
    }
}

pub use timer_event_ffi::{
    timer_event_await_cancel, timer_event_complete, timer_event_key, timer_event_should_cancel,
    timer_event_time,
};

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
