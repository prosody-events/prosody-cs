//! C dynamic library wrapper for prosody-ffi.
//!
//! This crate produces the `libprosody_ffi.dylib` (macOS), `libprosody_ffi.so` (Linux),
//! or `prosody_ffi.dll` (Windows) that C# loads via P/Invoke.
//!
//! All FFI definitions live in the `prosody-ffi` crate. This crate simply re-exports
//! them to produce the cdylib output.
//!
//! ## Why This Crate Exists
//!
//! Rust's Cargo has a known issue where a crate with `crate-type = ["cdylib", "rlib"]`
//! that is also used as a build-dependency causes filename collisions. The idiomatic
//! solution is to keep cdylib production in a "leaf node" crate that nothing else
//! depends on.
//!
//! See: <https://github.com/rust-lang/cargo/issues/6313>

// Re-export everything from prosody-ffi so it's available in the cdylib
pub use prosody_ffi::*;
