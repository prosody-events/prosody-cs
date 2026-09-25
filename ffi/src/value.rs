//! Typed single-value state handles.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxValueState;
use prosody::consumer::message::ConsumerMessage;

use crate::error::FfiError;
use crate::message::Message;
use crate::runtime::run;
use crate::state::{StoreOutcome, into_bytes, into_message, reject_null, traced};

/// A JSON single-value state handle for one event.
#[derive(uniffi::Object)]
pub struct JsonValueStateHandle {
    pub(crate) name: String,
    pub(crate) state: BoxValueState<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl JsonValueStateHandle {
    /// Reads the current JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Vec<u8>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.get())
                .await
                .map(into_bytes)
        })
        .await
    }

    /// Buffers a JSON document write.
    ///
    /// # Errors
    ///
    /// Returns a state error if the document is `null` or the write fails.
    pub async fn set(
        self: Arc<Self>,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
            reject_null(&payload, &self.name, "; use ClearAsync to remove the value")?;
            traced(&self.propagator, carrier, self.state.set(payload)).await
        })
        .await
    }

    /// Clears the current value.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(self: Arc<Self>, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.clear()).await }).await
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

/// A Kafka-message single-value state handle for one event.
#[derive(uniffi::Object)]
pub struct MessageValueStateHandle {
    pub(crate) state: BoxValueState<ConsumerMessage<BinaryPayload>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export]
impl MessageValueStateHandle {
    /// Reads the current Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        self: Arc<Self>,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Arc<Message>>, FfiError> {
        run(async move {
            traced(&self.propagator, carrier, self.state.get())
                .await
                .map(|item| item.map(into_message))
        })
        .await
    }

    /// Buffers a Kafka message write.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn set(
        self: Arc<Self>,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        run(async move {
            traced(
                &self.propagator,
                carrier,
                self.state.set(message.consumer_message()),
            )
            .await
        })
        .await
    }

    /// Clears the current value.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(self: Arc<Self>, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        run(async move { traced(&self.propagator, carrier, self.state.clear()).await }).await
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
