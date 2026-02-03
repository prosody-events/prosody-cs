//! Cancellation signaling for async operations.

use tokio_util::sync::CancellationToken;

/// A cancellation signal that can be created by C# and passed to Rust async
/// operations.
///
/// C# creates this object, passes it to an async method, and can call
/// `cancel()` to signal that the operation should be aborted. Rust code awaits
/// `cancelled()` to detect when cancellation has been requested.
#[derive(uniffi::Object)]
pub struct CancellationSignal {
    token: CancellationToken,
}

impl Default for CancellationSignal {
    fn default() -> Self {
        Self::new()
    }
}

#[uniffi::export(async_runtime = "tokio")]
impl CancellationSignal {
    /// Creates a new cancellation signal.
    #[uniffi::constructor]
    #[must_use]
    pub fn new() -> Self {
        Self {
            token: CancellationToken::new(),
        }
    }

    /// Signals cancellation. Any async operation waiting on this signal will be
    /// notified.
    pub fn cancel(&self) {
        self.token.cancel();
    }

    /// Waits until cancellation is signaled.
    ///
    /// This is used internally by Rust async operations to detect cancellation.
    pub async fn cancelled(&self) {
        self.token.cancelled().await;
    }
}
