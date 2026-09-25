//! JSON deque state handle.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxDequeState;

use crate::cursor::JsonDequeCursor;
use crate::error::FfiError;
use crate::query::PositionQuery;
use crate::runtime::run;
use crate::state::{StoreOutcome, into_bytes, platform_index, reject_null, traced};

/// A JSON deque state handle for one event.
#[derive(uniffi::Object)]
pub struct JsonDequeStateHandle {
    pub(crate) name: String,
    pub(crate) state: BoxDequeState<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl JsonDequeStateHandle {
    /// Returns the live element count.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn len(self: Arc<Self>, carrier: HashMap<String, String>) -> Result<u64, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.len())
                .await
                .map(|length| length as u64)
        })
        .await
    }

    /// Reports whether the deque has no live elements.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn is_empty(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.is_empty()).await }).await
    }

    /// Reads the JSON document bytes at `index`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        self: Arc<Self>,
        index: u64,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(
                &self.propagator,
                carrier,
                self.state.get(platform_index(index)?),
            )
            .await
            .map(into_bytes)
        })
        .await
    }

    /// Appends one JSON document.
    ///
    /// # Errors
    ///
    /// Returns a state error if the document is `null` or the write fails.
    pub async fn push_back(
        self: Arc<Self>,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
            reject_null(&payload, &self.name, " in a deque")?;
            traced(&self.propagator, carrier, self.state.push_back(payload)).await
        })
        .await
    }

    /// Prepends one JSON document.
    ///
    /// # Errors
    ///
    /// Returns a state error if the document is `null` or the write fails.
    pub async fn push_front(
        self: Arc<Self>,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
            reject_null(&payload, &self.name, " in a deque")?;
            traced(&self.propagator, carrier, self.state.push_front(payload)).await
        })
        .await
    }

    /// Removes and returns the front JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_front(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.pop_front())
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Removes and returns the back JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_back(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.pop_back())
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Reads the front JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_front(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.peek_front())
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Reads the back JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_back(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.peek_back())
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Removes every element.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(self: Arc<Self>, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.clear()).await }).await
    }

    /// Opens a cursor over the live elements that `query` selects.
    ///
    /// # Errors
    ///
    /// Returns a state error if a position exceeds the platform range or the
    /// query limit is zero.
    pub fn values(&self, query: PositionQuery) -> Result<Arc<JsonDequeCursor>, FfiError> {
        Ok(Arc::new(JsonDequeCursor {
            cursor: self.state.values().with_query(query.try_into()?).stream(),
            propagator: Arc::clone(&self.propagator),
        }))
    }

    /// Commits the buffered operations and reports whether any existed.
    ///
    /// # Errors
    ///
    /// Returns a state error if the commit fails.
    pub async fn commit(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<StoreOutcome, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.commit())
                .await
                .map(StoreOutcome::from)
        })
        .await
    }

    /// Discards the buffered operations and reports whether any existed.
    pub async fn rollback(self: Arc<Self>, carrier: HashMap<String, String>) -> StoreOutcome {
        run(async move {
            let context = self.propagator.extract(&carrier);
            self.state.rollback().with_context(context).await.into()
        })
        .await
    }
}
