//! Request arguments and outcomes at the FFI boundary.

use std::collections::HashMap;

use prosody::codec::BinaryPayload;
use prosody::requester::{ResponseError, SubsystemOutcomes};
use prosody::subsystem::SubsystemName;

use crate::error::FfiError;
use crate::handler::NativeRequestResult;

/// Parses the subsystem names that a request targets.
///
/// Returns a permanent state error for the first invalid name.
pub(super) fn subsystem_names(names: Vec<String>) -> Result<Vec<SubsystemName>, FfiError> {
    names
        .into_iter()
        .map(SubsystemName::try_new)
        .collect::<Result<_, _>>()
        .map_err(|error| FfiError::PermanentState(error.to_string()))
}

/// Converts each subsystem outcome to its FFI result, keyed by subsystem name.
pub(super) fn native_request_results(
    outcomes: SubsystemOutcomes<BinaryPayload>,
) -> HashMap<String, NativeRequestResult> {
    outcomes
        .into_iter()
        .map(|(subsystem, result)| (subsystem.to_string(), native_request_result(result)))
        .collect()
}

fn native_request_result(result: Result<BinaryPayload, ResponseError>) -> NativeRequestResult {
    match result {
        Ok(value) => NativeRequestResult::Ok { value: value.bytes },
        Err(ResponseError::Handler { message }) => NativeRequestResult::HandlerError { message },
        Err(ResponseError::Timeout) => NativeRequestResult::Timeout {
            message: ResponseError::Timeout.to_string(),
        },
        Err(ResponseError::FormatMismatch) => NativeRequestResult::FormatMismatch {
            message: ResponseError::FormatMismatch.to_string(),
        },
        Err(ResponseError::Malformed) => NativeRequestResult::Malformed {
            message: ResponseError::Malformed.to_string(),
        },
    }
}
