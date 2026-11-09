"""Fetch pinned library source outside the repository; never reuse a research working tree."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import havok_metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--mirror", type=Path, help="Local SoulsFormatsNEXT repository for offline setup")
    parser.add_argument("--hk-mirror", type=Path, help="Local HKLib repository for offline setup")
    parser.add_argument("--minhook-mirror", type=Path, help="Local MinHook repository for offline setup")
    parser.add_argument("--shader-mirror", type=Path, help="Local HlslDecompiler repository for offline setup")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    destination = args.destination.resolve()
    if destination.exists() or destination.is_relative_to(repo):
        raise ValueError("Dependency destination must be fresh and outside the repository")
    spec = json.loads(Path(__file__).with_name("dependencies.json").read_text())
    destination.mkdir(parents=True)
    for name, entry in spec.items():
        checkout = destination / name
        subprocess.run(["git", "init", "--quiet", str(checkout)], check=True)
        mirror = {"SoulsFormatsNEXT": args.mirror, "HKLib": args.hk_mirror,
                  "MinHook": args.minhook_mirror, "HlslDecompiler": args.shader_mirror}[name]
        source = str(mirror.resolve()) if mirror else entry["url"]
        subprocess.run(["git", "-C", str(checkout), "fetch", "--quiet", "--depth=1", source, entry["revision"]], check=True)
        subprocess.run(["git", "-C", str(checkout), "checkout", "--quiet", "--detach", "FETCH_HEAD"], check=True)
        actual = subprocess.check_output(["git", "-C", str(checkout), "rev-parse", "HEAD"], text=True).strip()
        if actual != entry["revision"]:
            raise RuntimeError(f"Unexpected dependency revision: {name}")
    # Oodle's optional logging hook is absent from an older library supplied by the host.
    path = destination / "SoulsFormatsNEXT/SoulsFormats/Utilities/Compression/Oodle/OodleCompressor.cs"
    code = path.read_text(encoding="utf-8-sig")
    marker = '            IntPtr setPrintf = InteropNativeLibrary.GetExport(handle, "OodleCore_Plugins_SetPrintf");'
    replacement = '''            if (!InteropNativeLibrary.TryGetExport(handle, "OodleCore_Plugins_SetPrintf", out IntPtr setPrintf))
            {
                uint version = 0;
                ((delegate* unmanaged<uint, uint*, int>)checkVersion)(0, &version);
                return version;
            }'''
    if code.count(marker) != 1:
        raise RuntimeError("Pinned Oodle adapter no longer has the expected optional-hook call")
    path.write_text(code.replace(marker, replacement), encoding="utf-8", newline="\n")
    havok_metadata.apply(destination / "HKLib")
    fingerprints = {}
    for name in spec:
        checkout = destination / name
        paths = subprocess.check_output(["git", "-C", str(checkout), "ls-files", "-z"]).split(b"\0")
        for relative in paths:
            if relative:
                file = checkout / relative.decode()
                fingerprints[file.relative_to(destination).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    (destination / "manifest.json").write_text(json.dumps({"dependencies": spec, "files": fingerprints}, indent=2) + "\n", newline="\n")
    print(f"Set AOG_DEPENDENCIES={destination}")


if __name__ == "__main__":
    main()
