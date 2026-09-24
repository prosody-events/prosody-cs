//! Set state handle.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::consumer::event_context::BoxSetState;

use crate::cursor::KeyCursor;
use crate::error::FfiError;
use crate::query::KeyQuery;
use crate::state::{StoreOutcome, traced};

/// A set state handle for one event. A set stores ordered string members.
#[derive(uniffi::Object)]
pub struct SetStateHandle {
    pub(crate) state: BoxSetState,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl SetStateHandle {
    /// Reports whether `member` is in the set.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains(
        &self,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.state.contains(member)).await
    }

    /// Reports whether each member is in the set, in request order.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains_many(
        &self,
        members: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        traced(&self.propagator, carrier, self.state.contains_many(members)).await
    }

    /// Reports whether the set has no members.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn is_empty(&self, carrier: HashMap<String, String>) -> Result<bool, FfiError> {
        traced(&self.propagator, carrier, self.state.is_empty()).await
    }

    /// Adds `member` to the set.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn insert(
        &self,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.insert(member)).await
    }

    /// Removes `member` from the set. An absent member is a no-op.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn remove(
        &self,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.remove(member)).await
    }

    /// Removes every member.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.clear()).await
    }

    /// Opens a cursor over the members that `query` selects.
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

    /// Commits the buffered operations and reports whether any existed.
    ///
    /// # Errors
    ///
    /// Returns a state error if the commit fails.
    pub async fn commit(&self, carrier: HashMap<String, String>) -> Result<StoreOutcome, FfiError> {
        traced(&self.propagator, carrier, self.state.commit())
            .await
            .map(StoreOutcome::from)
    }

    /// Discards the buffered operations and reports whether any existed.
    pub async fn rollback(&self, carrier: HashMap<String, String>) -> StoreOutcome {
        let context = self.propagator.extract(&carrier);
        self.state.rollback().with_context(context).await.into()
    }
}
