//! Kafka-message deque state handle.

use std::collections::HashMap;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::BinaryPayload;
use prosody::consumer::event_context::BoxDequeState;
use prosody::consumer::message::ConsumerMessage;

use crate::cursor::MessageDequeCursor;
use crate::error::FfiError;
use crate::message::Message;
use crate::state::{OwnedCarrier, ScanDirection, into_message, platform_index, traced};

/// A Kafka-message deque state handle for one event.
pub struct MessageDequeStateHandle {
    pub(crate) state: BoxDequeState<ConsumerMessage<BinaryPayload>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl MessageDequeStateHandle {
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

    /// Reads the Kafka message at `index`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        &self,
        index: u64,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Message>, FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.state.get(platform_index(index)?),
        )
        .await
        .map(|item| item.map(into_message))
    }

    /// Appends one Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn push_back(
        &self,
        message: Message,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.state.push_back(message.consumer_message()),
        )
        .await
    }

    /// Prepends one Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn push_front(
        &self,
        message: Message,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        traced(
            &self.propagator,
            carrier,
            self.state.push_front(message.consumer_message()),
        )
        .await
    }

    /// Removes and returns the front Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Message>, FfiError> {
        traced(&self.propagator, carrier, self.state.pop_front())
            .await
            .map(|item| item.map(into_message))
    }

    /// Removes and returns the back Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Message>, FfiError> {
        traced(&self.propagator, carrier, self.state.pop_back())
            .await
            .map(|item| item.map(into_message))
    }

    /// Reads the front Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Message>, FfiError> {
        traced(&self.propagator, carrier, self.state.peek_front())
            .await
            .map(|item| item.map(into_message))
    }

    /// Reads the back Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Message>, FfiError> {
        traced(&self.propagator, carrier, self.state.peek_back())
            .await
            .map(|item| item.map(into_message))
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
    ) -> MessageDequeCursor {
        let context = OwnedCarrier::new(carrier).into_context(&self.propagator);
        let _guard = context.attach();
        MessageDequeCursor {
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
