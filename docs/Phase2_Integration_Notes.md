# Phase 2 integration boundary

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
  No stable machine-readable status output format was confirmed. The CLI transport deliberately
  does not parse human-readable output or advertise Repository/Status capabilities.
- [Quickstart](https://github.com/EpicGames/lore/blob/main/docs/tutorials/quickstart.md)
  documents `.lore/` as a normal checkout marker. Detection treats the marker only as a hint;
  the Repository ID must come from Lore itself. Other checkout layouts require explicit support.

Implemented here: a bridge-bound SDK read adapter converting input into typed domain objects,
backend availability/selection without mid-operation fallback, repository candidate detection,
bounded CLI process transport requiring an absolute configured executable, and immutable status
snapshots that discard older generations. None of these claim an SDK bridge is installed.

Before enabling live status: pin and verify a compatible Lore SDK or external bridge and its
native runtime; implement and test that bridge against a real Lore repository; confirm a
versioned CLI machine output format if CLI fallback is required; serialize any `--scan` write;
test both Windows x64 and macOS arm64 in a Unity host. Until then the capability resolver returns
Unsupported instead of inventing status or silently invoking a PATH binary.
