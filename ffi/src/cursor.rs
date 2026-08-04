//! Typed cursors for keyed-state scans.

use std::collections::HashMap;
use std::num::NonZeroUsize;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxStateCursor;
use prosody::consumer::message::ConsumerMessage;

use crate::error::FfiError;
use crate::message::Message;

/// Maximum number of immediately-ready scan items in one FFI vector.
///
/// Core owns ready draining, error ordering, and pull serialization. This
/// binding owns only the transport cap and conversion.
const READY_CHUNK_SIZE: NonZeroUsize = match NonZeroUsize::new(256) {
    Some(size) => size,
    None => NonZeroUsize::MIN,
};

/// One JSON map entry.
#[derive(uniffi::Record)]
pub struct JsonMapEntry {
    /// The entry key.
    pub key: String,
    /// The JSON document bytes.
    pub bytes: Vec<u8>,
}

/// One Kafka-message map entry.
#[derive(uniffi::Record)]
pub struct MessageMapEntry {
    /// The entry key.
    pub key: String,
    /// The resolved Kafka message.
    pub message: Arc<Message>,
}

/// Scans JSON deque elements.
#[derive(uniffi::Object)]
pub struct JsonDequeCursor {
    pub(crate) cursor: BoxStateCursor<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl JsonDequeCursor {
    /// Pulls the next immediately-ready chunk.
    ///
    /// Returns `None` after the scan ends.
    ///
    /// # Errors
    ///
    /// Returns a state error if the pull fails or the cursor is closed.
    pub async fn next_chunk(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<Vec<u8>>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.cursor
            .next_ready_chunk(READY_CHUNK_SIZE)
            .with_context(context)
            .await
            .map(|chunk| {
                chunk.map(|items| items.into_iter().map(|payload| payload.bytes).collect())
            })
            .map_err(FfiError::from)
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans JSON map entries.
#[derive(uniffi::Object)]
pub struct JsonMapCursor {
    pub(crate) cursor: BoxStateCursor<(String, BinaryPayload)>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl JsonMapCursor {
    /// Pulls the next immediately-ready chunk.
    ///
    /// Returns `None` after the scan ends.
    ///
    /// # Errors
    ///
    /// Returns a state error if the pull fails or the cursor is closed.
    pub async fn next_chunk(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<JsonMapEntry>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.cursor
            .next_ready_chunk(READY_CHUNK_SIZE)
            .with_context(context)
            .await
            .map(|chunk| {
                chunk.map(|items| {
                    items
                        .into_iter()
                        .map(|(key, payload)| JsonMapEntry {
                            key,
                            bytes: payload.bytes,
                        })
                        .collect()
                })
            })
            .map_err(FfiError::from)
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans Kafka-message deque elements.
#[derive(uniffi::Object)]
pub struct MessageDequeCursor {
    pub(crate) cursor: BoxStateCursor<ConsumerMessage<BinaryPayload>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl MessageDequeCursor {
    /// Pulls the next immediately-ready chunk.
    ///
    /// Returns `None` after the scan ends.
    ///
    /// # Errors
    ///
    /// Returns a state error if the pull fails or the cursor is closed.
    pub async fn next_chunk(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<Arc<Message>>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.cursor
            .next_ready_chunk(READY_CHUNK_SIZE)
            .with_context(context)
            .await
            .map(|chunk| {
                chunk.map(|items| {
                    items
                        .into_iter()
                        .map(|message| Arc::new(Message::new(message)))
                        .collect()
                })
            })
            .map_err(FfiError::from)
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans Kafka-message map entries.
#[derive(uniffi::Object)]
pub struct MessageMapCursor {
    pub(crate) cursor: BoxStateCursor<(String, ConsumerMessage<BinaryPayload>)>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl MessageMapCursor {
    /// Pulls the next immediately-ready chunk.
    ///
    /// Returns `None` after the scan ends.
    ///
    /// # Errors
    ///
    /// Returns a state error if the pull fails or the cursor is closed.
    pub async fn next_chunk(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<MessageMapEntry>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.cursor
            .next_ready_chunk(READY_CHUNK_SIZE)
            .with_context(context)
            .await
            .map(|chunk| {
                chunk.map(|items| {
                    items
                        .into_iter()
                        .map(|(key, message)| MessageMapEntry {
                            key,
                            message: Arc::new(Message::new(message)),
                        })
                        .collect()
                })
            })
            .map_err(FfiError::from)
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans map keys without reading values.
#[derive(uniffi::Object)]
pub struct MapKeyCursor {
    pub(crate) cursor: BoxStateCursor<String>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl MapKeyCursor {
    /// Pulls the next immediately-ready chunk.
    ///
    /// Returns `None` after the scan ends.
    ///
    /// # Errors
    ///
    /// Returns a state error if the pull fails or the cursor is closed.
    pub async fn next_chunk(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<String>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.cursor
            .next_ready_chunk(READY_CHUNK_SIZE)
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}
