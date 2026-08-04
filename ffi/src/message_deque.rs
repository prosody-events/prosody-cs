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
use crate::state::{OwnedCarrier, ScanDirection, platform_index};

/// A Kafka-message deque state handle for one event.
#[derive(uniffi::Object)]
pub struct MessageDequeStateHandle {
    pub(crate) state: BoxDequeState<ConsumerMessage<BinaryPayload>>,
    pub(crate) propagator: Arc<TextMapCompositePropagator>,
}

#[uniffi::export(async_runtime = "tokio")]
impl MessageDequeStateHandle {
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

    /// Reads the Kafka message at `index`.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn get(
        &self,
        index: u64,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Arc<Message>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .get(platform_index(index)?)
            .with_context(context)
            .await
            .map(|item| item.map(|message| Arc::new(Message::new(message))))
            .map_err(FfiError::from)
    }

    /// Appends one Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn push_back(
        &self,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .push_back(message.consumer_message())
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Prepends one Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the write fails.
    pub async fn push_front(
        &self,
        message: Arc<Message>,
        carrier: HashMap<String, String>,
    ) -> Result<(), FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .push_front(message.consumer_message())
            .with_context(context)
            .await
            .map_err(FfiError::from)
    }

    /// Removes and returns the front Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Arc<Message>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .pop_front()
            .with_context(context)
            .await
            .map(|item| item.map(|message| Arc::new(Message::new(message))))
            .map_err(FfiError::from)
    }

    /// Removes and returns the back Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the operation fails.
    pub async fn pop_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Arc<Message>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .pop_back()
            .with_context(context)
            .await
            .map(|item| item.map(|message| Arc::new(Message::new(message))))
            .map_err(FfiError::from)
    }

    /// Reads the front Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_front(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Arc<Message>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .peek_front()
            .with_context(context)
            .await
            .map(|item| item.map(|message| Arc::new(Message::new(message))))
            .map_err(FfiError::from)
    }

    /// Reads the back Kafka message.
    ///
    /// # Errors
    ///
    /// Returns a state error if the read fails.
    pub async fn peek_back(
        &self,
        carrier: HashMap<String, String>,
    ) -> Result<Option<Arc<Message>>, FfiError> {
        let context = self.propagator.extract(&carrier);
        self.state
            .peek_back()
            .with_context(context)
            .await
            .map(|item| item.map(|message| Arc::new(Message::new(message))))
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
    ) -> Arc<MessageDequeCursor> {
        let context = OwnedCarrier::new(carrier).into_context(&self.propagator);
        let _guard = context.attach();
        Arc::new(MessageDequeCursor {
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
