//! JSON deque state handle.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxDequeState;

use crate::cursor::JsonDequeCursor;
use crate::error::FfiError;
use crate::state::{OwnedCarrier, ScanDirection, platform_index, reject_null};

/// A JSON deque state handle for one event.
#[derive(uniffi::Object)]
pub struct JsonDequeStateHandle {
    pub(crate) name: String,
    pub(crate) state: BoxDequeState<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl JsonDequeStateHandle {
    /// Returns the live element count.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn len(&self, carrier: HashMap<String, String>) -> Result<u64, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .len()
            .with_context(context)
            .await
            .map(|length| length as u64)
            .map_err(FfiError::from)
    }

    /// Reports whether the deque has no live elements.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn is_empty(&self, carrier: HashMap<String, String>) -> Result<bool, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .is_empty()
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Reads the JSON document bytes at `index`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        &self,
        index: u64,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .get(platform_index(index)?)
            .with_context(context)
            .await
            .map(|item| item.map(|payload| payload.bytes))
            .map_err(FfiError::from)
    }

    /// Appends one JSON document.
    ///
    /// # Errors
    ///
    /// Returns a state error if the document is `null` or the write fails.
    pub async fn push_back(
        &self,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
        reject_null(&payload, &self.name, " in a deque")?;
        let context = self.propagator.extract(&carrier);
        self.state
            .push_back(payload)
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Prepends one JSON document.
    ///
    /// # Errors
    ///
    /// Returns a state error if the document is `null` or the write fails.
    pub async fn push_front(
        &self,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
        reject_null(&payload, &self.name, " in a deque")?;
        let context = self.propagator.extract(&carrier);
        self.state
            .push_front(payload)
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Removes and returns the front JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .pop_front()
            .with_context(context)
            .await
            .map(|item| item.map(|payload| payload.bytes))
            .map_err(FfiError::from)
    }

    /// Removes and returns the back JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .pop_back()
            .with_context(context)
            .await
            .map(|item| item.map(|payload| payload.bytes))
            .map_err(FfiError::from)
    }

    /// Reads the front JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .peek_front()
            .with_context(context)
            .await
            .map(|item| item.map(|payload| payload.bytes))
            .map_err(FfiError::from)
    }

    /// Reads the back JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .peek_back()
            .with_context(context)
            .await
            .map(|item| item.map(|payload| payload.bytes))
            .map_err(FfiError::from)
    }

    /// Removes every element.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .clear()
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Opens a cursor over live elements.
    #[must_use]
    pub fn scan(
        &self,
        direction: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> Arc<JsonDequeCursor> {
        let context = OwnedCarrier::new(carrier).into_context(&self.propagator);
        let _guard = context.attach();
        Arc::new(JsonDequeCursor {
            cursor: self.state.scan(direction.into()),
            propagator: Arc::clone(&self.propagator),
        })
    }

    /// Commits the buffered operations.
    ///
    /// # Errors
    ///
    /// Returns a state error if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .commit()
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Discards the buffered operations.
    pub async fn rollback(&self, carrier: HashMap<String, String>) {
        let context = self.propagator.extract(&carrier);
        self.state.rollback().with_context(context).await;
    }
}
