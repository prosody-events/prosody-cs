//! FFI-safe Kafka message wrapper.
//!
//! This module provides [`Message`], a wrapper around prosody's
//! [`ConsumerMessage`] that exposes message data through UniFFI-exported
//! methods for C# consumption.

use std::time::SystemTime;

use prosody::consumer::Keyed;
use prosody::consumer::message::ConsumerMessage;

/// A Kafka message received from a consumer.
///
/// Wraps prosody's [`ConsumerMessage`] and exposes message metadata and payload
/// through FFI-safe accessor methods. The payload is eagerly serialized to JSON
/// bytes at construction time to avoid repeated serialization costs.
#[derive(uniffi::Object)]
pub struct Message {
    /// The underlying prosody message.
    inner: ConsumerMessage,
    /// Cached topic name to avoid repeated allocation.
    topic: String,
    /// Cached message key to avoid repeated allocation.
    key: String,
    /// Pre-serialized JSON payload bytes.
    payload: Vec<u8>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
)]
impl Message {
    /// Creates a new `Message` from a [`ConsumerMessage`].
    ///
    /// Eagerly serializes the message payload to JSON bytes and caches the
    /// topic and key strings for efficient repeated access.
    ///
    /// # Errors
    ///
    /// Returns [`simd_json::Error`] if the payload cannot be serialized to
    /// JSON.
    pub fn new(inner: ConsumerMessage) -> Result<Self, simd_json::Error> {
        let topic = inner.topic().to_string();
        let key = inner.key().to_string();
        // Serialize JSON Value back to UTF-8 bytes using simd_json
        let payload = simd_json::to_vec(inner.payload())?;
        Ok(Self {
            inner,
            topic,
            key,
            payload,
        })
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

    /// The message payload as UTF-8 JSON bytes.
    #[must_use]
    pub fn payload(&self) -> Vec<u8> {
        self.payload.clone()
    }
}
