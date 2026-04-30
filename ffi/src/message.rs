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
/// Wraps prosody's [`ConsumerMessage`] and exposes message metadata and payload
/// through FFI-safe accessor methods. The payload bytes are copied verbatim
/// from the wire by [`JsonBinaryCodec`] and cached at construction time to
/// avoid repeated cloning on each accessor call.
///
/// [`JsonBinaryCodec`]: prosody::codec::JsonBinaryCodec
#[derive(uniffi::Object)]
pub struct Message {
    /// The underlying prosody message.
    inner: ConsumerMessage<BinaryPayload>,
    /// Cached topic name to avoid repeated allocation.
    topic: String,
    /// Cached message key to avoid repeated allocation.
    key: String,
    /// Cached payload bytes for repeated FFI access.
    payload: Vec<u8>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
)]
impl Message {
    /// Creates a new `Message` from a [`ConsumerMessage`].
    ///
    /// Caches the topic, key, and payload bytes for efficient repeated access.
    #[must_use]
    pub fn new(inner: ConsumerMessage<BinaryPayload>) -> Self {
        let topic = inner.topic().to_string();
        let key = inner.key().to_string();
        // The payload sits behind an `Arc` shared with retry middleware,
        // which clones the message to re-dispatch on transient failure.
        // UniFFI's `Lower for Vec<T>` consumes an owned `Vec` and copies
        // it byte-by-byte into a fresh `RustBuffer`, so we must produce
        // an owned `Vec<u8>` here regardless.
        let payload = inner.payload().bytes.clone();
        Self {
            inner,
            topic,
            key,
            payload,
        }
    }
}

#[uniffi::export]
impl Message {
    /// The Kafka topic this message was consumed from.
    #[must_use]
    pub fn topic(&self) -> String {
        self.topic.clone()
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
        self.key.clone()
    }

    /// The message payload as raw bytes copied verbatim from the wire.
    #[must_use]
    pub fn payload(&self) -> Vec<u8> {
        self.payload.clone()
    }
}
