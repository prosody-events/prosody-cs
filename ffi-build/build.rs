//! Build script for generating C# bindings from the prosody-ffi crate.
//!
//! This build script runs during `cargo build -p prosody-ffi-build` and generates
//! C# bindings to `src/Prosody/Native/ProsodyFFI.cs` using Interoptopus.
//!
//! The script automatically:
//! - Creates the output directory if it doesn't exist
//! - Removes any stale generated files before writing
//! - Applies post-generation fixes for known Interoptopus 0.15 alpha bugs
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
use interoptopus_backend_csharp::{Interop, Visibility};
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
        .visibility_types(Visibility::ForceInternal)
        .inventory(ffi_inventory())
        .build()?;

    interop.write_file(output_path)?;

    // Apply post-generation fixes for known Interoptopus 0.15 alpha bugs
    apply_alpha_fixes(output_path)?;

    println!("Generated C# bindings to src/Prosody/Native/ProsodyFFI.cs");
    Ok(())
}

/// Apply fixes for known code generation bugs in Interoptopus 0.15 alpha.
///
/// These bugs produce invalid C# syntax or incorrect behavior.
/// Each fix should be reported upstream and removed once Interoptopus 0.15 is stable.
///
/// # Known Issues (TODO: open GitHub issues)
///
/// 1. **OptionBool cast syntax**: Generated code has `_(byte) (Some ? 1 : 0)`
///    instead of `(byte) (_Some ? 1 : 0)`.
///
/// 2. **string.Empty reference**: Generated code has `_Some._string.Empty`
///    instead of `string.Empty`.
///
/// 3. **IntPtr.Zero reference**: Generated code has `_IntPtr.Zero`
///    instead of `IntPtr.Zero`.
///
/// 4. **Visibility::ForceInternal inconsistent**: The `visibility_types` setting
///    only applies to some partial declarations. Marshaller partials, helper classes,
///    and extension classes are still generated as `public`. All 4 replacements below
///    are required. Workaround: post-process to replace `public` with `internal`.
///
/// 5. **C# struct Option fields zero-initialize to Some, not None**: In Rust,
///    `ffi::Option` has `#[default]` on `None` (discriminant 1). But C# structs
///    zero-initialize fields to 0, which maps to `Some` (discriminant 0). This
///    causes struct fields of Option types to appear as `Some(default)` instead
///    of `None` when the containing struct is default-constructed. Workaround:
///    require users to use factory methods that explicitly initialize Options.
///    TODO: Open Interoptopus issue - Option discriminant order causes C# zero-init mismatch
fn apply_alpha_fixes(path: &Path) -> Result<(), Box<dyn Error>> {
    let content = fs::read_to_string(path)?;

    let fixed = content
        // TODO: Open Interoptopus issue - OptionBool cast syntax bug
        // Generated: `_(byte) (Some ? 1 : 0)` should be `(byte) (_Some ? 1 : 0)`
        .replace("_(byte) (Some ? 1 : 0)", "(byte) (_Some ? 1 : 0)")
        // TODO: Open Interoptopus issue - string.Empty reference bug
        // Generated: `_Some._string.Empty` should be `string.Empty`
        .replace("_Some._string.Empty", "string.Empty")
        // TODO: Open Interoptopus issue - IntPtr.Zero reference bug
        // Generated: `_IntPtr.Zero` should be `IntPtr.Zero`
        .replace("= _IntPtr.Zero", "= IntPtr.Zero")
        // TODO: Open Interoptopus issue - Visibility::ForceInternal doesn't apply to all partials
        // Marshaller partial declarations are still generated as `public`
        .replace("public static partial class", "internal static partial class")
        .replace("public static class", "internal static class")
        .replace("public partial struct", "internal partial struct")
        .replace("public partial class", "internal partial class");

    fs::write(path, fixed)?;
    Ok(())
}
