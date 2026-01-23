//! # Prosody C# Native Bindings
//!
//! This crate provides C# bindings for the Prosody Kafka client library.
//! It bridges the Rust implementation of Prosody with C#/.NET, allowing .NET
//! applications to use Prosody for event processing and messaging.
//!
//! The extension handles asynchronous communication between Rust and C#,
//! provides client functionality for interacting with Kafka, and manages
//! message handling with proper lifecycle management.

#![allow(clippy::multiple_crate_versions)]

mod client;
mod context;
mod handler;
mod runtime;
mod types;
