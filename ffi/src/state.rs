//! Shared keyed-state types, validation, and trace helpers.

use std::collections::HashMap;
use std::future::Future;
use std::sync::Arc;

use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use opentelemetry::trace::{FutureExt, WithContext};
use prosody::codec::BinaryPayload;
use prosody::consumer::message::ConsumerMessage;
use prosody::state::{Direction, StoreOutcome as CoreStoreOutcome};
use tracing::{Span, debug};
use tracing_opentelemetry::OpenTelemetrySpanExt;

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

/// Runs `operation` in the caller's trace context.
pub(crate) fn in_context<F: Future>(
    propagator: &TextMapCompositePropagator,
    carrier: &HashMap<String, String>,
    operation: F,
) -> WithContext<F> {
    operation.with_context(propagator.extract(carrier))
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
    in_context(propagator, &carrier, operation)
        .await
        .map_err(Into::into)
}

/// Makes the caller's trace context the parent of `span` and returns `span`.
///
/// A failure to set the parent is logged at debug level. The span stays usable.
pub(crate) fn with_parent(
    span: Span,
    propagator: &TextMapCompositePropagator,
    carrier: &HashMap<String, String>,
) -> Span {
    if let Err(error) = span.set_parent(propagator.extract(carrier)) {
        debug!("failed to set parent span: {error:#}");
    }
    span
}

/// Returns the bytes from an optional binary payload.
pub(crate) fn into_bytes(payload: Option<BinaryPayload>) -> Option<Vec<u8>> {
    payload.map(|payload| payload.bytes)
}

/// Wraps one resolved Kafka message for FFI.
pub(crate) fn into_message(message: ConsumerMessage<BinaryPayload>) -> Arc<Message> {
    Arc::new(message.into())
}

/// Converts an FFI deque index to the platform index type.
pub(crate) fn platform_index(index: u64) -> Result<usize, FfiError> {
    usize::try_from(index)
        .map_err(|_| FfiError::TransientState("index exceeds platform range".to_owned()))
}
