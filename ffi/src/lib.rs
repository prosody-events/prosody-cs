//! Prosody FFI bindings for C#.
//!
//! This crate provides FFI bindings for the Prosody Kafka client library,
//! using Interoptopus for automatic C# bindings generation.
//!
//! ## Crate Structure
//!
//! - `ffi/` (this crate): FFI type definitions and `ffi_inventory()` (rlib only)
//! - `ffi-cdylib/`: Thin wrapper that produces the cdylib (.dylib/.so/.dll)
//! - `ffi-build/`: Contains build.rs that generates C# bindings
//!
//! This three-crate pattern avoids Cargo filename collisions that occur when a
//! crate with `crate-type = ["cdylib", "rlib"]` is also used as a build-dependency.
//! See: <https://github.com/rust-lang/cargo/issues/6313>
//!
//! ## Building
//!
//! ```bash
//! # Build the cdylib (produces libprosody_ffi.dylib/.so/.dll)
//! cargo build -p prosody-ffi-cdylib
//!
//! # Generate C# bindings (via build.rs)
//! cargo build -p prosody-ffi-build
//! ```
//!
//! ## Module Organization
//!
//! - [`error`]: Error types for FFI boundary crossing
//! - [`runtime`]: Global Tokio runtime wrapper for async support
//! - [`types`]: FFI type definitions (`FFIClientOptions`, `ConsumerState`)
//! - [`callbacks`]: C# handler callbacks and `CSharpHandler` implementation
//! - [`events`]: Opaque event types for async C# handler invocation
//! - [`service`]: `ProsodyClientService` implementation
//!
//! Future modules (to be added):
//! - `context`: Event context wrapper
//! - `admin`: Admin client operations
//! - `logging`: Logging bridge to C# `ILogger`

use interoptopus::inventory::Inventory;
use interoptopus::{extra_type, function, pattern};
use std::sync::LazyLock;
use tokio::runtime::Runtime;

pub mod callbacks;
pub mod error;
pub mod events;
pub mod runtime;
pub mod service;
pub mod types;

// Re-export key types for convenience
pub use callbacks::{
    CSharpHandler, HandlerCallbacks, HandlerResultCode, OnMessageCallback, OnShutdownCallback,
    OnTimerCallback,
};
pub use error::{CSharpHandlerError, FFIErrorCode};
pub use events::{
    message_event_await_cancel, message_event_complete, message_event_key, message_event_offset,
    message_event_partition, message_event_payload, message_event_should_cancel,
    message_event_timestamp, message_event_topic, timer_event_await_cancel, timer_event_complete,
    timer_event_key, timer_event_should_cancel, timer_event_time, MessageEvent, TimerEvent,
};
pub use runtime::GlobalTokio;
pub use service::ProsodyClientService;
pub use types::{ConsumerState, FFIClientOptions};

/// Global Tokio runtime for all async operations.
///
/// This runtime powers all async operations in the extension, including
/// message processing, scheduling, and communication with C#.
/// Lazily initialized on first use - critical for fork safety.
///
/// Reference: prosody-rb/ext/prosody/src/lib.rs:43-49
#[expect(clippy::expect_used, reason = "Runtime initialization failure is unrecoverable - library cannot function without it")]
pub static RUNTIME: LazyLock<Runtime> =
    LazyLock::new(|| Runtime::new().expect("Failed to create Tokio runtime"));

/// Returns the FFI inventory containing all exported types and functions.
///
/// This function is called by the `ffi-build` crate's build.rs to generate
/// C# bindings using Interoptopus.
///
/// The inventory is built using the Interoptopus 0.15 pattern:
/// `Inventory::builder().register(...).validate().inventory()`
///
/// # Registered Items
///
/// ## Types
/// - [`FFIErrorCode`]: Error codes for FFI operations
/// - [`ConsumerState`]: Consumer lifecycle state
///
/// ## Services
/// - [`ProsodyClientService`]: Main client service
/// - `Context`: Event context API (to be added)
/// - `AdminClientService`: Admin operations (to be added)
///
/// # Example
///
/// ```rust,ignore
/// // In ffi-build/build.rs:
/// use prosody_ffi::ffi_inventory;
///
/// let generator = DotNet::new_built().with_config(config);
/// generator.write_file(&ffi_inventory(), "ProsodyFFI.cs");
/// ```
#[must_use]
pub fn ffi_inventory() -> Inventory {
    Inventory::builder()
        // Core types
        .register(extra_type!(FFIErrorCode))
        .register(extra_type!(ConsumerState))
        // Callback types
        .register(extra_type!(HandlerResultCode))
        .register(extra_type!(HandlerCallbacks))
        .register(extra_type!(OnMessageCallback))
        .register(extra_type!(OnTimerCallback))
        .register(extra_type!(OnShutdownCallback))
        // Event types (opaque)
        .register(extra_type!(MessageEvent))
        .register(extra_type!(TimerEvent))
        // MessageEvent FFI functions
        .register(function!(message_event_topic))
        .register(function!(message_event_partition))
        .register(function!(message_event_offset))
        .register(function!(message_event_timestamp))
        .register(function!(message_event_key))
        .register(function!(message_event_payload))
        .register(function!(message_event_complete))
        .register(function!(message_event_should_cancel))
        .register(function!(message_event_await_cancel))
        // TimerEvent FFI functions
        .register(function!(timer_event_key))
        .register(function!(timer_event_time))
        .register(function!(timer_event_complete))
        .register(function!(timer_event_should_cancel))
        .register(function!(timer_event_await_cancel))
        // Services
        .register(pattern!(ProsodyClientService))
        // Future additions:
        // - Context service via pattern!(Context)
        // - AdminClientService via pattern!(AdminClientService)
        .validate()
        .build()
}
