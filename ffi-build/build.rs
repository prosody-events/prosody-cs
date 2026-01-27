//! Build script for generating C# bindings from the prosody-ffi crate.
//!
//! This build script runs during `cargo build -p prosody-ffi-build` and generates
//! C# bindings to `src/Prosody/Native/ProsodyFFI.cs` using Interoptopus.
//!
//! The script automatically:
//! - Creates the output directory if it doesn't exist
//! - Removes any stale generated files before writing
//!
//! ## Rebuild Triggers
//!
//! This build script re-runs when:
//! - `prosody-ffi` crate changes (automatic via build-dependency tracking)
//! - This build.rs file changes (automatic via Cargo)
//!
//! No explicit `rerun-if-changed` directives are needed because Cargo's
//! build-dependency tracking handles changes to `prosody-ffi` automatically.

use interoptopus::lang::NamespaceMappings;
use interoptopus_backend_csharp::Interop;
use prosody_ffi::ffi_inventory;
use std::error::Error;
use std::fs;
use std::path::Path;

fn main() -> Result<(), Box<dyn Error>> {
    // Tell Cargo when to re-run this build script:
    // 1. When build.rs itself changes (this directive)
    // 2. When prosody-ffi changes (automatic via build-dependency in Cargo.toml)
    //
    // Without any rerun-if-changed, Cargo assumes always rerun.
    // This directive limits reruns to when build.rs changes OR dependencies change.
    println!("cargo::rerun-if-changed=build.rs");

    let output_path = Path::new("../src/Prosody/Native/ProsodyFFI.cs");

    // Ensure output directory exists
    if let Some(parent) = output_path.parent() {
        fs::create_dir_all(parent)?;
    }

    // Remove stale generated file if it exists (ensures clean regeneration)
    if output_path.exists() {
        fs::remove_file(output_path)?;
    }

    let interop = Interop::builder()
        .class("ProsodyFFI")
        .dll_name("prosody_ffi")
        .namespace_mappings(NamespaceMappings::new("Prosody.Native"))
        .inventory(ffi_inventory())
        .build()?;

    interop.write_file(output_path)?;

    println!("Generated C# bindings to src/Prosody/Native/ProsodyFFI.cs");
    Ok(())
}
