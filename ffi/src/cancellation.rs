//! Cooperative cancellation signaling for async operations.
//!
//! This module provides [`CancellationSignal`], a thread-safe mechanism for
//! signaling cancellation from C# to Rust async operations. The pattern follows
//! cooperative cancellation: the caller requests cancellation, and the async
//! operation checks for that request at appropriate points.

use std::future::Future;
use std::sync::Arc;

use tokio::select;
use tokio_util::sync::CancellationToken;

use crate::error::FfiError;
use crate::runtime::run;

/// A thread-safe signal for cooperative cancellation of async operations.
///
/// Created by C# code and passed to Rust async methods. The C# caller can
/// invoke [`cancel`](Self::cancel) at any time to request cancellation.
/// Rust code passes the signal to `cancellable` to stop its work.
#[derive(uniffi::Object)]
pub struct CancellationSignal {
    token: CancellationToken,
}

/// Delegates to [`CancellationSignal::new`].
impl Default for CancellationSignal {
    fn default() -> Self {
        Self::new()
    }
}

#[uniffi::export]
impl CancellationSignal {
    /// Creates a new cancellation signal in the unsignalled state.
    #[uniffi::constructor]
    #[must_use]
    pub fn new() -> Self {
        Self {
            token: CancellationToken::new(),
        }
    }

    /// Signals cancellation, waking any tasks awaiting
    /// [`cancelled`](Self::cancelled).
    ///
    /// This method is idempotent: calling it multiple times has no additional
    /// effect after the first call.
    pub fn cancel(&self) {
        self.token.cancel();
    }

    /// Waits until cancellation is signaled.
    ///
    /// Returns immediately if [`cancel`](Self::cancel) has already been called.
    pub async fn cancelled(self: Arc<Self>) {
        run(async move {
            self.token.cancelled().await;
        })
        .await;
    }
}

/// Runs `future` until it completes or `cancel` fires.
///
/// Returns [`FfiError::Cancelled`] only when `cancel` fires first. Without a
/// signal, the future runs to completion.
pub(crate) async fn cancellable<F, T, E>(
    cancel: Option<Arc<CancellationSignal>>,
    future: F,
) -> Result<T, FfiError>
where
    F: Future<Output = Result<T, E>>,
    E: Into<FfiError>,
{
    let Some(signal) = cancel else {
        return future.await.map_err(Into::into);
    };

    select! {
        result = future => result.map_err(Into::into),
        () = signal.token.cancelled() => Err(FfiError::Cancelled),
    }
}
