//! FFI-safe Kafka message wrapper.
//!
//! This module provides [`Message`], a wrapper around prosody's
//! [`ConsumerMessage`] that exposes message data through UniFFI-exported
//! methods for C# consumption.

use std::time::SystemTime;

use prosody::codec::BinaryPayload;
use prosody::consumer::Keyed;
use prosody::consumer::message::{ConsumerMessage, Record};

/// A Kafka message received from a consumer.
///
/// Wraps prosody's [`ConsumerMessage`] and exposes message metadata and payload
/// through FFI-safe accessor methods. The payload bytes are copied verbatim
/// from the wire by [`JsonBinaryCodec`] when the message is decoded. Each
/// accessor clones once into the FFI return buffer as required by `UniFFI`.
///
/// [`JsonBinaryCodec`]: prosody::codec::JsonBinaryCodec
#[derive(uniffi::Object)]
pub struct Message {
    /// The underlying prosody message.
    inner: ConsumerMessage<BinaryPayload>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
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
    pub fn payload(&self) -> Option<Vec<u8>> {
        match self.inner.record() {
            Record::Message(payload) => Some(payload.bytes.clone()),
            Record::Excise => None,
        }
    }
}
