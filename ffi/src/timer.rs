//! Timer trigger wrapper for FFI export.
//!
//! Provides a [`Timer`] type that wraps prosody's internal [`Trigger`] and
//! exposes timer data through BoltFFI-exported methods for use in C# callbacks.

use std::time::SystemTime;

use prosody::timers::Trigger;

/// A scheduled timer trigger.
///
/// Wraps prosody's [`Trigger`] and exposes timer metadata through
/// BoltFFI-exported methods. Each timer has a unique key and a scheduled
/// fire time.
#[derive(Clone)]
pub struct Timer {
    trigger: Trigger,
    key: String,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "BoltFFI requires separate impl blocks for exported vs internal methods"
)]
impl Timer {
    /// Creates a new `Timer` from a [`Trigger`].
    ///
    /// Extracts and caches the key as a `String` for efficient repeated access.
    #[must_use]
    pub fn new(trigger: Trigger) -> Self {
        let key = trigger.key.to_string();
        Self { trigger, key }
    }
}

#[prosody_ffi_macros::ffi_async]
#[boltffi::export]
impl Timer {
    /// The unique identifier for this timer.
    #[must_use]
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// The scheduled time when this timer should fire.
    #[must_use]
    pub fn time(&self) -> SystemTime {
        self.trigger.time.into()
    }
}
