"""Compare authored DXBC and translated DXIL by rendering without opening a game or window."""

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
    args = parser.parse_args()
    root = args.output.resolve()
    if root.exists() or not root.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError("Use a fresh output directory under system temp")
    root.mkdir()
    repo = Path(__file__).resolve().parents[1]
    subprocess.run(["cmake", "-S", str(repo / "tests/ShaderExecution"), "-B", str(root / "native"),
                    "-DCMAKE_TOOLCHAIN_FILE=" + str(repo / "eng/llvm-mingw.cmake"),
                    "-DAOG_LLVM_MINGW=" + os.environ["AOG_LLVM_MINGW"], "-DCMAKE_BUILD_TYPE=Release"], check=True)
    subprocess.run(["cmake", "--build", str(root / "native"), "--parallel", "2"], check=True)
    executable = root / "native/aog-shader-execution.exe"

    def path(value):
        return str(value) if os.name == "nt" else "Z:" + str(value).replace("/", "\\")

    def run(arguments):
        command = [str(executable)] if os.name == "nt" else ["wine", str(executable)]
        subprocess.run(command + arguments, check=True, timeout=45)

    inputs = root / "inputs"
    inputs.mkdir()
    for name, profile in (("pixel", "ps_5_0"), ("vertex", "vs_5_0"), ("compute", "cs_5_0"), ("writable", "ps_5_0")):
        run(["--compile", profile, path(repo / "tests/ShaderExecution" / (name + ".hlsl")), path(inputs / (name + ".dxbc"))])
    converted = root / "converted"
    subprocess.run(["dotnet", "run", "--project", str(repo / "tests/AgeOfGold.Checks"), "--",
                    "source-shader-compile", str(inputs), str(converted)], cwd=repo, check=True)
    comparisons = []
    for mode in (0, 3):
        values = []
        for name, source, suffix in (("source", inputs, ".dxbc"), ("translated", converted, ".dxil")):
            output = root / f"{name}-{mode}.bin"
            run([path(source / ("pixel" + suffix)), path(output), str(mode), path(source / ("vertex" + suffix))])
            data = output.read_bytes()
            assert len(data) == 64 * 4 * 4, "Incomplete graphics readback"
            row = struct.unpack("<256f", data)
            assert all(math.isfinite(v) for v in row), "Non-finite graphics result"
            values.append(row)
        expected = []
        for index in range(64):
            x = (index - 16) / 16
            expected.extend((min(abs(x), 1), abs(x) * (1 if mode == 0 else 10), abs(x) + 1.1 * x*x, -x))
        cpu_error = max(abs(a-b) for a, b in zip(values[0], expected, strict=True))
        conversion_error = max(abs(a-b) for a, b in zip(*values, strict=True))
        assert cpu_error < 0.000002, f"Source GPU output disagrees with CPU expectation: {cpu_error}"
        assert conversion_error < 0.000002, f"Translated GPU output differs: {conversion_error}"
        comparisons.append({"mode": mode, "values": 256, "cpuMaximumError": cpu_error,
                            "conversionMaximumError": conversion_error})
    report = {"passed": True, "comparisons": comparisons,
              "converterFingerprint": (converted / "tool-sha256.txt").read_text().strip()}
    (root / "check.json").write_text(json.dumps(report, indent=2) + "\n")
    print("PASS: translated graphics match authored GPU reference and independent CPU expectations")


if __name__ == "__main__":
    main()
