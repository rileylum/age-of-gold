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
    for name, profile in (("pixel", "ps_5_0"), ("vertex", "vs_5_0"), ("compute", "cs_5_0"),
                          ("writable", "ps_5_0"), ("resources", "ps_5_0"), ("constants", "ps_5_0"),
                          ("packed", "ps_5_0"), ("matrices", "ps_5_0"), ("constants-sm4", "ps_4_0"),
                          ("selector", "ps_5_0"), ("matrices-column", "ps_5_0"), ("values", "ps_5_0")):
        source_name = "constants" if name == "constants-sm4" else name
        run(["--compile", profile, path(repo / "tests/ShaderExecution" / (source_name + ".hlsl")), path(inputs / (name + ".dxbc"))])
    converted = root / "converted"
    subprocess.run(["dotnet", "run", "--project", str(repo / "tests/AgeOfGold.Checks"), "--",
                    "source-shader-compile", str(inputs), str(converted)], cwd=repo, check=True)
    comparisons = []
    for fixture, mode in (("pixel", 0), ("pixel", 3), ("selector", 0), ("selector", 3), ("selector", 7)):
        values = []
        for name, source, suffix in (("source", inputs, ".dxbc"), ("translated", converted, ".dxil")):
            output = root / f"{fixture}-{name}-{mode}.bin"
            run([path(source / (fixture + suffix)), path(output), str(mode), path(source / ("vertex" + suffix))])
            data = output.read_bytes()
            assert len(data) == 64 * 4 * 4, "Incomplete graphics readback"
            row = struct.unpack("<256f", data)
            assert all(math.isfinite(v) for v in row), "Non-finite graphics result"
            values.append(row)
        expected = []
        for index in range(64):
            x = (index - 16) / 16
            expected.extend((min(abs(x), 1), abs(x) * (1 if mode == 0 else 10), abs(x) + 1.1 * x*x, -x))
        if fixture == "selector":
            expected = ((mode, mode + 1, mode + 2, mode + 3) if mode <= 3 else (-1, -2, -3, -4)) * 64
        cpu_error = max(abs(a-b) for a, b in zip(values[0], expected, strict=True))
        conversion_error = max(abs(a-b) for a, b in zip(*values, strict=True))
        assert cpu_error < 0.000002, f"Source GPU output disagrees with CPU expectation: {cpu_error}"
        assert conversion_error < 0.000002, f"Translated GPU output differs: {conversion_error}"
        comparisons.append({"fixture": fixture, "mode": mode, "values": 256, "cpuMaximumError": cpu_error,
                            "conversionMaximumError": conversion_error})
    constants = (3.25, -.15625, 1.5, -.5) * 64
    for fixture, expected in (("resources", (.1, .45, 1, .5) * 64), ("constants", constants),
                              ("packed", constants), ("constants-sm4", constants),
                              ("matrices", (2.5, 0, 0, .625, -1.25, 3, 0, -.375) * 32)):
        values = []
        for name, source, suffix in (("source", inputs, ".dxbc"), ("translated", converted, ".dxil")):
            output = root / f"{name}-{fixture}.bin"
            run([path(source / (fixture + suffix)), path(output), "0",
                 path(source / ("vertex" + suffix)), "--resources" if fixture == "resources" else "--constants"])
            data = output.read_bytes()
            assert len(data) == 1024, "Incomplete binding graphics readback"
            row = struct.unpack("<256f", data)
            assert all(math.isfinite(v) for v in row), "Non-finite binding result"
            values.append(row)
        cpu_error = max(abs(a-b) for a, b in zip(values[0], expected, strict=True))
        conversion_error = max(abs(a-b) for a, b in zip(*values, strict=True))
        assert cpu_error < .000002, f"{fixture} GPU reference disagrees with CPU: {cpu_error}"
        assert conversion_error < .000002, f"{fixture} bindings changed: {conversion_error}"
        comparisons.append({"fixture": fixture, "values": 256,
                            "cpuMaximumError": cpu_error, "conversionMaximumError": conversion_error})
    matrices = (2.5, 0, 0, .625, -1.25, 3, 0, -.375) * 32
    for fixture, expected in (("constants", constants), ("packed", constants), ("constants-sm4", constants),
                              ("matrices", matrices), ("matrices-column", matrices), ("selector", (3, 4, 5, 6) * 64),
                              ("values", (2, -7, 5, 4, 6, -7, 5, 8) * 32), ("values-off", (0, -7, 5, 4, 0, -7, 5, 8) * 32)):
        parameters = converted / "parameters" / fixture
        program = "values" if fixture == "values-off" else fixture
        values = []
        for name, source, suffix in (("source", inputs, ".dxbc"), ("translated", converted, ".dxil")):
            output = root / f"{name}-bound-{fixture}.bin"
            run([path(source / (program + suffix)), path(output), "0", path(source / ("vertex" + suffix)),
                 "--constants", path(parameters / "b0.bin"), path(parameters / "b2.bin")])
            row = struct.unpack("<256f", output.read_bytes())
            assert all(math.isfinite(v) for v in row), "Non-finite named-parameter result"
            assert max(abs(a-b) for a, b in zip(row, expected, strict=True)) < .000002, f"{fixture} named values differ"
            values.append(row)
        difference = max(abs(a-b) for a, b in zip(*values, strict=True))
        assert difference < .000002, f"{fixture} named-parameter conversion differs"
        comparisons.append({"fixture": fixture, "namedParameters": True, "values": 256, "conversionMaximumError": difference})
    report = {"passed": True, "comparisons": comparisons,
              "converterFingerprint": (converted / "tool-sha256.txt").read_text().strip()}
    (root / "check.json").write_text(json.dumps(report, indent=2) + "\n")
    print("PASS: translated graphics match authored GPU reference and independent CPU expectations")


if __name__ == "__main__":
    main()
