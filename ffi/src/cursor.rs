//! Typed cursors for keyed-state scans.
//!
//! Keep concrete cursor types explicit because macros hide the exported
//! `BoltFFI` surface.

use std::collections::HashMap;
use std::num::NonZeroUsize;
use std::sync::Arc;

use opentelemetry::propagation::TextMapCompositePropagator;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxStateCursor;
use prosody::consumer::message::ConsumerMessage;

use crate::error::FfiError;
use crate::message::MessageBatch;
use crate::state::{into_message, traced};

/// Maximum number of immediately-ready scan items in one FFI vector.
///
/// Core owns ready draining, error ordering, and pull serialization. This
/// binding owns only the transport cap and conversion.
const READY_CHUNK_SIZE: NonZeroUsize = match NonZeroUsize::new(256) {
    Some(size) => size,
    None => NonZeroUsize::MIN,
};

/// One JSON map entry.
#[boltffi::data]
#[derive(Debug, Clone)]
pub struct JsonMapEntry {
    /// The entry key.
    pub key: String,
    /// The JSON document bytes.
    pub bytes: Vec<u8>,
}

/// Scans JSON deque elements.
pub struct JsonDequeCursor {
    pub(crate) cursor: BoxStateCursor<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
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
        traced(
            &self.propagator,
            carrier,
            self.cursor.next_ready_chunk(READY_CHUNK_SIZE),
        )
        .await
        .map(|chunk| chunk.map(|items| items.into_iter().map(|payload| payload.bytes).collect()))
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans JSON map entries.
pub struct JsonMapCursor {
    pub(crate) cursor: BoxStateCursor<(String, BinaryPayload)>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
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
        traced(
            &self.propagator,
            carrier,
            self.cursor.next_ready_chunk(READY_CHUNK_SIZE),
        )
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
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans Kafka-message deque elements.
pub struct MessageDequeCursor {
    pub(crate) cursor: BoxStateCursor<ConsumerMessage<BinaryPayload>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
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
    ) -> Result<Option<MessageBatch>, FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.cursor.next_ready_chunk(READY_CHUNK_SIZE),
        )
        .await
        .map(|chunk| {
            chunk.map(|items| MessageBatch::messages(items.into_iter().map(into_message).collect()))
        })
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans Kafka-message map entries.
pub struct MessageMapCursor {
    pub(crate) cursor: BoxStateCursor<(String, ConsumerMessage<BinaryPayload>)>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
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
    ) -> Result<Option<MessageBatch>, FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.cursor.next_ready_chunk(READY_CHUNK_SIZE),
        )
        .await
        .map(|chunk| {
            chunk.map(|items| {
                MessageBatch::entries(
                    items
                        .into_iter()
                        .map(|(key, message)| (key, into_message(message)))
                        .collect(),
                )
            })
        })
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}

/// Scans map keys without reading values.
pub struct MapKeyCursor {
    pub(crate) cursor: BoxStateCursor<String>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
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
        traced(
            &self.propagator,
            carrier,
            self.cursor.next_ready_chunk(READY_CHUNK_SIZE),
        )
        .await
    }

    /// Closes the cursor.
    pub async fn close(&self) {
        self.cursor.close().await;
    }
}
