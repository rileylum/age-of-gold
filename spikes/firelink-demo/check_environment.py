"""Check the Firelink room-lighting repair against both local installs."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

if os.environ.get('CI'):
    raise RuntimeError('Install checks must never run in CI')
source = Path(os.environ['AOG_DARK_SOULS_REMASTERED']).resolve()
host = Path(os.environ['AOG_ELDEN_RING']).resolve()
previous, output = (Path(arg).resolve() for arg in sys.argv[1:])
if not output.is_relative_to(Path(tempfile.gettempdir()).resolve()) or output.exists():
    raise ValueError('Use a fresh output directory under system temp')
if any(output.is_relative_to(p) for p in (source, host, previous, Path(__file__).resolve().parents[2])):
    raise ValueError('Output must be outside installs, input build and repository')
probe = Path(__file__).resolve().parents[1] / 'er-collision/probe/bin/publish/Probe.exe'


def windows(path):
    """Address a native path through the Wine Z drive."""
    return 'Z:' + str(path).replace('/', '\\')


wine_env = dict(os.environ, WINEDEBUG='-all', WINEDLLOVERRIDES='winedbg.exe=d',
                AOG_DARK_SOULS_REMASTERED=windows(source))
for args in (['firelink-environmentcheck'], ['firelink-environment', windows(previous), windows(output)]):
    run = subprocess.run(['wine', str(probe), windows(host), *args], env=wine_env, capture_output=True, text=True)
    assert run.returncode == 0, run.stdout + run.stderr
    print(run.stdout, end='', flush=True)

before = {p.relative_to(previous) for p in previous.rglob('*') if p.is_file()}
after = {p.relative_to(output) for p in output.rglob('*') if p.is_file()}
assert after - before == {Path('environment-repair-check.json')} and not before - after
changed = {p for p in before if (previous / p).read_bytes() != (output / p).read_bytes()}
assert changed == {Path('mod/map/mapstudio/m10_01_00_00.msb.dcx')}
report = json.loads((output / 'environment-repair-check.json').read_text())
assert report['restored_volumes'] > 0 and report['unchanged_input_files'] == len(before) - 1
assert len(report['tool_sha256']) == 64 and len(report['native_layout_sha256']) == 64
print('PASS: only the map changed; both installs matched the input build')
