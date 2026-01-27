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

use interoptopus::extra_type;
use interoptopus::inventory::Inventory;
use std::sync::LazyLock;
use tokio::runtime::Runtime;

// Phase 2: Foundational Infrastructure
pub mod error;
pub mod runtime;

// Re-export key types for convenience
pub use error::{CSharpHandlerError, FFIErrorCode};
pub use runtime::GlobalTokio;

/// Global Tokio runtime for all async operations.
///
/// This runtime powers all async operations in the extension, including
/// message processing, scheduling, and communication with C#.
/// Lazily initialized on first use - critical for fork safety.
///
/// Reference: prosody-rb/ext/prosody/src/lib.rs:43-49
#[allow(clippy::expect_used)]
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
/// ## Types (Phase 3)
/// - [`FFIErrorCode`]: Error codes for FFI operations
///
/// ## Services (Phase 4+)
/// - `ProsodyClientService`: Main client service (to be added)
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
        // Phase 3: Register foundational types
        .register(extra_type!(FFIErrorCode))
        // Future phases will add:
        // - Phase 4: ProsodyClientService via pattern!(ProsodyClientService)
        // - Phase 7: Callback functions via function!(...)
        // - Phase 8: Context service via pattern!(Context)
        // - Phase 9: AdminClientService via pattern!(AdminClientService)
        .validate()
        .build()
}
