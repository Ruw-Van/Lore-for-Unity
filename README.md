# Lore for Unity

Lore for Unity is an Editor-only Unity integration for Lore.

It brings Lore workflows into the Unity Editor while respecting Unity's asset model, including assets and their `.meta` files, scenes, prefabs, project settings, locks, conflicts, and safe working-copy operations.

## Status

This project is in early development and is not yet ready for production use.

Initial platform targets:

- Windows x64
- macOS arm64

## Installation

In Unity 2022.3 or later on Windows x64 or macOS arm64, open **Window > Package
Manager > + > Add package from disk** and select this repository's `package.json`.
Open **Window > Lore > Diagnostics** for Runtime setup. Nothing is downloaded at
Editor startup: choose **Download and install official Lore Runtime** to fetch
the pinned Epic Games v0.10.0 archive, or **Install Lore Runtime from local
archive** to use a previously downloaded official archive. Both paths verify
the archive size/SHA-256 and Lore executable before publication to the user
cache. An existing invalid installation is not silently overwritten. A Lore
working copy (`.lore`) must exist at the Unity project root or an ancestor to
use Repository features. Unity Editor integration has not yet been validated
on a host project; use a disposable project/working copy for initial trials.

Once Runtime health is **Ready**, open **Window > Lore > Lore**. If this Unity
project and its ancestors have no `.lore` working copy, **Initialize this Unity
project with Lore** creates a local-only, offline repository at the Unity
project root after confirmation. Existing project files are not staged or
pushed. If `.lore` already exists but cannot be verified, initialization is
not offered; inspect or repair the existing working copy instead.

## Conflict resolution (experimental)

Open **Window > Lore > Lore**, select a conflicted file in Changes, and inspect its
Diff and Recovery operation. You can choose a Lore version, try an unambiguous
text/Unity-YAML merge, load and edit the conflict text manually, or run a
configured external merge tool. A successful edited result is staged and marked
resolved in Lore; inspect it before acknowledging the Recovery record. If both
an Asset and its `.meta` conflict, choose a version for the pair rather than
editing only one side. Ambiguous changes are not merged automatically.

Use **Window > Lore > Settings** to register multiple trusted external tools in
the project's personal `UserSettings` (not shared ProjectSettings). Select a
tool by name in Changes when resolving a conflict. Provide an absolute executable
path and **one argument per line**. The argument tokens must collectively contain
`{base}`, `{mine}`, `{theirs}`, and `{result}`; for example:

```text
--base={base}
--mine={mine}
--theirs={theirs}
--result={result}
```

Each placeholder is replaced by a temporary file path. The tool must write the
resolved text to `{result}` and exit with code 0. There is no shell, implicit
PATH lookup, credential storage or automatic fallback to another tool. Temporary
copies are removed after the tool exits normally; a process crash may leave
copies in the OS temporary directory. Only use trusted executables and never
put tokens or passwords in argument templates. Unity Editor and Lore remote
integration have not been validated; this feature is not production-ready.

## License

Lore for Unity is licensed under the [MIT License](LICENSE.md).
