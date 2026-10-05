import json
import importlib.util
import pathlib
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("validate_package", pathlib.Path(__file__).with_name("validate-package.py"))
validate_package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validate_package)


class PackageValidationTests(unittest.TestCase):
    def test_local_artifact_must_match_pinned_size_and_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / "Editor/Infrastructure/Runtime").mkdir(parents=True)
            (root / "package.json").write_text(json.dumps({
                "name": "com.ruwvan.lore-for-unity", "version": "0.1.0"
            }), encoding="utf-8")
            artifact = root / "runtime.zip"
            artifact.write_bytes(b"test")
            (root / "Editor/Infrastructure/Runtime/runtime-manifest.json").write_text(json.dumps({
                "loreVersion": "0.10.0",
                "artifacts": [
                    {"platform": platform,
                     "officialArtifactUrl": "https://github.com/EpicGames/lore/releases/download/v0.10.0/file",
                     "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                     "downloadSize": 4, "artifactFormat": "zip"}
                    for platform in ("Windows-x64", "macOS-arm64")
                ]
            }), encoding="utf-8")
            for name in ("README.md", "CHANGELOG.md", "LICENSE.md", "Third Party Notices.md"):
                (root / name).write_text("present", encoding="utf-8")
            with patch.object(validate_package, "ROOT", root):
                validate_package.validate(artifact="Windows-x64", artifact_path=str(artifact))
                artifact.write_bytes(b"bad!")
                with self.assertRaisesRegex(AssertionError, "SHA-256 mismatch"):
                    validate_package.validate(artifact="Windows-x64", artifact_path=str(artifact))
                with self.assertRaisesRegex(AssertionError, "Git tag"):
                    validate_package.validate(tag="v9.9.9")


if __name__ == "__main__":
    unittest.main()
