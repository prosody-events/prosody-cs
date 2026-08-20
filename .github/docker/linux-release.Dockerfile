# syntax=docker/dockerfile:1

FROM rust:bookworm AS chef

RUN apt-get update \
    && apt-get install -y --no-install-recommends binutils cmake libcurl4-openssl-dev mold protobuf-compiler \
    && cargo install cargo-chef --version 0.1.77 --locked \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

FROM chef AS planner
COPY . .
RUN cargo chef prepare --recipe-path recipe.json

FROM chef AS builder
ARG RUST_TARGET
ARG RUSTFLAGS
ENV RUSTFLAGS="${RUSTFLAGS}"
ENV CARGO_PROFILE_RELEASE_DEBUG=2

COPY --from=planner /workspace/recipe.json recipe.json
RUN rustup target add "${RUST_TARGET}"
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/usr/local/cargo/git \
    --mount=type=cache,target=/workspace/target \
    cargo chef cook --release --package prosody_ffi --target "${RUST_TARGET}" --recipe-path recipe.json

COPY . .
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/usr/local/cargo/git \
    --mount=type=cache,target=/workspace/target \
    cargo build --release --package prosody_ffi --target "${RUST_TARGET}" \
    && mkdir /output \
    && mkdir /output/symbols \
    && cp "target/${RUST_TARGET}/release/libprosody_ffi.so" /output/ \
    && objcopy --only-keep-debug /output/libprosody_ffi.so /output/symbols/libprosody_ffi.so.debug \
    && strip --strip-debug /output/libprosody_ffi.so \
    && objcopy --add-gnu-debuglink=/output/symbols/libprosody_ffi.so.debug /output/libprosody_ffi.so

FROM scratch AS artifact
COPY --from=builder /output/ /
