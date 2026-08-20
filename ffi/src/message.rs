//! FFI-safe Kafka message wrapper.
//!
//! This module provides [`Message`], a wrapper around prosody's
//! [`ConsumerMessage`] that exposes message data through BoltFFI-exported
//! methods for C# consumption.

use std::time::SystemTime;

use prosody::codec::BinaryPayload;
use prosody::consumer::Keyed;
use prosody::consumer::message::ConsumerMessage;

use crate::error::FfiError;

/// A Kafka message received from a consumer.
///
/// Wraps prosody's [`ConsumerMessage`] and exposes message metadata and payload
/// through FFI-safe accessor methods. The payload bytes are copied verbatim
/// from the wire by [`JsonBinaryMessageCodec`] when the message is decoded.
/// Each accessor clones once into the FFI return buffer as required by
/// `BoltFFI`.
///
/// [`JsonBinaryMessageCodec`]: prosody::codec::JsonBinaryMessageCodec
#[derive(Clone)]
pub struct Message {
    /// The underlying prosody message.
    inner: ConsumerMessage<BinaryPayload>,
}

/// A Kafka excise record received from a consumer.
#[derive(Clone)]
pub struct ExciseMessage {
    inner: ConsumerMessage<()>,
}

/// A bounded group of resolved messages.
pub struct MessageBatch {
    items: Vec<MessageBatchItem>,
}

enum MessageBatchItem {
    Missing,
    Message(Message),
    Entry(String, Message),
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl MessageBatch {
    pub(crate) fn messages(items: Vec<Message>) -> Self {
        Self {
            items: items.into_iter().map(MessageBatchItem::Message).collect(),
        }
    }

    pub(crate) fn optional(items: Vec<Option<Message>>) -> Self {
        Self {
            items: items
                .into_iter()
                .map(|item| item.map_or(MessageBatchItem::Missing, MessageBatchItem::Message))
                .collect(),
        }
    }

    pub(crate) fn entries(items: Vec<(String, Message)>) -> Self {
        Self {
            items: items
                .into_iter()
                .map(|(key, message)| MessageBatchItem::Entry(key, message))
                .collect(),
        }
    }

    /// Returns the number of batch slots.
    #[must_use]
    pub fn count(&self) -> u64 {
        self.items.len() as u64
    }

    /// Returns the message at `index`, or `None` for a missing slot.
    ///
    /// # Errors
    ///
    /// Returns a transient error if the index is outside the batch.
    pub fn message_at(&self, index: u64) -> Result<Option<Message>, FfiError> {
        let index = usize::try_from(index).map_err(|_| {
            FfiError::TransientState("batch index exceeds platform range".to_owned())
        })?;
        self.items
            .get(index)
            .map(|item| match item {
                MessageBatchItem::Missing => None,
                MessageBatchItem::Message(message) | MessageBatchItem::Entry(_, message) => {
                    Some(message.clone())
                }
            })
            .ok_or_else(|| FfiError::TransientState("batch index is out of range".to_owned()))
    }

    /// Returns the map key at `index`.
    ///
    /// # Errors
    ///
    /// Returns a transient error if the slot is not a map entry.
    pub fn key_at(&self, index: u64) -> Result<String, FfiError> {
        let index = usize::try_from(index).map_err(|_| {
            FfiError::TransientState("batch index exceeds platform range".to_owned())
        })?;
        match self.items.get(index) {
            Some(MessageBatchItem::Entry(key, _)) => Ok(key.clone()),
            Some(MessageBatchItem::Missing | MessageBatchItem::Message(_)) => Err(
                FfiError::TransientState("batch slot has no map key".to_owned()),
            ),
            None => Err(FfiError::TransientState(
                "batch index is out of range".to_owned(),
            )),
        }
    }
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "BoltFFI requires separate impl blocks for exported vs internal methods"
)]
impl Message {
    /// Creates a new `Message` from a [`ConsumerMessage`].
    #[must_use]
    pub fn new(inner: ConsumerMessage<BinaryPayload>) -> Self {
        Self { inner }
    }

    /// Clones the wrapped consumer message for a keyed-state message write.
    ///
    /// [`ConsumerMessage`] is cheaply cloneable (it shares its value and
    /// processing state through `Arc`), so a message-collection write clones
    /// the inner message rather than reconstructing it field by field.
    #[must_use]
    pub(crate) fn consumer_message(&self) -> ConsumerMessage<BinaryPayload> {
        self.inner.clone()
    }
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl Message {
    /// The Kafka topic this message was consumed from.
    #[must_use]
    pub fn topic(&self) -> String {
        self.inner.topic().to_string()
    }

    /// The partition number within the topic.
    #[must_use]
    pub fn partition(&self) -> i32 {
        self.inner.partition()
    }

    /// The offset of this message within its partition.
    #[must_use]
    pub fn offset(&self) -> i64 {
        self.inner.offset()
    }

    /// The timestamp when the message was produced.
    #[must_use]
    pub fn timestamp(&self) -> SystemTime {
        (*self.inner.timestamp()).into()
    }

    /// The message key used for partitioning.
    #[must_use]
    pub fn key(&self) -> String {
        self.inner.key().to_string()
    }

    /// The message payload as raw bytes copied verbatim from the wire.
    #[must_use]
    pub fn payload(&self) -> Vec<u8> {
        self.inner.payload().bytes.clone()
    }
}

impl From<ConsumerMessage<BinaryPayload>> for Message {
    fn from(inner: ConsumerMessage<BinaryPayload>) -> Self {
        Self::new(inner)
    }
}

impl From<ConsumerMessage<()>> for ExciseMessage {
    fn from(inner: ConsumerMessage<()>) -> Self {
        Self { inner }
    }
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl ExciseMessage {
    /// The Kafka topic this record was consumed from.
    #[must_use]
    pub fn topic(&self) -> String {
        self.inner.topic().to_string()
    }

    /// The partition number within the topic.
    #[must_use]
    pub fn partition(&self) -> i32 {
        self.inner.partition()
    }

    /// The offset of this record within its partition.
    #[must_use]
    pub fn offset(&self) -> i64 {
        self.inner.offset()
    }

    /// The timestamp when the record was produced.
    #[must_use]
    pub fn timestamp(&self) -> SystemTime {
        (*self.inner.timestamp()).into()
    }

    /// The key that this record excises.
    #[must_use]
    pub fn key(&self) -> String {
        self.inner.key().to_string()
    }
}
