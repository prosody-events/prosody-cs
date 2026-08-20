//! Attributes for the Prosody FFI boundary.

use proc_macro::TokenStream;
use quote::quote;
use syn::{ImplItem, ItemImpl, parse_macro_input};

/// Enters the shared Tokio runtime when an exported future is polled.
#[proc_macro_attribute]
pub fn ffi_async(_attribute: TokenStream, item: TokenStream) -> TokenStream {
    let mut implementation = parse_macro_input!(item as ItemImpl);
    for item in &mut implementation.items {
        let ImplItem::Fn(method) = item else {
            continue;
        };
        if method.sig.asyncness.is_none() {
            continue;
        }

        let body = &method.block;
        method.block = syn::parse_quote!({ crate::runtime::enter(async move #body).await });
    }

    quote!(#implementation).into()
}
