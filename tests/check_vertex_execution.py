"""Check semantic vertex rewriting against authored GPU and CPU references without a window."""

import argparse
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    root = parser.parse_args().output.resolve()
    if root.exists() or not root.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError("Use fresh output under system temp")
    root.mkdir()
    repo = Path(__file__).resolve().parents[1]
    subprocess.run(["cmake", "-S", str(repo / "tests/ShaderExecution"), "-B", str(root / "native"),
                    "-DCMAKE_TOOLCHAIN_FILE=" + str(repo / "eng/llvm-mingw.cmake"),
                    "-DAOG_LLVM_MINGW=" + os.environ["AOG_LLVM_MINGW"], "-DCMAKE_BUILD_TYPE=Release"], check=True)
    subprocess.run(["cmake", "--build", str(root / "native"), "--parallel", "2"], check=True)
    fixtures = root / "fixtures"
    subprocess.run(["dotnet", "run", "--project", str(repo / "tests/AgeOfGold.Checks"), "--",
                    "vertex-shader-fixtures", str(fixtures)], cwd=repo, check=True)

    def path(value):
        return str(value) if os.name == "nt" else "Z:" + str(value).replace("/", "\\")

    command = [str(root / "native/aog-shader-execution.exe")]
    if os.name != "nt":
        command.insert(0, "wine")
    comparisons = []
    x, y, z, w = (v / 64 - 2 for v in (200, 40, 120, 250))
    rotated = (.5*x + 2*y - z, -2*x + .25*y + 1.5*z, x - 3*y + .75*z)
    for mode in (0, 3):
        expected = (*rotated, w) if mode == 0 else (
            2*rotated[0] + x, 2*rotated[1] + y, 2*rotated[2] + z, -w)
        results = []
        for kind in ("reference", "translated"):
            output = root / f"{kind}-{mode}.bin"
            subprocess.run(command + [path(fixtures / "pixel.dxil"), path(output), str(mode),
                                      path(fixtures / f"{kind}.dxil"), "--tangents"], check=True, timeout=45)
            values = struct.unpack("<256f", output.read_bytes())
            assert all(math.isfinite(v) for v in values), "Non-finite shader result"
            error = max(abs(a-b) for a, b in zip(values, expected * 64, strict=True))
            assert error < 0.000002, f"{kind} mode {mode} disagrees with CPU reference: {error}"
            results.append(values)
        difference = max(abs(a-b) for a, b in zip(*results, strict=True))
        assert difference < 0.000002, f"Second tangent GPU mismatch: {difference}"
        comparisons.append({"mode": mode, "values": 256, "maximumError": difference})
    (root / "check.json").write_text(json.dumps({"passed": True, "comparisons": comparisons}, indent=2) + "\n")
    print("PASS: semantic tangent rewrite matches independent CPU and GPU references on both control-flow paths")


if __name__ == "__main__":
    main()
