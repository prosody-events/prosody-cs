//! FFI-safe Kafka message wrapper.
//!
//! This module provides [`Message`], a wrapper around prosody's
//! [`ConsumerMessage`] that exposes message data through UniFFI-exported
//! methods for C# consumption.

use std::time::SystemTime;

use prosody::codec::BinaryPayload;
use prosody::consumer::Keyed;
use prosody::consumer::message::ConsumerMessage;

/// A Kafka message received from a consumer.
///
/// Each accessor clones its value once into the FFI return buffer.
#[derive(uniffi::Object)]
pub struct Message {
    /// The underlying prosody message.
    inner: ConsumerMessage<BinaryPayload>,
}

/// A Kafka excise record received from a consumer.
#[derive(uniffi::Object)]
pub struct ExciseMessage {
    inner: ConsumerMessage<()>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
)]
impl Message {
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

#[uniffi::export]
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

    /// The source system that produced this message, when its headers name
    /// one.
    #[must_use]
    pub fn source_system(&self) -> Option<String> {
        self.inner.source_system().map(ToString::to_string)
    }

    /// Whether the sender waits for a response to this message.
    #[must_use]
    pub fn is_response_requested(&self) -> bool {
        self.inner.response_requested()
    }
}

impl From<ConsumerMessage<BinaryPayload>> for Message {
    fn from(inner: ConsumerMessage<BinaryPayload>) -> Self {
        Self { inner }
    }
}

impl From<ConsumerMessage<()>> for ExciseMessage {
    fn from(inner: ConsumerMessage<()>) -> Self {
        Self { inner }
    }
}

#[uniffi::export]
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

    /// The source system that produced this record, when its headers name
    /// one.
    #[must_use]
    pub fn source_system(&self) -> Option<String> {
        self.inner.source_system().map(ToString::to_string)
    }

    /// Whether the sender waits for a response to this record.
    #[must_use]
    pub fn is_response_requested(&self) -> bool {
        self.inner.response_requested()
    }
}
