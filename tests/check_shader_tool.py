"""Prove external shader-tool construction and reject changed source and reused output."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = args.output.resolve()
    if root.exists() or not root.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError("Use a fresh directory under system temp")
    root.mkdir()
    repo = Path(__file__).resolve().parents[1]
    manifests = []
    for name in ("first", "repeat"):
        destination = root / name
        result = subprocess.run(["python", str(repo / "eng/build_shader_tool.py"), str(destination)],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (root / (name + ".log")).write_text(result.stdout)
        if result.returncode:
            raise RuntimeError(result.stdout)
        manifest = json.loads((destination / "manifest.json").read_text())
        assert manifest["revision"] == "14bb0af4f9c074138df5a40d8d0bee0f30da0e30"
        assert "HlslDecompiler-LICENSE.txt" in manifest["files"]
        for relative, expected in manifest["files"].items():
            assert hashlib.sha256((destination / relative).read_bytes()).hexdigest() == expected
        executable = destination / ("HlslDecompiler.exe" if os.name == "nt" else "HlslDecompiler")
        result = subprocess.run([str(executable), "--help"], check=True, text=True, capture_output=True)
        assert "--instructions" in result.stdout
        manifests.append(manifest)
    assert manifests[0] == manifests[1], "External shader tool builds differ"
    result = subprocess.run(["python", str(repo / "eng/build_shader_tool.py"), str(root / "first")],
                            text=True, capture_output=True)
    assert result.returncode and "fresh" in result.stderr.lower(), result.stderr
    changed = Path(os.environ["AOG_DEPENDENCIES"]) / "HlslDecompiler/Hlsl/AogAuthoredUntracked.cs"
    with changed.open("x") as stream:
        stream.write("// Authored source-integrity probe.\n")
    try:
        result = subprocess.run(["python", str(repo / "eng/build_shader_tool.py"), str(root / "changed")],
                                text=True, capture_output=True)
        assert result.returncode and "clean pinned" in result.stderr, result.stderr
        assert not (root / "changed").exists(), "Rejected tool build left output"
    finally:
        changed.unlink()
    (root / "check.json").write_text(json.dumps({"passed": True, "repeatIdentical": True,
        "files": len(manifests[0]["files"]), "overwriteRejected": True, "changedSourceRejected": True}, indent=2) + "\n")
    print("PASS: pinned external shader tool repeats exactly, retains notices and refuses overwrite")


if __name__ == "__main__":
    main()
