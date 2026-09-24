//! Typed ordered-map state handles.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxMapState;
use prosody::consumer::message::ConsumerMessage;

use crate::cursor::{JsonMapCursor, KeyCursor, MessageMapCursor};
use crate::error::FfiError;
use crate::message::Message;
use crate::query::KeyQuery;
use crate::state::{StoreOutcome, into_bytes, into_message, reject_null, traced};

/// One optional JSON value from an ordered batch read.
///
/// The C# generator emits `new byte[length][]?` for the nested optional byte
/// vector. This syntax does not compile, so this record keeps the generated
/// type valid.
#[derive(uniffi::Record)]
pub struct JsonMapValue {
    /// The JSON document bytes, or `None` when the key is absent.
    pub bytes: Option<Vec<u8>>,
}

/// A JSON ordered-map state handle for one event.
#[derive(uniffi::Object)]
pub struct JsonMapStateHandle {
    pub(crate) name: String,
    pub(crate) state: BoxMapState<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl JsonMapStateHandle {
    /// Reads the JSON document bytes for `key`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        traced(&self.propagator, carrier, self.state.get(key))
            .await
            .map(into_bytes)
    }

    /// Reads several JSON values in request order.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get_many(
        &self,
        keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<JsonMapValue>, FfiError> {
        traced(&self.propagator, carrier, self.state.get_many(keys))
            .await
            .map(|items| {
                items
                    .into_iter()
                    .map(|item| JsonMapValue {
                        bytes: into_bytes(item),
                    })
                    .collect()
            })
    }

    /// Reports whether `key` exists.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains_key(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.state.contains_key(key)).await
    }

    /// Opens a cursor over the live keys that `query` selects without reading
    /// values.
    ///
    /// # Errors
    ///
    /// Returns a state error if the query limit is zero.
    pub fn keys(&self, query: KeyQuery) -> Result<Arc<KeyCursor>, FfiError> {
        Ok(Arc::new(KeyCursor {
            cursor: self.state.keys().with_query(query.try_into()?).stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Reports whether the map has no live entries.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn is_empty(&self, carrier: HashMap<String, String>) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.state.is_empty()).await
    }

    /// Reports whether each key exists, in request order.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains_many(
        &self,
        keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        traced(&self.propagator, carrier, self.state.contains_many(keys)).await
    }

    /// Inserts or replaces one JSON document.
    ///
    /// # Errors
    ///
    /// Returns a state error if the document is `null` or the write fails.
    pub async fn set(
        &self,
        key: String,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
        reject_null(
            &payload,
            &self.name,
            "; use RemoveAsync to remove the entry",
        )?;
        traced(&self.propagator, carrier, self.state.set(key, payload)).await
    }

    /// Removes `key`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the removal fails.
    pub async fn remove(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.remove(key)).await
    }

    /// Removes every entry.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.clear()).await
    }

    /// Opens a cursor over the live entries that `query` selects.
    ///
    /// # Errors
    ///
    /// Returns a state error if the query limit is zero.
    pub fn entries(&self, query: KeyQuery) -> Result<Arc<JsonMapCursor>, FfiError> {
        Ok(Arc::new(JsonMapCursor {
            cursor: self.state.entries().with_query(query.try_into()?).stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Commits the buffered operations and reports whether any existed.
    ///
    /// # Errors
    ///
    /// Returns a state error if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.commit())
            .await
            .map(|_| ())
    }

    /// Discards the buffered operations.
    pub async fn rollback(&self, carrier: HashMap<String, String>) {
        let context = self.propagator.extract(&carrier);
        self.state.rollback().with_context(context).await;
    }
}

/// A Kafka-message ordered-map state handle for one event.
#[derive(uniffi::Object)]
pub struct MessageMapStateHandle {
    pub(crate) state: BoxMapState<ConsumerMessage<BinaryPayload>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl MessageMapStateHandle {
    /// Reads the Kafka message for `key`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Arc<Message>>, FfiError> {
        traced(&self.propagator, carrier, self.state.get(key))
            .await
            .map(|item| item.map(into_message))
    }

    /// Reads several Kafka messages in request order.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get_many(
        &self,
        keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<Option<Arc<Message>>>, FfiError> {
        traced(&self.propagator, carrier, self.state.get_many(keys))
            .await
            .map(|items| {
                items
                    .into_iter()
                    .map(|item| item.map(into_message))
                    .collect()
            })
    }

    /// Reports whether `key` exists without resolving its message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains_key(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.state.contains_key(key)).await
    }

    /// Opens a cursor over the live keys that `query` selects without resolving
    /// messages.
    ///
    /// # Errors
    ///
    /// Returns a state error if the query limit is zero.
    pub fn keys(&self, query: KeyQuery) -> Result<Arc<KeyCursor>, FfiError> {
        Ok(Arc::new(KeyCursor {
            cursor: self.state.keys().with_query(query.try_into()?).stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Reports whether the map has no live entries.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn is_empty(&self, carrier: HashMap<String, String>) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.state.is_empty()).await
    }

    /// Reports whether each key exists, in request order.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains_many(
        &self,
        keys: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        traced(&self.propagator, carrier, self.state.contains_many(keys)).await
    }

    /// Inserts or replaces one Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn set(
        &self,
        key: String,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.state.set(key, message.consumer_message()),
        )
        .await
    }

    /// Removes `key`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the removal fails.
    pub async fn remove(
        &self,
        key: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.remove(key)).await
    }

    /// Removes every entry.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.clear()).await
    }

    /// Opens a cursor over the live entries that `query` selects.
    ///
    /// # Errors
    ///
    /// Returns a state error if the query limit is zero.
    pub fn entries(&self, query: KeyQuery) -> Result<Arc<MessageMapCursor>, FfiError> {
        Ok(Arc::new(MessageMapCursor {
            cursor: self.state.entries().with_query(query.try_into()?).stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Commits the buffered operations and reports whether any existed.
    ///
    /// # Errors
    ///
    /// Returns a state error if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.commit())
            .await
            .map(|_| ())
    }

    /// Discards the buffered operations.
    pub async fn rollback(&self, carrier: HashMap<String, String>) {
        let context = self.propagator.extract(&carrier);
        self.state.rollback().with_context(context).await;
    }
}
