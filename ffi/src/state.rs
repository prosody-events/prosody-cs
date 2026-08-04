//! Shared keyed-state types and validation.

use std::collections::HashMap;

use opentelemetry::Context;
use opentelemetry::propagation::{TextMapCompositePropagator, TextMapPropagator};
use prosody::codec::{BinaryPayload, ErasedStateCodec};
use prosody::state::Direction;

use crate::error::FfiError;

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

/// A carrier consumed while its OpenTelemetry context is extracted.
///
/// The owned wrapper keeps synchronous scan methods compatible with the
/// required by-value FFI argument without a lint exception.
pub(crate) struct OwnedCarrier(HashMap<String, String>);

impl OwnedCarrier {
    /// Creates an owned carrier.
    pub(crate) fn new(carrier: HashMap<String, String>) -> Self {
        Self(carrier)
    }

    /// Extracts the context and consumes the carrier.
    pub(crate) fn into_context(self, propagator: &TextMapCompositePropagator) -> Context {
        propagator.extract(&self.0)
    }
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
