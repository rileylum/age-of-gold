"""Exercise capacity checks through the command that launches a capture session."""

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


SCRIPT = Path(__file__).with_name("space.py")


def run(*arguments):
    return subprocess.run(
        [sys.executable, str(SCRIPT), *map(str, arguments)],
        capture_output=True,
        text=True,
    )


with tempfile.TemporaryDirectory(prefix="aog-space-check-") as temporary:
    root = Path(temporary)
    marker = root / "started"
    child = [
        sys.executable,
        "-c",
        "from pathlib import Path; import sys; Path(sys.argv[1]).write_text(sys.argv[2])",
        str(marker),
        "literal ; $() argument",
    ]
    result = run("--space", root, 0, 0, "--", *child)
    assert result.returncode == 0, result.stderr
    assert marker.read_text() == "literal ; $() argument"
    marker.unlink()

    result = run("--space", root, 10**12, 0, "--", *child)
    assert result.returncode != 0 and not marker.exists()
    assert "Insufficient capacity" in result.stderr, result.stderr

    # Two paths on one filesystem consume one combined budget.
    alias = root / "alias"
    alias.symlink_to(root, target_is_directory=True)
    result = run("--space", root, 2, 3, "--space", alias, 5, 1)
    report = json.loads(result.stdout)
    assert len(report["filesystems"]) == 1
    filesystem = report["filesystems"][0]
    assert filesystem["expectedBytes"] == 7 * 1024**3
    assert filesystem["reserveBytes"] == 3 * 1024**3
    assert filesystem["requiredBytes"] == 10 * 1024**3

    capacity = os.statvfs(root)
    free_gib = capacity.f_bavail * capacity.f_frsize // 1024**3
    if free_gib >= 2:
        claim = free_gib // 2 + 1
        result = run("--space", root, claim, 0)
        assert result.returncode == 0, result.stderr
        result = run("--space", root, claim, 0, "--space", alias, claim, 0, "--", *child)
        assert result.returncode != 0 and not marker.exists()
        assert "Insufficient capacity" in result.stderr, result.stderr

    for invalid in ["-1", "nan", "1.5"]:
        result = run("--space", root, invalid, 0, "--", *child)
        assert result.returncode != 0 and not marker.exists(), invalid
    result = run("--space", root / "missing", 0, 0, "--", *child)
    assert result.returncode != 0 and not marker.exists()
    result = run("--", *child)
    assert result.returncode != 0 and not marker.exists()

    result = run("--space", root, 0, 0, "--", sys.executable, "-c", "raise SystemExit(7)")
    assert result.returncode == 7, result.stderr

print("PASS: launch gating, shared filesystem budgets, invalid inputs and child exit status")
