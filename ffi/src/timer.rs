//! Timer trigger wrapper for C# handler invocation.

use std::time::SystemTime;

use prosody::timers::Trigger;

/// Timer trigger data.
///
/// Wraps prosody's `Trigger` and exposes timer data via methods.
/// This matches the Python `Timer` dataclass.
#[derive(uniffi::Object)]
pub struct Timer {
    trigger: Trigger,
    key: String,
}

#[expect(
    clippy::multiple_inherent_impl,
    reason = "UniFFI requires separate impl blocks for exported vs internal methods"
)]
impl Timer {
    /// Creates a new Timer wrapping a Trigger.
    #[must_use]
    pub fn new(trigger: Trigger) -> Self {
        let key = trigger.key.to_string();
        Self { trigger, key }
    }
}

#[uniffi::export]
impl Timer {
    /// Returns the timer key.
    #[must_use]
    pub fn key(&self) -> String {
        self.key.clone()
    }

    /// Returns the timer fire time.
    #[must_use]
    pub fn time(&self) -> SystemTime {
        self.trigger.time.into()
    }
}
