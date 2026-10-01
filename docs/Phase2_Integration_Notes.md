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
  Repository/Status capabilities must be advertised only when a verified cached executable is
  explicitly injected; bootstrap does not create or register one.
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
An offline disposable Lore repository was used to verify JSON revision, untracked and staged
events. This does not authorize automatic Runtime installation or establish a plugin-wide
verified Lore version. macOS arm64 and Unity EditMode integration remain untested.

Before activating live status in Unity: verify an installed Runtime/executable with an exact
version probe, inject the CLI adapter and shared operation gate from the Composition Root,
test the full JSON mapping on Windows x64 and macOS arm64 in a Unity host, and arrange a
Unity-compatible SDK bridge if SDK-first selection is to be restored. Until then bootstrap
leaves the Runtime unconfigured and the capability resolver returns Unsupported.
