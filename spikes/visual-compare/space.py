"""Check filesystem budgets before starting a local build or capture command."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    """Combine claims on each filesystem and launch only when all budgets fit."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--space", action="append", nargs=3, required=True,
        metavar=("PATH", "EXPECTED_GIB", "RESERVE_GIB"),
        help="existing destination directory, expected additional GiB, untouched reserve GiB",
    )
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    filesystems = {}
    for name, expected, reserve in args.space:
        try:
            expected, reserve = int(expected), int(reserve)
            if expected < 0 or reserve < 0:
                raise ValueError("GiB values must be nonnegative integers")
            path = Path(name).resolve(strict=True)
            if not path.is_dir():
                raise ValueError(f"Not a directory: {path}")
            device = path.stat().st_dev
            capacity = os.statvfs(path)
        except (ValueError, OSError, RuntimeError) as error:
            parser.error(str(error))
        available = capacity.f_bavail * capacity.f_frsize
        row = filesystems.setdefault(device, {
            "paths": [], "availableBytes": available,
            "expectedBytes": 0, "reserveBytes": 0,
        })
        row["paths"].append(str(path))
        row["availableBytes"] = min(row["availableBytes"], available)
        row["expectedBytes"] += expected * 1024**3
        row["reserveBytes"] = max(row["reserveBytes"], reserve * 1024**3)
    for row in filesystems.values():
        row["requiredBytes"] = row["expectedBytes"] + row["reserveBytes"]
        row["fits"] = row["availableBytes"] >= row["requiredBytes"]
    passed = all(row["fits"] for row in filesystems.values())
    print(json.dumps({"passed": passed, "filesystems": list(filesystems.values())}, indent=2), flush=True)
    if not passed:
        print("Insufficient capacity; command was not started.", file=sys.stderr)
        return 1
    command = args.command
    if command[:1] == ["--"]:
        command = command[1:]
    if not command:
        return 0
    returncode = subprocess.run(command).returncode
    return returncode if returncode >= 0 else 128 - returncode


if __name__ == "__main__":
    raise SystemExit(main())
