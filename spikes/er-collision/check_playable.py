"""Build and check the manual multi-section cage test using both local installs."""

import os
from pathlib import Path
import subprocess
import sys
import tempfile
import tomllib

if os.environ.get("CI"):
    raise RuntimeError("Install checks must never run in CI")

source = Path(os.environ["AOG_DARK_SOULS_REMASTERED"]).resolve()
host = Path(os.environ["AOG_ELDEN_RING"]).resolve()
with (source / "map/m10_02_00_00/l10_02_00_00.hkxbhd").open("rb") as stream:
    assert stream.read(1), "Empty source header"
output = Path(sys.argv[1]).resolve()
assert output.is_relative_to(Path(tempfile.gettempdir()).resolve()), "Output must be temporary"
assert not output.exists(), "Use a fresh output directory"
probe = Path(__file__).parent / "probe/bin/publish/Probe.exe"


def wine_path(path):
    """Address a Linux path through Wine's standard Z drive."""
    return "Z:" + str(path).replace("/", "\\")


env = dict(os.environ, WINEDEBUG="-all", WINEDLLOVERRIDES="winedbg.exe=d",
           AOG_DARK_SOULS_REMASTERED=wine_path(source), AOG_CAGE="2",
           AOG_ROUNDTRIP="1", AOG_SETS="h,l", AOG_REPLACE="020100", AOG_REFINEMENTS="4")
result = subprocess.run(["wine", str(probe), wine_path(host), "cagetest", wine_path(output)],
                        env=env, capture_output=True, text=True)
assert result.returncode == 0, result.stdout + result.stderr
expected = 12 * 4**4
for detail in ("h", "l"):
    assert f"{detail}020100: our {expected} triangles" in result.stdout, result.stdout
profile = output / "age-of-gold-cagetest.me3"
config = tomllib.loads(profile.read_text())
assert config["savefile"] == "AgeOfGoldSpike.sl2"
assert config.get("start_online") is False
assert config["packages"] == [{"path": "mod/"}]
print(result.stdout, end="")
print(f"PASS: playable cage has {expected} triangles per set, offline profile and separate save; {profile}")
