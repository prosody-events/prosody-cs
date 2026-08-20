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
use crate::state::{into_bytes, into_message, reject_null, traced};

/// A JSON single-value state handle for one event.
pub struct JsonValueStateHandle {
    pub(crate) name: String,
    pub(crate) state: BoxValueState<BinaryPayload>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl JsonValueStateHandle {
    /// Reads the current JSON document bytes.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(&self, carrier: HashMap<String, String>) -> Result<Option<Vec<u8>>, FfiError> {
        traced(&self.propagator, carrier, self.state.get())
            .await
            .map(into_bytes)
    }

    /// Buffers a JSON document write.
    ///
    /// # Errors
    ///
    /// Returns a state error if the document is `null` or the write fails.
    pub async fn set(
        &self,
        bytes: Vec<u8>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let payload = BinaryPayload::new(bytes, None::<String>, None::<String>);
        reject_null(&payload, &self.name, "; use ClearAsync to remove the value")?;
        traced(&self.propagator, carrier, self.state.set(payload)).await
    }

    /// Clears the current value.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.clear()).await
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

/// A Kafka-message single-value state handle for one event.
pub struct MessageValueStateHandle {
    pub(crate) state: BoxValueState<ConsumerMessage<BinaryPayload>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl MessageValueStateHandle {
    /// Reads the current Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(&self, carrier: HashMap<String, String>) -> Result<Option<Message>, FfiError> {
        traced(&self.propagator, carrier, self.state.get())
            .await
            .map(|item| item.map(into_message))
    }

    /// Buffers a Kafka message write.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn set(
        &self,
        message: Message,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.state.set(message.consumer_message()),
        )
        .await
    }

    /// Clears the current value.
    ///
    /// # Errors
    ///
    /// Returns a state error if the clear fails.
    pub async fn clear(&self, carrier: HashMap<String, String>) -> Result<(), FfiError> {
        traced(&self.propagator, carrier, self.state.clear()).await
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
