"""Build the authored native hook check twice with the pinned external compiler."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = args.output.resolve()
    repo = Path(__file__).resolve().parents[1]
    compiler = Path(os.environ["AOG_LLVM_MINGW"]).resolve(strict=True)
    dependencies = Path(os.environ["AOG_DEPENDENCIES"]).resolve(strict=True)
    if root.exists() or not root.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError("Use a fresh output directory under system temp")
    for protected in (repo, compiler, dependencies):
        if root.is_relative_to(protected) or protected.is_relative_to(root):
            raise ValueError("Native check output must be separate from sources and installed tools")
    root.mkdir()

    def configure(output, tool_root):
        return subprocess.run([
            "cmake", "-S", str(repo / "tests/NativeHooks"), "-B", str(output),
            "-DCMAKE_TOOLCHAIN_FILE=" + str(repo / "eng/llvm-mingw.cmake"),
            "-DAOG_LLVM_MINGW=" + str(tool_root), "-DCMAKE_BUILD_TYPE=Release",
        ], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

    if os.name != "nt":
        fake = root / "unsupported/bin"
        fake.mkdir(parents=True)
        for name in ("x86_64-w64-mingw32-clang", "x86_64-w64-mingw32-clang++"):
            path = fake / name
            path.write_text("#!/bin/sh\nprintf 'clang version 0.0.0 (authored unsupported compiler)\\n'\n")
            path.chmod(0o755)
        result = configure(root / "rejected", fake.parent)
        (root / "rejected.log").write_text(result.stdout)
        assert result.returncode != 0 and "Unsupported LLVM-MinGW compiler" in result.stdout, result.stdout

    results = []
    for name in ("first", "repeat"):
        build = root / name
        result = configure(build, compiler)
        (root / (name + ".log")).write_text(result.stdout)
        if result.returncode:
            raise RuntimeError(result.stdout)
        subprocess.run(["cmake", "--build", str(build), "--parallel", "2"], check=True)
        executable = build / "aog-native-hooks.exe"
        command = [str(executable)] if os.name == "nt" else ["wine", str(executable)]
        subprocess.run(command, check=True)
        assert (build / "LICENSES/MinHook.txt").read_bytes() == (dependencies / "MinHook/LICENSE.txt").read_bytes()
        assert (build / "LICENSES/LLVM-MinGW.txt").read_bytes() == (compiler / "LICENSE.TXT").read_bytes()
        for notice in (compiler / "x86_64-w64-mingw32/share/mingw32").glob("COPYING*"):
            assert (build / "LICENSES/mingw-w64" / notice.name).read_bytes() == notice.read_bytes()
        results.append(digest(executable))
    assert results[0] == results[1], "Independent native builds differ"
    (root / "check.json").write_text(json.dumps({"passed": True, "repeatIdentical": True,
        "sha256": results[0], "licencesRetained": True}, indent=2) + "\n")
    print("PASS: native hook lifecycle, rejected compiler, deterministic builds and staged notices")


if __name__ == "__main__":
    main()
