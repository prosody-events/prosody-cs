# syntax=docker/dockerfile:1

FROM rust:bookworm AS chef

RUN apt-get update \
    && apt-get install -y --no-install-recommends cmake libcurl4-openssl-dev mold \
    && cargo install cargo-chef --version 0.1.77 --locked \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

FROM chef AS planner
COPY . .
RUN cargo chef prepare --recipe-path recipe.json

FROM chef AS builder
ARG RUST_TARGET
ENV RUSTFLAGS="-C link-arg=-fuse-ld=mold"

COPY --from=planner /workspace/recipe.json recipe.json
RUN rustup target add "${RUST_TARGET}"
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/usr/local/cargo/git \
    --mount=type=cache,target=/workspace/target \
    cargo chef cook --release --package prosody-ffi --target "${RUST_TARGET}" --recipe-path recipe.json

COPY . .
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/usr/local/cargo/git \
    --mount=type=cache,target=/workspace/target \
    cargo build --release --package prosody-ffi --target "${RUST_TARGET}" \
    && mkdir /output \
    && cp "target/${RUST_TARGET}/release/libprosody_ffi.so" /output/

FROM scratch AS artifact
COPY --from=builder /output/ /
