//! Global Tokio runtime wrapper for Interoptopus async support.
//!
//! This module provides the `GlobalTokio` type that implements the Interoptopus
//! `AsyncRuntime` trait while sharing a single global Tokio runtime across all
//! FFI services. This pattern matches sibling wrappers (prosody-rb, prosody-py,
//! prosody-js).
//!
//! # Why a Global Runtime?
//!
//! | Aspect | Per-Service Runtime | Global Runtime (chosen) |
//! |--------|---------------------|-------------------------|
//! | Resource usage | Multiple thread pools | Single shared pool |
//! | Sibling parity | ❌ Differs | ✅ Matches siblings |
//! | Fork safety | ❌ Each creates threads | ✅ Lazy init after fork |
//! | Interoptopus compat | ✅ Native | ✅ Via `GlobalTokio` wrapper |
//!
//! # Reference
//!
//! - prosody-rb: `ext/prosody/src/lib.rs:43-49` - Global RUNTIME pattern
//! - data-model.md: "Global Runtime Pattern" section

use crate::RUNTIME;
use interoptopus::pattern::asynk::AsyncRuntime;
use std::future::Future;

/// Zero-sized wrapper that implements `AsyncRuntime` using the global `RUNTIME`.
///
/// This allows Interoptopus services to use `#[derive(AsyncRuntime)]` while
/// sharing a single Tokio runtime across all service instances.
///
/// # Usage
///
/// Include a `runtime: GlobalTokio` field in service structs:
///
/// ```rust,ignore
/// #[derive(AsyncRuntime)]
/// #[ffi_type(opaque)]
/// pub struct MyService {
///     runtime: GlobalTokio,  // Zero-sized, references global RUNTIME
///     // ... other fields
/// }
/// ```
///
/// The field is zero-sized and adds no memory overhead.
#[derive(Clone, Copy, Default, Debug)]
pub struct GlobalTokio;

impl AsyncRuntime for GlobalTokio {
    type T = ();

    fn spawn<Fn, F>(&self, f: Fn)
    where
        Fn: FnOnce(Self::T) -> F,
        F: Future<Output = ()> + Send + 'static,
    {
        RUNTIME.spawn(f(()));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::Arc;
    use std::thread;
    use std::time::Duration;

    #[test]
    fn global_tokio_is_zero_sized() {
        assert_eq!(size_of::<GlobalTokio>(), 0);
    }

    #[test]
    fn global_tokio_is_copy() {
        let a = GlobalTokio;
        let b = a;
        let _ = (a, b); // Both are valid - GlobalTokio is Copy
    }

    #[test]
    fn global_tokio_implements_default() {
        fn assert_default<T: Default>() {}
        assert_default::<GlobalTokio>();
    }

    #[test]
    fn global_tokio_spawn_executes_future() {
        let flag = Arc::new(AtomicBool::new(false));
        let flag_clone = Arc::clone(&flag);

        let rt = GlobalTokio;
        rt.spawn(move |()| async move {
            flag_clone.store(true, Ordering::SeqCst);
        });

        // Give the spawned task time to execute
        thread::sleep(Duration::from_millis(50));

        assert!(flag.load(Ordering::SeqCst), "spawned future should have executed");
    }
}
