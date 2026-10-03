# Phase 2 integration boundary

The package now targets Unity 2022.3 or newer. Unity's
[C# compiler documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/CSharpCompiler.html)
specifies C# 9.0, while its [.NET profile](https://docs.unity3d.com/2022.3/Documentation/Manual/dotnet-profile-support.html)
supports .NET Standard 2.1, **not** net9.0. Validate source with `LangVersion=9.0` and
`TargetFramework=netstandard2.1`; do not import the net9/net10 LoreVcs package into Unity.

Current implementation direction: CLI-first while the official SDK's Unity compatibility is
unresolved. This is a temporary prioritization, not a silent replacement of the frozen SDK-first
product design. Once a compatible SDK bridge is available the capability resolver still prefers it.

Official references checked on 2026-10-02:

- [EpicGames/lore-dotnet README](https://github.com/EpicGames/lore-dotnet/blob/main/README.md)
  documents `Lore.RepositoryStatus(...)`, callbacks and `.Wait()`.
- [LoreVcs.csproj](https://github.com/EpicGames/lore-dotnet/blob/main/LoreVcs/LoreVcs.csproj)
  targets **net9.0/net10.0**, and NuGet's native runtime is resolved by RID. This cannot be
  referenced directly from the current Unity Editor assembly (netstandard-compatible) without
  a separately verified compatibility/distribution plan. No NuGet binary or SDK types are bundled.
- [CLI command reference](https://github.com/EpicGames/lore/blob/main/docs/reference/lore-cli-commands.md)
  documents `--repository`, `--non-interactive`, `--no-pager`, `repository status`, and
  `--scan`. The last operation persists dirty flags into staged state; it is **not a pure read**.
  [CLI v0.10.0 source](https://github.com/EpicGames/lore/blob/v0.10.0/lore-client/src/cli/cli.rs)
  confirms a hidden `--json` option serializing one LoreEvent per line. The adapter parses this
  event stream and rejects malformed or unknown events instead of parsing terminal text.
  `--scan` is serialized per repository with a gate shared by future write services.
  Repository/Status capabilities are advertised only after a cache-local executable passes
  the pinned executable SHA-256 and `--version` probe. Bootstrap never searches the system PATH.
- [Quickstart](https://github.com/EpicGames/lore/blob/main/docs/tutorials/quickstart.md)
  documents `.lore/` as a normal checkout marker. Detection treats the marker only as a hint;
  the Repository ID must come from Lore itself. Other checkout layouts require explicit support.

Implemented here: a bridge-bound SDK read adapter converting input into typed domain objects,
backend availability/selection without mid-operation fallback, repository candidate detection,
bounded CLI process transport requiring an absolute configured executable, a JSON-event CLI
repository/status adapter, and immutable status
snapshots that discard older generations. None of these claim an SDK bridge is installed.

For local validation, the official v0.10.0 Windows x64 CLI ZIP was downloaded to a temporary
directory (not the package). Its SHA-256 matched the digest published on the
[Epic release](https://github.com/EpicGames/lore/releases/tag/v0.10.0):
`c755a7588b5bb2409a3803a085413bd159c119641e642484b4845dd527c42925`.
The official macOS arm64 archive also matched its published digest
(`4aef58f7e58c7ae618ebcf676bbe79925a64e2f6529c2e3094a02cbab192481b`).
The two extracted executables were hashed and pinned in `CliRuntimeProbe` for **v0.10.0 only**.
An offline disposable Lore repository was used on Windows to verify JSON revision, untracked and
staged events. The package now pins the official artifact URLs and digests in
`Editor/Infrastructure/Runtime/runtime-manifest.json`; it still never downloads automatically.
macOS arm64 execution and Unity EditMode integration remain untested.

Bootstrap probes only the version-specific user cache. If verification passes it creates the
CLI-first read services, detects the project's Lore repository, and requests an initial scan.
Without a verified cached executable it remains Setup Required; there is no silent PATH fallback.
Installation remains an explicit user operation (the archive installer is not yet available),
so simply importing the package does not install Lore. Before release, test the full JSON mapping
on macOS arm64 and Windows x64 in a Unity host. A Unity-compatible SDK bridge is still required
to restore SDK-first selection.
