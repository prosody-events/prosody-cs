//! Transfers callback resources into `BoltFFI` class handles.

use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};

use ahash::RandomState;
use parking_lot::Mutex;
use scc::HashMap;

use crate::context::Context;
use crate::error::FfiError;
use crate::message::{ExciseMessage, Message};
use crate::timer::Timer;

/// Resources for one handler call.
///
/// C# exchanges the event ID for this class before its first suspension.
/// The exchange removes the entry from the transfer registry. `BoltFFI` then
/// owns the class and releases it when C# disposes the handle.
pub struct NativeEvent {
    context: Mutex<Option<Context>>,
    payload: EventPayload,
}

enum EventPayload {
    Message(Message),
    Excise(ExciseMessage),
    Timer(Timer),
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl NativeEvent {
    pub(crate) fn message(context: Context, message: Message) -> Self {
        Self {
            context: Mutex::new(Some(context)),
            payload: EventPayload::Message(message),
        }
    }

    pub(crate) fn excise(context: Context, message: ExciseMessage) -> Self {
        Self {
            context: Mutex::new(Some(context)),
            payload: EventPayload::Excise(message),
        }
    }

    pub(crate) fn timer(context: Context, timer: Timer) -> Self {
        Self {
            context: Mutex::new(Some(context)),
            payload: EventPayload::Timer(timer),
        }
    }

    /// Takes the event context.
    ///
    /// # Errors
    ///
    /// Returns a transient error if the context was already taken.
    pub fn take_context(&self) -> Result<Context, FfiError> {
        self.context
            .lock()
            .take()
            .ok_or_else(|| FfiError::TransientState("event context was already taken".to_owned()))
    }

    /// Returns the Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a transient error if this is not a message event.
    pub fn message_value(&self) -> Result<Message, FfiError> {
        match &self.payload {
            EventPayload::Message(message) => Ok(message.clone()),
            EventPayload::Excise(_) | EventPayload::Timer(_) => Err(FfiError::TransientState(
                "event does not contain a Kafka message".to_owned(),
            )),
        }
    }

    /// Returns the excise message.
    ///
    /// # Errors
    ///
    /// Returns a transient error if this is not an excise event.
    pub fn excise_value(&self) -> Result<ExciseMessage, FfiError> {
        match &self.payload {
            EventPayload::Excise(message) => Ok(message.clone()),
            EventPayload::Message(_) | EventPayload::Timer(_) => Err(FfiError::TransientState(
                "event does not contain an excise message".to_owned(),
            )),
        }
    }

    /// Returns the timer.
    ///
    /// # Errors
    ///
    /// Returns a transient error if this is not a timer event.
    pub fn timer_value(&self) -> Result<Timer, FfiError> {
        match &self.payload {
            EventPayload::Timer(timer) => Ok(timer.clone()),
            EventPayload::Message(_) | EventPayload::Excise(_) => Err(FfiError::TransientState(
                "event does not contain a timer".to_owned(),
            )),
        }
    }
}

/// A bounded transfer table for in-flight callback resources.
pub(crate) struct EventRegistry {
    entries: HashMap<u64, NativeEvent, RandomState>,
    next_id: AtomicU64,
    active: AtomicUsize,
    capacity: usize,
}

/// Removes a transfer entry if C# does not take it.
pub(crate) struct EventTicket<'registry> {
    registry: &'registry EventRegistry,
    id: u64,
}

impl EventTicket<'_> {
    pub(crate) const fn id(&self) -> u64 {
        self.id
    }
}

impl Drop for EventTicket<'_> {
    fn drop(&mut self) {
        self.registry.remove(self.id);
    }
}

impl EventRegistry {
    pub(crate) fn new(capacity: usize) -> Self {
        Self {
            entries: HashMap::with_capacity_and_hasher(capacity, RandomState::new()),
            next_id: AtomicU64::new(0),
            active: AtomicUsize::new(0),
            capacity,
        }
    }

    pub(crate) fn insert(&self, event: NativeEvent) -> Result<EventTicket<'_>, FfiError> {
        self.active
            .fetch_update(Ordering::Relaxed, Ordering::Relaxed, |active| {
                (active < self.capacity).then_some(active + 1)
            })
            .map_err(|_| {
                FfiError::TransientState("event transfer capacity is exhausted".to_owned())
            })?;

        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        if self.entries.insert_sync(id, event).is_err() {
            self.active.fetch_sub(1, Ordering::Relaxed);
            return Err(FfiError::TransientState(
                "event transfer ID collision".to_owned(),
            ));
        }
        Ok(EventTicket { registry: self, id })
    }

    pub(crate) fn take(&self, id: u64) -> Result<NativeEvent, FfiError> {
        let event = self
            .entries
            .remove_sync(&id)
            .map(|(_, event)| event)
            .ok_or_else(|| {
                FfiError::TransientState("event transfer ID is not active".to_owned())
            })?;
        self.active.fetch_sub(1, Ordering::Relaxed);
        Ok(event)
    }

    pub(crate) fn remove(&self, id: u64) {
        if self.entries.remove_sync(&id).is_some() {
            self.active.fetch_sub(1, Ordering::Relaxed);
        }
    }
}
