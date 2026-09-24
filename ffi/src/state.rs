//! Shared keyed-state types and validation.

use std::collections::HashMap;
use std::future::Future;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::FutureExt;
use prosody::codec::{BinaryPayload, ErasedStateCodec};
use prosody::consumer::message::ConsumerMessage;
use prosody::state::{Direction, StoreOutcome as CoreStoreOutcome};

use crate::error::FfiError;
use crate::message::Message;

/// The direction of a collection scan.
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum ScanDirection {
    /// Scans in ascending key or index order.
    Forward,
    /// Scans in descending key or index order.
    Backward,
}

impl From<ScanDirection> for Direction {
    fn from(direction: ScanDirection) -> Self {
        match direction {
            ScanDirection::Forward => Direction::Forward,
            ScanDirection::Backward => Direction::Backward,
        }
    }
}

/// The effect of a commit or a rollback.
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum StoreOutcome {
    /// The call wrote or discarded buffered operations.
    Applied,
    /// Nothing was buffered.
    NoOp,
}

impl From<CoreStoreOutcome> for StoreOutcome {
    fn from(outcome: CoreStoreOutcome) -> Self {
        match outcome {
            CoreStoreOutcome::Applied => Self::Applied,
            CoreStoreOutcome::NoOp => Self::NoOp,
        }
    }
}

/// Runs one state operation with the caller's trace context.
pub(crate) async fn traced<T, E>(
    propagator: &TextMapCompositePropagator,
    carrier: HashMap<String, String>,
    operation: impl Future<Output = Result<T, E>>,
) -> Result<T, FfiError>
where
    E: Into<FfiError>,
{
    operation
        .with_context(propagator.extract(&carrier))
        .await
        .map_err(Into::into)
}

/// Returns the bytes from an optional binary payload.
pub(crate) fn into_bytes(payload: Option<BinaryPayload>) -> Option<Vec<u8>> {
    payload.map(|payload| payload.bytes)
}

/// Wraps one resolved Kafka message for FFI.
pub(crate) fn into_message(message: ConsumerMessage<BinaryPayload>) -> Arc<Message> {
    Arc::new(Message::new(message))
}

/// Converts an FFI deque index to the platform index type.
pub(crate) fn platform_index(index: u64) -> Result<usize, FfiError> {
    usize::try_from(index)
        .map_err(|_| FfiError::TransientState("index exceeds platform range".to_owned()))
}

/// Rejects a JSON `null` document before it reaches the state codec.
pub(crate) fn reject_null(
    payload: &BinaryPayload,
    collection: &str,
    advice: &str,
) -> Result<(), FfiError> {
    if payload.is_absent_sentinel() {
        return Err(FfiError::TransientState(format!(
            "collection {collection:?}: JSON null is not a storable value{advice}"
        )));
    }
    Ok(())
}
