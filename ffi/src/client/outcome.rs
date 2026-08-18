use super::{BinaryPayload, NativeRequestResult, ResponseError};

pub(super) fn native_request_result(
    result: Result<BinaryPayload, ResponseError>,
) -> NativeRequestResult {
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
