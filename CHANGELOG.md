# Changelog

## [2.5.0](https://github.com/cincpro/prosody-cs/compare/v2.4.1...v2.5.0) (2026-03-19)


### Features

* configuration error surfacing, per-type timer semaphores, timer read performance ([#27](https://github.com/cincpro/prosody-cs/issues/27)) ([7015c50](https://github.com/cincpro/prosody-cs/commit/7015c50457441a71e1dcd3eefc62dca8cd4ffb9a))

## [2.4.1](https://github.com/cincpro/prosody-cs/compare/v2.4.0...v2.4.1) (2026-03-13)


### Bug Fixes

* telemetry event_time uses millisecond precision with Z suffix ([#25](https://github.com/cincpro/prosody-cs/issues/25)) ([31522fa](https://github.com/cincpro/prosody-cs/commit/31522facfbaee071b321e6194ad21c0f5ba18a6e))

## [2.4.0](https://github.com/cincpro/prosody-cs/compare/v2.3.1...v2.4.0) (2026-03-12)


### Features

* add telemetry emitter support ([#22](https://github.com/cincpro/prosody-cs/issues/22)) ([708a396](https://github.com/cincpro/prosody-cs/commit/708a3967d1465a7249b2bb5dc3c00af4b0a78b68))

## [2.3.1](https://github.com/cincpro/prosody-cs/compare/v2.3.0...v2.3.1) (2026-03-05)


### Bug Fixes

* prevent false OffsetDeleted errors from concurrent loader requests ([#20](https://github.com/cincpro/prosody-cs/issues/20)) ([4f4188b](https://github.com/cincpro/prosody-cs/commit/4f4188b4a0e2a5e375ae841b84b13855c2e82e2e))

## [2.3.0](https://github.com/cincpro/prosody-cs/compare/v2.2.2...v2.3.0) (2026-03-02)


### Features

* add ProsodyLogging.ResetForTesting() public method ([#18](https://github.com/cincpro/prosody-cs/issues/18)) ([2560481](https://github.com/cincpro/prosody-cs/commit/25604812cd188889844f948aeea606dafca158f2))

## [2.2.2](https://github.com/cincpro/prosody-cs/compare/v2.2.1...v2.2.2) (2026-02-26)


### Bug Fixes

* graceful slab_loader shutdown and remove timer backpressure gaps ([#16](https://github.com/cincpro/prosody-cs/issues/16)) ([649599b](https://github.com/cincpro/prosody-cs/commit/649599bac25fec56f555fde9120c8bc5571d4a58))

## [2.2.1](https://github.com/cincpro/prosody-cs/compare/v2.2.0...v2.2.1) (2026-02-23)


### Bug Fixes

* release ci ([#14](https://github.com/cincpro/prosody-cs/issues/14)) ([363e183](https://github.com/cincpro/prosody-cs/commit/363e183c377fd7f83ee4d5d5873bdefe2215e989))

## [2.2.0](https://github.com/cincpro/prosody-cs/releases/tag/v2.2.0) (2026-02-20)


### Features

* restructure namespaces, Logging updates, builder API changes, DI integration changes and options validation ([#10](https://github.com/cincpro/prosody-cs/issues/10)) ([fe224ca](https://github.com/cincpro/prosody-cs/commit/fe224ca8f7120ff0535637f3612d0eca97b12e4b))

## [2.1.0](https://github.com/cincpro/prosody-cs/releases/tag/v2.1.0) (2026-02-12)


### Features

* add admin client, integration tests, and project improvements ([cc5d5ac](https://github.com/cincpro/prosody-cs/commit/cc5d5ac4ce586c3fb67ef3a86c6abbbdd7c401be))
* add fluent builder pattern for client configuration ([#4](https://github.com/cincpro/prosody-cs/issues/4)) ([9030738](https://github.com/cincpro/prosody-cs/commit/9030738d88f5f58e21989e2a2b0465180b63b0bb))
* add GitHub Actions workflows for CI/CD ([232c0a0](https://github.com/cincpro/prosody-cs/commit/232c0a0dabc04d2356a3cc0c0f33f8a38b1e853a))
* add logging integration with tracing-subscriber ([1f4fbb8](https://github.com/cincpro/prosody-cs/commit/1f4fbb8e0d7e220860c13ee64db542c168713b4d))
* add OpenTelemetry trace context propagation ([3046761](https://github.com/cincpro/prosody-cs/commit/3046761cddcb3594feca419174084c060d75dd85))
* add permanent error classification for handlers ([5f91ed7](https://github.com/cincpro/prosody-cs/commit/5f91ed7de9889e659efe92fe21e6b0f3ef5603a9))
* C# client with UniFFI bindings ([a59a625](https://github.com/cincpro/prosody-cs/commit/a59a625fe4397fdcccb5fd86ba1bb9f360674b82))
* **ffi:** add callback handler pattern with async cancellation ([3e974f3](https://github.com/cincpro/prosody-cs/commit/3e974f3321d425ccda165dd2f1a0545a8f7a8e03))
* **ffi:** add error handling and runtime modules with cdylib wrapper ([d4d2d67](https://github.com/cincpro/prosody-cs/commit/d4d2d67ed895c6bfdbf369e3629de3d0e391f5aa))
* **ffi:** add error type conversions and C# exception helper ([095c68f](https://github.com/cincpro/prosody-cs/commit/095c68fda9f3ea3abb624a8af81455c4c275e4ff))
* **ffi:** add Interoptopus alpha fixes and FFI conversion ([3091fc7](https://github.com/cincpro/prosody-cs/commit/3091fc71c21d3b1763b5db4715c00fc962f4054f))
* **ffi:** add Makefile build system and configuration types ([09a051f](https://github.com/cincpro/prosody-cs/commit/09a051fbacc975bfd5dd2b5983ffda530ec58dd0))
* **ffi:** implement OpenTelemetry trace context propagation ([c17aaeb](https://github.com/cincpro/prosody-cs/commit/c17aaeb342bdd817b537f1629ea6626c32a07f84))
* **ffi:** implement ProsodyClientService and FFI types ([2ce2299](https://github.com/cincpro/prosody-cs/commit/2ce2299206ce58c0b2d90e06ddb7eb2080b75e89))
* implement IAsyncDisposable on ProsodyClient ([#5](https://github.com/cincpro/prosody-cs/issues/5)) ([7bb7f51](https://github.com/cincpro/prosody-cs/commit/7bb7f51390cf80eeab689d3193b958122adb174e))
* propagate exception messages from C# to Rust ([3b21916](https://github.com/cincpro/prosody-cs/commit/3b219164312873b47d6948e927eeb2d026766e7e))


### Bug Fixes

* add CARGO_NET_GIT_FETCH_WITH_CLI globally to all workflows ([c668b90](https://github.com/cincpro/prosody-cs/commit/c668b90313c6c12e16933b439ba83085820b74a7))
* add jemalloc global allocator and fix broken doc link ([981111d](https://github.com/cincpro/prosody-cs/commit/981111d5ec222fed9827c2d7504c0b49bb8cc5a5))
* add zlib to cross build container ([6eb8bb6](https://github.com/cincpro/prosody-cs/commit/6eb8bb604dc852b0af66a5c0b31f29ee645a5280))
* bump prosody ([bb910e3](https://github.com/cincpro/prosody-cs/commit/bb910e3f3bbb3ccc626d2f5f5b35fec337bea237))
* cache uniffi-cs installation and restore release please manifest ([#8](https://github.com/cincpro/prosody-cs/issues/8)) ([fc1d753](https://github.com/cincpro/prosody-cs/commit/fc1d7534438fbb6ffb0712b22d3233b90c68c485))
* compile libz statically ([b725b60](https://github.com/cincpro/prosody-cs/commit/b725b608d622a18b85b4c7754628447a5df05364))
* correct native library paths in build configuration ([319906d](https://github.com/cincpro/prosody-cs/commit/319906d616d4be006deeaafce5609175255636cb))
* disable_initial_exec_tls ([985cd5e](https://github.com/cincpro/prosody-cs/commit/985cd5e210065ae472270b3319c2fd6e8c417838))
* **ffi:** use CancellationToken to fix race condition ([d5ac20d](https://github.com/cincpro/prosody-cs/commit/d5ac20dbc2f905fdc080c2df4812df3f9282f3f7))
* install CSharpier globally for uniffi-bindgen-cs auto-formatting ([7e97424](https://github.com/cincpro/prosody-cs/commit/7e97424130b0d78b3f511f20aaf92be2257d4c90))
* install the right zlib architecture ([4b09193](https://github.com/cincpro/prosody-cs/commit/4b09193354af4fdd6aa42f647cde173c964bdbb7))
* link zlib in cross ([cd6c30a](https://github.com/cincpro/prosody-cs/commit/cd6c30ad7f05f700b358f5558a703f557dc836aa))
* namespace the package ([f6c7e44](https://github.com/cincpro/prosody-cs/commit/f6c7e44454e0693db94318be1a31d03549e5ab7a))
* pass through zlib paths ([f74ca2f](https://github.com/cincpro/prosody-cs/commit/f74ca2fa8087c90b0882172b8b6c2b99b59ebd4f))
* remove needless borrow in logging.rs ([9e4b4a2](https://github.com/cincpro/prosody-cs/commit/9e4b4a219c5ee9a60516356f20fbcad6046a094e))
* support older glibcs ([c08babf](https://github.com/cincpro/prosody-cs/commit/c08babf5554a3840269d5ad77510ff13dbfdd0ad))
* test container depenencies ([f8d8dea](https://github.com/cincpro/prosody-cs/commit/f8d8deacef1e58832c75204cc23dc210cf500d1e))
* tests should use nuget package ([fbf80ad](https://github.com/cincpro/prosody-cs/commit/fbf80ad4e566c99d9b6397f13a0f0db6420fbba1))
* update prosody ref ([0256b14](https://github.com/cincpro/prosody-cs/commit/0256b1496f73abf76186d8f0f93a78ea4b678df1))
* use baptiste0928/cargo-install for enterprise allowlist compliance ([7a9d826](https://github.com/cincpro/prosody-cs/commit/7a9d826aa864f7cfc65c72e9f1f49dc2a8b2c598))
* use environment variables for test configuration ([3d389c7](https://github.com/cincpro/prosody-cs/commit/3d389c7d14d31af73d9c23ee72e32d2f76e614d6))
* use geekbot username for git credentials ([2be4b33](https://github.com/cincpro/prosody-cs/commit/2be4b33d072f16947b285aca4ef38b86930116e2))
* use macos-15 runner (macos-13 is retired) ([99aa075](https://github.com/cincpro/prosody-cs/commit/99aa0752e2986e7d0a22590864d2697b0eb43473))
* use proper target-specific dependency for jemalloc ([f71d72d](https://github.com/cincpro/prosody-cs/commit/f71d72dac4025b4fb80ad4ce9a7b46b50f51fba6))
* use TEMP_GH_ALL_REPOS_RW for git credentials ([3de206c](https://github.com/cincpro/prosody-cs/commit/3de206ca2f7f5c7ea4af5acc45d6349c08e00c8e))


### Performance Improvements

* use jemalloc allocator ([11410ac](https://github.com/cincpro/prosody-cs/commit/11410ac2d016d02b56255529a0aa9d00af30faf8))
