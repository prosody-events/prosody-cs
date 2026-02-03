//! Kafka message wrapper for C# handler invocation.

use std::time::SystemTime;

use prosody::consumer::Keyed;
use prosody::consumer::message::ConsumerMessage;

/// Kafka message data.
///
/// Wraps prosody's `ConsumerMessage` and exposes message data via methods.
/// This matches the Python `Message` dataclass.
#[derive(uniffi::Object)]
pub struct Message {
    inner: ConsumerMessage,
    topic: String,
    key: String,
    payload: Vec<u8>,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs non-exported methods"
)]
impl Message {
    /// Creates a new Message wrapping a [`ConsumerMessage`].
    ///
    /// # Errors
    ///
    /// Returns an error if the payload cannot be serialized to JSON bytes.
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
    /// Returns the topic name.
    #[must_use]
    pub fn topic(&self) -> String {
        self.topic.clone()
    }

    /// Returns the partition number.
    #[must_use]
    pub fn partition(&self) -> i32 {
        self.inner.partition()
    }

    /// Returns the message offset.
    #[must_use]
    pub fn offset(&self) -> i64 {
        self.inner.offset()
    }

    /// Returns the message timestamp.
    #[must_use]
    pub fn timestamp(&self) -> SystemTime {
        (*self.inner.timestamp()).into()
    }

    /// Returns the message key.
    #[must_use]
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// Returns the message payload (UTF-8 JSON bytes).
    #[must_use]
    pub fn payload(&self) -> Vec<u8> {
        self.payload.clone()
    }
}
