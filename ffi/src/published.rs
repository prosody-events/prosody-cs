//! Read-only published-state handles.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::TextMapCompositePropagator;
use prosody::codec::BinaryPayload;
use prosody::high_level::erased::{
    SharedDequeReader, SharedMapReader, SharedSetReader, SharedValueReader,
};

use crate::cursor::{JsonDequeCursor, JsonMapCursor, KeyCursor};
use crate::error::FfiError;
use crate::map::JsonMapValue;
use crate::query::{KeyQuery, PositionQuery};
use crate::state::{into_bytes, platform_index, traced};

#[derive(uniffi::Object)]
/// Reads a published value collection.
pub struct PublishedValueHandle {
    pub(crate) reader: SharedValueReader<BinaryPayload>,
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
    pub(crate) reader: SharedMapReader<BinaryPayload>,
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

    /// Reports whether each committed map entry exists, in request order.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the batch fails.
    pub async fn contains_many(
        &self,
        key: String,
        map_keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.reader.contains_many(key, map_keys),
        )
        .await
    }

    /// Reports whether the committed map has no entries.
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

    /// Opens a cursor over the entries that `query` selects.
    ///
    /// The cursor reads nothing until its first pull.
    ///
    /// # Errors
    ///
    /// Returns a state error if the query limit is zero.
    pub fn entries(&self, key: String, query: KeyQuery) -> Result<Arc<JsonMapCursor>, FfiError> {
        Ok(Arc::new(JsonMapCursor {
            cursor: self
                .reader
                .entries(key)
                .with_query(query.try_into()?)
                .stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Opens a key-only cursor over the entries that `query` selects.
    ///
    /// The cursor reads nothing until its first pull.
    ///
    /// # Errors
    ///
    /// Returns a state error if the query limit is zero.
    pub fn keys(&self, key: String, query: KeyQuery) -> Result<Arc<KeyCursor>, FfiError> {
        Ok(Arc::new(KeyCursor {
            cursor: self.reader.keys(key).with_query(query.try_into()?).stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }
}

#[derive(uniffi::Object)]
/// Reads a published set collection.
pub struct PublishedSetHandle {
    pub(crate) reader: SharedSetReader,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl PublishedSetHandle {
    /// Reports whether a committed member exists.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn contains(
        &self,
        key: String,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.reader.contains(key, member)).await
    }

    /// Reports whether each committed member exists, in request order.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the batch fails.
    pub async fn contains_many(
        &self,
        key: String,
        members: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.reader.contains_many(key, members),
        )
        .await
    }

    /// Reports whether the committed set has no members.
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

    /// Opens a cursor over the members that `query` selects.
    ///
    /// The cursor reads nothing until its first pull.
    ///
    /// # Errors
    ///
    /// Returns a state error if the query limit is zero.
    pub fn keys(&self, key: String, query: KeyQuery) -> Result<Arc<KeyCursor>, FfiError> {
        Ok(Arc::new(KeyCursor {
            cursor: self.reader.keys(key).with_query(query.try_into()?).stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }
}

#[derive(uniffi::Object)]
/// Reads a published deque collection.
pub struct PublishedDequeHandle {
    pub(crate) reader: SharedDequeReader<BinaryPayload>,
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

    /// Opens a cursor over the elements that `query` selects.
    ///
    /// The cursor reads nothing until its first pull.
    ///
    /// # Errors
    ///
    /// Returns a state error if a position exceeds the platform range or the
    /// query limit is zero.
    pub fn values(
        &self,
        key: String,
        query: PositionQuery,
    ) -> Result<Arc<JsonDequeCursor>, FfiError> {
        Ok(Arc::new(JsonDequeCursor {
            cursor: self
                .reader
                .values(key)
                .with_query(query.try_into()?)
                .stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }
}
