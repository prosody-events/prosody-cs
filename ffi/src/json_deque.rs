//! JSON deque state handle.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxDequeState;

use crate::cursor::JsonDequeCursor;
use crate::error::FfiError;
use crate::state::{OwnedCarrier, ScanDirection, into_bytes, platform_index, reject_null, traced};

/// A JSON deque state handle for one event.
pub struct JsonDequeStateHandle {
    pub(crate) name: String,
    pub(crate) state: BoxDequeState<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl JsonDequeStateHandle {
    /// Returns the live element count.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn len(&self, carrier: HashMap<String, String>) -> Result<u64, FfiError> {
        traced(&self.propagator, carrier, self.state.len())
            .await
            .map(|length| length as u64)
    }

    /// Reports whether the deque has no live elements.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn is_empty(&self, carrier: HashMap<String, String>) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.state.is_empty()).await
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
        traced(
            &self.propagator,
            carrier,
            self.state.get(platform_index(index)?),
        )
        .await
        .map(into_bytes)
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
        traced(&self.propagator, carrier, self.state.push_back(payload)).await
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
        traced(&self.propagator, carrier, self.state.push_front(payload)).await
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
        traced(&self.propagator, carrier, self.state.pop_front())
            .await
            .map(into_bytes)
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
        traced(&self.propagator, carrier, self.state.pop_back())
            .await
            .map(into_bytes)
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
        traced(&self.propagator, carrier, self.state.peek_front())
            .await
            .map(into_bytes)
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
        traced(&self.propagator, carrier, self.state.peek_back())
            .await
            .map(into_bytes)
    }

    /// Removes every element.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.clear()).await
    }

    /// Opens a cursor over live elements.
    #[must_use]
    pub fn scan(
        &self,
        direction: ScanDirection,
        carrier: HashMap<String, String>,
    ) -> JsonDequeCursor {
        let context = OwnedCarrier::new(carrier).into_context(&self.propagator);
        let _guard = context.attach();
        JsonDequeCursor {
            cursor: self.state.scan(direction.into()),
            propagator: Arc::clone(&self.propagator),
        }
    }

    /// Commits the buffered operations.
    ///
    /// # Errors
    ///
    /// Returns a state error if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.commit()).await
    }

    /// Discards the buffered operations.
    pub async fn rollback(&self, carrier: HashMap<String, String>) {
        let context = self.propagator.extract(&carrier);
        self.state.rollback().with_context(context).await;
    }
}
