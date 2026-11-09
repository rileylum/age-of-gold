"""Build the pinned external shader converter, outside the repository and releases."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--runtime", choices=("linux-x64", "win-x64"),
                        default="win-x64" if os.name == "nt" else "linux-x64")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    dependencies = Path(os.environ["AOG_DEPENDENCIES"]).resolve(strict=True)
    output = args.output.resolve()
    protected = [repo, dependencies]
    for name in ("AOG_DARK_SOULS_REMASTERED", "AOG_ELDEN_RING"):
        if name in os.environ:
            protected.append(Path(os.environ[name]).resolve(strict=True))
    if output.exists() or any(output.is_relative_to(p) or p.is_relative_to(output) for p in protected):
        raise ValueError("Use a fresh output separate from the repository, dependencies and installs")
    checkout = dependencies / "HlslDecompiler"
    specification = json.loads((repo / "eng/dependencies.json").read_text())["HlslDecompiler"]
    revision = subprocess.check_output(["git", "-C", str(checkout), "rev-parse", "HEAD"], text=True).strip()
    changed = subprocess.check_output(["git", "-C", str(checkout), "status", "--porcelain", "--untracked-files=all"])
    if revision != specification["revision"] or changed:
        raise ValueError("The shader converter must use the clean pinned source revision")
    expected = json.loads((dependencies / "manifest.json").read_text())["files"]
    for relative, fingerprint in expected.items():
        if relative.startswith("HlslDecompiler/"):
            if hashlib.sha256((dependencies / relative).read_bytes()).hexdigest() != fingerprint:
                raise ValueError("Shader converter source fingerprint changed: " + relative)
    tracked = subprocess.check_output(["git", "-C", str(checkout), "ls-files", "-z"]).decode().split("\0")
    for path in [checkout / "Program.cs", *(p for folder in ("Hlsl", "DirectXShaderModel", "Util")
                 for p in (checkout / folder).rglob("*.cs"))]:
        relative = path.relative_to(checkout).as_posix()
        if path.is_symlink() or relative not in tracked or "HlslDecompiler/" + relative not in expected:
            raise ValueError("Unrecorded shader converter source: " + relative)
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".aog-shader-tool-", dir=output.parent))
    try:
        sdk = subprocess.check_output(["dotnet", "--version"], cwd=repo, text=True).strip()
        subprocess.run(["dotnet", "publish", str(repo / "eng/ShaderConverter"), "-c", "Release",
                        "-r", args.runtime, "--self-contained", "true", "-o", str(staging)], cwd=repo, check=True)
        shutil.copyfile(checkout / "LICENSE", staging / "HlslDecompiler-LICENSE.txt")
        files = {p.relative_to(staging).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in sorted(staging.rglob("*")) if p.is_file()}
        manifest = {"tool": "HlslDecompiler", "revision": revision, "sdk": sdk,
                    "runtime": args.runtime, "files": files}
        (staging / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", newline="\n")
        staging.rename(output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    print(f"Set AOG_SHADER_CONVERTER={output}")


if __name__ == "__main__":
    main()
