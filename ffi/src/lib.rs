//! Prosody FFI bindings for C#.
//!
//! This crate provides FFI bindings for the Prosody Kafka client library,
//! using Interoptopus for automatic C# bindings generation.
//!
//! ## Crate Structure
//!
//! - `ffi/` (this crate): Contains FFI type definitions and `ffi_inventory()`
//! - `ffi-build/`: Contains build.rs that generates C# bindings
//!
//! ## Building
//!
//! ```bash
//! # Build the FFI library
//! cargo build -p prosody-ffi
//!
//! # Generate C# bindings (via build.rs)
//! cargo build -p prosody-ffi-build
//! ```

use interoptopus::inventory::Inventory;
use std::sync::LazyLock;
use tokio::runtime::Runtime;

// Module declarations (to be added as implementation progresses)
// pub mod callbacks;
// pub mod context;
// pub mod error;
// pub mod runtime;
// pub mod service;
// pub mod types;
// pub mod admin;
// pub mod logging;

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
/// # Example
///
/// ```rust,ignore
/// // In ffi-build/build.rs:
/// use prosody_ffi::ffi_inventory;
///
/// let generator = DotNet::new_built().with_config(config);
/// generator.write_file(&ffi_inventory(), "ProsodyFFI.cs");
/// ```
pub fn ffi_inventory() -> Inventory {
    Inventory::builder()
        // Types and services will be registered here as implementation progresses:
        // .register(pattern!(ProsodyClientService))
        // .register(pattern!(Context))
        // .register(pattern!(AdminClientService))
        // .register(function!(prosody_register_handler))
        // .register(function!(prosody_free_registered_handler))
        // etc.
        .validate()
        .build()
}
