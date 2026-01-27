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
//!
//! Future modules (to be added):
//! - `types`: FFI type definitions (`FFIMessage`, `FFITimer`, `FFIClientOptions`)
//! - `service`: `ProsodyClientService` implementation
//! - `context`: Event context wrapper
//! - `handler`: C# handler callback bridge
//! - `admin`: Admin client operations
//! - `logging`: Logging bridge to C# `ILogger`

use interoptopus::{extra_type, pattern};
use interoptopus::inventory::Inventory;
use std::sync::LazyLock;
use tokio::runtime::Runtime;

pub mod error;
pub mod runtime;
pub mod service;
pub mod types;

// Re-export key types for convenience
pub use error::{CSharpHandlerError, FFIErrorCode};
pub use runtime::GlobalTokio;
pub use service::ProsodyClientService;
pub use types::{ConsumerState, FFIClientOptions, FFIMessage, FFITimer};

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
        .register(extra_type!(FFIErrorCode))
        .register(extra_type!(ConsumerState))
        .register(pattern!(ProsodyClientService))
        // Future additions:
        // - Callback functions via function!(...)
        // - Context service via pattern!(Context)
        // - AdminClientService via pattern!(AdminClientService)
        .validate()
        .build()
}
