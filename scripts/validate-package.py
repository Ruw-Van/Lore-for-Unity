"""Offline pre-release checks. Never downloads runtime artifacts or runs Unity."""
import json
import pathlib
import re
import sys


ROOT = pathlib.Path(__file__).resolve().parent.parent
VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-.][A-Za-z0-9.-]+)?\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")


def validate(tag=None):
    package = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))
    manifest = json.loads((ROOT / "Editor/Infrastructure/Runtime/runtime-manifest.json").read_text(encoding="utf-8"))
    assert package["name"] == "com.ruwvan.lore-for-unity"
    assert VERSION.fullmatch(package["version"])
    if tag:
        assert tag == "v" + package["version"], "Git tag must match the UPM version"
    version = manifest["loreVersion"]
    assert VERSION.fullmatch(version)
    assert len(manifest["artifacts"]) == 2
    platforms = set()
    for item in manifest["artifacts"]:
        platform = item["platform"]
        assert platform in {"Windows-x64", "macOS-arm64"} and platform not in platforms
        platforms.add(platform)
        assert SHA256.fullmatch(item["sha256"])
        assert item["downloadSize"] > 0
        assert item["artifactFormat"] in {"zip", "tar.gz"}
        assert item["officialArtifactUrl"].startswith(
            "https://github.com/EpicGames/lore/releases/download/v" + version + "/"
        )
    for filename in ("README.md", "CHANGELOG.md", "LICENSE.md", "Third Party Notices.md"):
        assert (ROOT / filename).is_file(), filename
    print("Offline UPM package and runtime manifest metadata checks passed.")


if __name__ == "__main__":
    try:
        validate(sys.argv[1] if len(sys.argv) > 1 else None)
    except (AssertionError, KeyError, ValueError) as error:
        sys.exit(f"Package validation failed: {error}")
