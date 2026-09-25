//! Read-only published-state handles.

use std::collections::HashMap;
use std::sync::Arc;
use std::time::Duration;

use opentelemetry::propagation::TextMapCompositePropagator;
use prosody::codec::BinaryPayload;
use prosody::high_level::erased::{
    ErasedReadCache, SharedDequeReader, SharedMapReader, SharedSetReader, SharedValueReader,
};
use prosody::propagator::new_propagator;

use crate::cursor::{JsonDequeCursor, JsonMapCursor, KeyCursor};
use crate::error::FfiError;
use crate::map::JsonMapValue;
use crate::query::{KeyQuery, PositionQuery};
use crate::runtime::run;
use crate::state::{into_bytes, platform_index, traced};

#[derive(uniffi::Object)]
/// Reads a published value collection.
pub struct PublishedValueHandle {
    reader: SharedValueReader<BinaryPayload>,
    propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl PublishedValueHandle {
    /// Reads the committed value for a user key.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn get(
        self: Arc<Self>,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.reader.get(key))
                .await
                .map(into_bytes)
        })
        .await
    }
}

#[derive(uniffi::Object)]
/// Reads a published map collection.
pub struct PublishedMapHandle {
    reader: SharedMapReader<BinaryPayload>,
    propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl PublishedMapHandle {
    /// Reads one committed map entry.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn get(
        self: Arc<Self>,
        key: String,
        map_key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.reader.get(key, map_key))
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Reads several committed map entries in one batch.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the batch fails.
    pub async fn get_many(
        self: Arc<Self>,
        key: String,
        map_keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<JsonMapValue>, FfiError> {
        run(async move {
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
        })
        .await
    }

    /// Reports whether a committed map entry exists.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn contains_key(
        self: Arc<Self>,
        key: String,
        map_key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        run(async move {
            traced(
                &self.propagator,
                carrier,
                self.reader.contains_key(key, map_key),
            )
            .await
        })
        .await
    }

    /// Reports whether each committed map entry exists, in request order.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the batch fails.
    pub async fn contains_many(
        self: Arc<Self>,
        key: String,
        map_keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        run(async move {
            traced(
                &self.propagator,
                carrier,
                self.reader.contains_many(key, map_keys),
            )
            .await
        })
        .await
    }

    /// Reports whether the committed map has no entries.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn is_empty(
        self: Arc<Self>,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        run(async move { traced(&self.propagator, carrier, self.reader.is_empty(key)).await }).await
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
    reader: SharedSetReader,
    propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl PublishedSetHandle {
    /// Reports whether a committed member exists.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn contains(
        self: Arc<Self>,
        key: String,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.reader.contains(key, member)).await
        })
        .await
    }

    /// Reports whether each committed member exists, in request order.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the batch fails.
    pub async fn contains_many(
        self: Arc<Self>,
        key: String,
        members: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        run(async move {
            traced(
                &self.propagator,
                carrier,
                self.reader.contains_many(key, members),
            )
            .await
        })
        .await
    }

    /// Reports whether the committed set has no members.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn is_empty(
        self: Arc<Self>,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        run(async move { traced(&self.propagator, carrier, self.reader.is_empty(key)).await }).await
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
    reader: SharedDequeReader<BinaryPayload>,
    propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl PublishedDequeHandle {
    /// Reads one committed deque element.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn get(
        self: Arc<Self>,
        key: String,
        index: u64,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            let index = platform_index(index)?;
            traced(&self.propagator, carrier, self.reader.get(key, index))
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Returns the committed deque length.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn len(
        self: Arc<Self>,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<u64, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.reader.len(key))
                .await
                .map(|length| length as u64)
        })
        .await
    }

    /// Reports whether the committed deque is empty.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn is_empty(
        self: Arc<Self>,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        run(async move { traced(&self.propagator, carrier, self.reader.is_empty(key)).await }).await
    }

    /// Reads the committed front element.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn peek_front(
        self: Arc<Self>,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.reader.peek_front(key))
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Reads the committed back element.
    ///
    /// # Errors
    ///
    /// Returns a categorized state error when the read fails.
    pub async fn peek_back(
        self: Arc<Self>,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.reader.peek_back(key))
                .await
                .map(into_bytes)
        })
        .await
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

/// Chooses the read cache for a published reader.
///
/// Returns a permanent state error when the caller sets both a TTL and
/// `disabled`.
pub(crate) fn read_cache(
    ttl: Option<Duration>,
    disabled: bool,
) -> Result<ErasedReadCache, FfiError> {
    match (ttl, disabled) {
        (Some(_), true) => Err(FfiError::PermanentState(
            "read cache cannot set both a TTL and disabled".to_owned(),
        )),
        (None, true) => Ok(ErasedReadCache::Disabled),
        (Some(ttl), false) => Ok(ErasedReadCache::Ttl(ttl)),
        (None, false) => Ok(ErasedReadCache::Inherit),
    }
}

/// Wraps a published value reader for FFI.
pub(crate) fn value_handle(reader: SharedValueReader<BinaryPayload>) -> Arc<PublishedValueHandle> {
    let propagator = Arc::new(new_propagator());
    Arc::new(PublishedValueHandle { reader, propagator })
}

/// Wraps a published map reader for FFI.
pub(crate) fn map_handle(reader: SharedMapReader<BinaryPayload>) -> Arc<PublishedMapHandle> {
    let propagator = Arc::new(new_propagator());
    Arc::new(PublishedMapHandle { reader, propagator })
}

/// Wraps a published set reader for FFI.
pub(crate) fn set_handle(reader: SharedSetReader) -> Arc<PublishedSetHandle> {
    let propagator = Arc::new(new_propagator());
    Arc::new(PublishedSetHandle { reader, propagator })
}

/// Wraps a published deque reader for FFI.
pub(crate) fn deque_handle(reader: SharedDequeReader<BinaryPayload>) -> Arc<PublishedDequeHandle> {
    let propagator = Arc::new(new_propagator());
    Arc::new(PublishedDequeHandle { reader, propagator })
}
