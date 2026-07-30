//! Read-only published-state handles.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::JsonBinaryCodec;
use prosody::high_level::erased::{
    ErasedDequeReader, ErasedDirection, ErasedMapReader, ErasedValueReader,
};

use crate::error::FfiError;
use crate::state::{CursorVariant, ScanDirection, StateCursor, StateItem};

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
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.reader
            .get(key)
            .with_context(context)
            .await
            .map(|value| {
                value.map(|payload| StateItem::Json {
                    bytes: payload.bytes,
                })
            })
            .map_err(FfiError::from)
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
    ) -> Result<Option<StateItem>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.reader
            .get(key, map_key)
            .with_context(context)
            .await
            .map(|value| {
                value.map(|payload| StateItem::Json {
                    bytes: payload.bytes,
                })
            })
            .map_err(FfiError::from)
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
    ) -> Result<Vec<Option<StateItem>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.reader
            .get_many(key, map_keys)
            .with_context(context)
            .await
            .map(|values| {
                values
                    .into_iter()
                    .map(|value| {
                        value.map(|payload| StateItem::Json {
                            bytes: payload.bytes,
                        })
                    })
                    .collect()
            })
            .map_err(FfiError::from)
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
    ) -> Result<Arc<StateCursor>, FfiError> {
        let context = self.propagator.extract(&carrier);
        let cursor = self
            .reader
            .stream(key, direction(direction_value))
            .with_context(context)
            .await
            .map_err(FfiError::from)?;
        Ok(Arc::new(StateCursor {
            cursor: CursorVariant::MapJson(cursor),
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
    ) -> Result<Option<StateItem>, FfiError> {
        let index = usize::try_from(index)
            .map_err(|_| FfiError::TransientState("index exceeds platform range".to_owned()))?;
        let context = self.propagator.extract(&carrier);
        self.reader
            .get(key, index)
            .with_context(context)
            .await
            .map(|value| {
                value.map(|payload| StateItem::Json {
                    bytes: payload.bytes,
                })
            })
            .map_err(FfiError::from)
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
        let context = self.propagator.extract(&carrier);
        self.reader
            .len(key)
            .with_context(context)
            .await
            .map(|length| length as u64)
            .map_err(FfiError::from)
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
    ) -> Result<Arc<StateCursor>, FfiError> {
        let context = self.propagator.extract(&carrier);
        let cursor = self
            .reader
            .stream(key, direction(direction_value))
            .with_context(context)
            .await
            .map_err(FfiError::from)?;
        Ok(Arc::new(StateCursor {
            cursor: CursorVariant::DequeJson(cursor),
            propagator: Arc::clone(&self.propagator),
        }))
    }
}
