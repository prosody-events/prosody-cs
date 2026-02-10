//! Cooperative cancellation signaling for async operations.
//!
//! This module provides [`CancellationSignal`], a thread-safe mechanism for
//! signaling cancellation from C# to Rust async operations. The pattern follows
//! cooperative cancellation: the caller requests cancellation, and the async
//! operation checks for that request at appropriate points.

use tokio_util::sync::CancellationToken;

/// A thread-safe signal for cooperative cancellation of async operations.
///
/// Created by C# code and passed to Rust async methods. The C# caller can
/// invoke [`cancel`](Self::cancel) at any time to request cancellation, and
/// Rust code uses [`cancelled`](Self::cancelled) to await that signal.
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

#[uniffi::export(async_runtime = "tokio")]
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
    /// Typically used in a `tokio::select!` branch to abort work when
    /// cancellation is requested.
    pub async fn cancelled(&self) {
        self.token.cancelled().await;
    }
}
