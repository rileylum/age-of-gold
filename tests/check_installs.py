"""Exercise input preparation twice against both local installs, outside CI."""

import hashlib
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    if os.environ.get("CI"):
        raise RuntimeError("Install checks must never run in CI")
    source = Path(os.environ["AOG_DARK_SOULS_REMASTERED"]).resolve(strict=True)
    host = Path(os.environ["AOG_ELDEN_RING"]).resolve(strict=True)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--firelink", action="store_true", help="Prepare Firelink geometry and all explicit material textures")
    args = parser.parse_args()
    root = args.output.resolve()
    repo = Path(__file__).resolve().parents[1]
    if root.exists() or not root.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError("Use a fresh directory under system temp")
    if any(root.is_relative_to(p) or p.is_relative_to(root) for p in (source, host, repo)):
        raise ValueError("Check output must be separate from installs and repository")
    root.mkdir()
    request = {
        "source": ["map/MapStudio/m10_02_00_00.msb", "mtd/Mtd.mtdbnd.dcx",
                   "mtd/MtdPatch.mtdbnd.dcx", "other/envlight.tpf"],
        "host": ["map/mapstudio/m10_01_00_00.msb.dcx", "material/allmaterial.matbinbnd.dcx",
                 "map/m10/m10_01_00_00/m10_01_00_00_000000.mapbnd.dcx"],
    }
    request_path = root / "request.json"
    request_path.write_text(json.dumps(request))
    subprocess.run(["dotnet", "build", str(repo / "src/AgeOfGold"), "-c", "Release"], check=True)
    app = repo / "src/AgeOfGold/bin/Release/net10.0/AgeOfGold.dll"
    results = []
    for name in ("first", "repeat"):
        output = root / name
        command = ["dotnet", str(app), "prepare-firelink" if args.firelink else "prepare",
                   "--source", str(source), "--host", str(host), "--output", str(output)]
        if not args.firelink:
            command.extend(["--request", str(request_path)])
        subprocess.run(command, check=True, cwd=root)
        report = json.loads((output / "manifest.json").read_text())
        for entry in report["files"]:
            path = output / entry["path"]
            assert path.stat().st_size == entry["size"]
            assert digest(path) == entry["sha256"]
        for relative in [] if args.firelink else request["source"]:
            assert digest(source / relative) == digest(output / "source" / relative)
        for relative in request["host"]:
            with (output / "host" / relative).open("rb") as stream:
                assert stream.read(4) == b"DCX\0", f"Prepared host input lacks its compressed container: {relative}"
        # Re-read every recorded install input independently, including encrypted archive ranges.
        for game, install in (("source", source), ("host", host)):
            for key, expected in report["inputs"][game].items():
                if "@" in key:
                    relative, extent = key.split("@")
                    offset, length = map(int, extent.split("+"))
                    with (install / relative).open("rb") as stream:
                        stream.seek(offset)
                        actual = hashlib.sha256(stream.read(length)).hexdigest().upper()
                else:
                    actual = digest(install / key)
                assert actual == expected, f"Install input changed: {game}/{key}"
        results.append({str(p.relative_to(output)): digest(p) for p in output.rglob("*") if p.is_file()})
    assert results[0] == results[1], "Independent preparations differ"
    (root / "check.json").write_text(json.dumps({"passed": True, "outputs": results[0]}, indent=2) + "\n")
    print(f"PASS: {len(results[0])} outputs match independent preparations; recorded install inputs are unchanged")


if __name__ == "__main__":
    main()
