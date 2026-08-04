//! Read-only published-state handles.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::TextMapCompositePropagator;
use prosody::codec::JsonBinaryCodec;
use prosody::high_level::erased::{
    ErasedDequeReader, ErasedDirection, ErasedMapReader, ErasedValueReader,
};

use crate::cursor::{JsonDequeCursor, JsonMapCursor, MapKeyCursor};
use crate::error::FfiError;
use crate::map::JsonMapValue;
use crate::state::{ScanDirection, into_bytes, platform_index, traced};

fn direction(direction: ScanDirection) -> ErasedDirection {
    match direction {
        ScanDirection::Forward => ErasedDirection::Forward,
        ScanDirection::Backward => ErasedDirection::Backward,
    }
}

#[derive(uniffi::Object)]
/// Reads a published value collection.
pub struct PublishedValueHandle {
    pub(crate) reader: Arc<dyn ErasedValueReader<JsonBinaryCodec>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl PublishedValueHandle {
    /// Reads the committed value for a user key.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn get(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        traced(&self.propagator, carrier, self.reader.get(key))
            .await
            .map(into_bytes)
    }
}

#[derive(uniffi::Object)]
/// Reads a published map collection.
pub struct PublishedMapHandle {
    pub(crate) reader: Arc<dyn ErasedMapReader<JsonBinaryCodec>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl PublishedMapHandle {
    /// Reads one committed map entry.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn get(
        &self,
        key: String,
        map_key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        traced(&self.propagator, carrier, self.reader.get(key, map_key))
            .await
            .map(into_bytes)
    }

    /// Reads several committed map entries in one batch.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the batch fails.
    pub async fn get_many(
        &self,
        key: String,
        map_keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<JsonMapValue>, FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.reader.get_many(key, map_keys),
        )
        .await
        .map(|values| {
            values
                .into_iter()
                .map(|value| JsonMapValue {
                    bytes: into_bytes(value),
                })
                .collect()
        })
    }

    /// Reports whether a committed map entry exists.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn contains_key(
        &self,
        key: String,
        map_key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.reader.contains_key(key, map_key),
        )
        .await
    }

    /// Opens an ordered map cursor.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the cursor cannot be opened.
    pub async fn scan(
        &self,
        key: String,
        direction_value: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> Result<Arc<JsonMapCursor>, FfiError> {
        let cursor = traced(
            &self.propagator,
            carrier,
            self.reader.stream(key, direction(direction_value)),
        )
        .await?;
        Ok(Arc::new(JsonMapCursor {
            cursor,
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Opens an ordered key-only map cursor.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the cursor cannot be opened.
    pub async fn keys(
        &self,
        key: String,
        direction_value: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> Result<Arc<MapKeyCursor>, FfiError> {
        let cursor = traced(
            &self.propagator,
            carrier,
            self.reader.keys(key, direction(direction_value)),
        )
        .await?;
        Ok(Arc::new(MapKeyCursor {
            cursor,
            propagator: Arc::clone(&self.propagator),
        }))
    }
}

#[derive(uniffi::Object)]
/// Reads a published deque collection.
pub struct PublishedDequeHandle {
    pub(crate) reader: Arc<dyn ErasedDequeReader<JsonBinaryCodec>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl PublishedDequeHandle {
    /// Reads one committed deque element.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn get(
        &self,
        key: String,
        index: u64,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        let index = platform_index(index)?;
        traced(&self.propagator, carrier, self.reader.get(key, index))
            .await
            .map(into_bytes)
    }

    /// Returns the committed deque length.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn len(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<u64, FfiError> {
        traced(&self.propagator, carrier, self.reader.len(key))
            .await
            .map(|length| length as u64)
    }

    /// Reports whether the committed deque is empty.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn is_empty(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.reader.is_empty(key)).await
    }

    /// Reads the committed front element.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn peek_front(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        traced(&self.propagator, carrier, self.reader.peek_front(key))
            .await
            .map(into_bytes)
    }

    /// Reads the committed back element.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn peek_back(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        traced(&self.propagator, carrier, self.reader.peek_back(key))
            .await
            .map(into_bytes)
    }

    /// Opens an ordered deque cursor.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the cursor cannot be opened.
    pub async fn scan(
        &self,
        key: String,
        direction_value: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> Result<Arc<JsonDequeCursor>, FfiError> {
        let cursor = traced(
            &self.propagator,
            carrier,
            self.reader.stream(key, direction(direction_value)),
        )
        .await?;
        Ok(Arc::new(JsonDequeCursor {
            cursor,
            propagator: Arc::clone(&self.propagator),
        }))
    }
}
