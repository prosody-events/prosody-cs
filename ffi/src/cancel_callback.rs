//! Push-style cancellation callback implemented by the foreign binding.

/// Callback the foreign binding registers to receive cancellation.
///
/// Register an implementation with
/// [`Context::watch_cancel`](crate::context::Context::watch_cancel). The
/// watcher task calls [`cancel`](Self::cancel) at most once when
/// cancellation fires.
#[uniffi::export(with_foreign)]
pub trait CancelCallback: Send + Sync {
    /// Signals cancellation to the foreign binding.
    ///
    /// Called at most once, from a background task.
    fn cancel(&self);
}
