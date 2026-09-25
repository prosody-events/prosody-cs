//! Set state handle.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::consumer::event_context::BoxSetState;

use crate::cursor::KeyCursor;
use crate::error::FfiError;
use crate::query::KeyQuery;
use crate::runtime::run;
use crate::state::{StoreOutcome, traced};

/// A set state handle for one event. A set stores ordered string members.
#[derive(uniffi::Object)]
pub struct SetStateHandle {
    pub(crate) state: BoxSetState,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl SetStateHandle {
    /// Reports whether `member` is in the set.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains(
        self: Arc<Self>,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<bool, FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.contains(member)).await })
            .await
    }

    /// Reports whether each member is in the set, in request order.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn contains_many(
        self: Arc<Self>,
        members: Vec<String>,
        carrier: HashMap<String, String>,
    ) -> Result<Vec<bool>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.contains_many(members)).await
        })
        .await
    }

    /// Reports whether the set has no members.
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

    /// Adds `member` to the set.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn insert(
        self: Arc<Self>,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.insert(member)).await }).await
    }

    /// Removes `member` from the set. An absent member is a no-op.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn remove(
        self: Arc<Self>,
        member: String,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.remove(member)).await }).await
    }

    /// Removes every member.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(self: Arc<Self>, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.clear()).await }).await
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
