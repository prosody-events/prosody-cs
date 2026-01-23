//! Build script for generating C# bindings from Rust FFI functions.
//!
//! This script uses csbindgen to automatically generate C# P/Invoke declarations
//! from the `extern "C"` functions defined in the Rust source files.

fn main() {
    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/runtime.rs")
        .input_extern_file("src/client.rs")
        .input_extern_file("src/handler.rs")
        .input_extern_file("src/context.rs")
        .input_extern_file("src/types.rs")
        .csharp_dll_name("prosody_cs")
        .csharp_namespace("Prosody.Native")
        .csharp_class_name("NativeMethods")
        .csharp_class_accessibility("internal")
        .csharp_use_function_pointer(true)
        .generate_csharp_file("../src/Prosody/Native/NativeMethods.g.cs")
        .expect("Failed to generate C# bindings");
}
