# Phase 1 implementation boundary

This package contains contracts and safety foundations, not a bundled Lore runtime.
The official v0.10.0 artifact URLs, formats, sizes and archive checksums were confirmed later;
see [Phase 2 integration notes](Phase2_Integration_Notes.md) for the pinned manifest and CLI probe.
No artifact is downloaded at Editor startup.

- `Core` contains typed identifiers, paths, errors, results, file states and an immutable repository snapshot.
- `Application/Backend` splits repository, status, revision, push, branch, lock, diff and merge contracts.
  Capability resolution returns a fixed per-operation SDK/CLI choice; it never switches after execution starts.
- Runtime manifest validation checks required fields, platform uniqueness, cache-safe version and format
  tokens, HTTPS URL syntax, positive size and SHA-256 syntax. It **cannot attest that a URL is owned by Epic**;
  release verification must check provenance and actual artifact bytes before enabling downloads.
- `LoreRuntimeManager` can verify local artifacts, find platform metadata, check an installed runtime via
  an injected probe, record project usage, and orchestrate an explicitly requested offline installation.
  Installation is disabled without an archive validator/installer and executable/version probe. An installer
  must validate extraction paths, validate expected files and version, and publish atomically. The manager
  takes a cross-process cache lock and verifies a private copy before calling the installer.
- Runtime cache locations are per-user OS cache paths. Registry writes use an atomic replacement under
  the caller's cross-process lock. Project records are not a reliable process-liveness signal; removal
  remains disabled until a real usage detector can prevent deletion of an in-use runtime.
- Bootstrap reads the package-local `Editor/Infrastructure/Runtime/runtime-manifest.json` asset
  after Editor initialization. The file was added once official metadata was confirmed.
  It does not touch the network, disk cache or system PATH. Missing manifest means Setup Required;
  unsupported host architecture means Unsupported Platform. SDK/CLI adapters and backend activation
  belong to Phase 2 after the Lore interfaces and runtime are verified.

The Unity package does not include a host Unity project. EditMode tests must also be run from a host
project's Unity Test Runner before treating a release as validated.
