"""Manual source-placement and collision workflow check; requires both installs, never CI."""

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
probe = Path(__file__).resolve().parents[1] / 'er-collision/probe/bin/publish/Probe.exe'

def windows(path):
    """Address a native path through the Wine Z drive."""
    return 'Z:' + str(path).replace('/', '\\')

with tempfile.TemporaryDirectory(prefix='aog-firelink-source-check-') as temporary:
    layout = Path(temporary) / 'layout.json'
    env = dict(os.environ, WINEDEBUG='-all', WINEDLLOVERRIDES='winedbg.exe=d',
               AOG_DARK_SOULS_REMASTERED=windows(source))
    result = subprocess.run(['wine', str(probe), windows(host), 'firelink-layout', windows(layout)],
                            env=env, capture_output=True, text=True)
    assert result.returncode == 0, result.stdout + result.stderr
    assert layout.is_file(), 'firelink-layout must write the source placements'
    placements = json.loads(layout.read_text())
    assert placements['parts'], 'Expected placed collision'
    assert all(part['role'] for part in placements['parts']), 'Every placement needs a classified role'
    for part in placements['parts']:
        assert len(part['position']) == len(part['rotation']) == len(part['scale']) == 3
    outputs = [Path(temporary) / name for name in ('first.json', 'second.json')]
    for output in outputs:
        result = subprocess.run([sys.executable, '-B', str(Path(__file__).with_name('read_source.py')),
                                 str(layout), str(output)], capture_output=True, text=True)
        assert result.returncode == 0, result.stdout + result.stderr
    assert outputs[0].read_bytes() == outputs[1].read_bytes(), 'Repeated extraction changed bytes'
    data = json.loads(outputs[0].read_text())
    expected = [part for part in placements['parts'] if part['role'] == 'solid']
    assert [p['name'] for p in data['parts']] == [p['name'] for p in expected]
    assert data['spawn'] == placements['spawn']
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'dsr-collision'))
    from check_installs import reference_reader
    from soulstruct.containers import Binder
    reference = reference_reader()
    for detail in ('l', 'h'):
        binder = Binder.from_path(source / 'map/m10_02_00_00' / f'{detail}10_02_00_00.hkxbhd')
        entries = {entry.name.replace('\\', '/').split('/')[-1]: entry for entry in binder.entries}
        for part, placement in zip(data['parts'], expected, strict=True):
            name = detail + placement['model'][1:] + 'A10.hkx.dcx'
            mesh = reference.from_bytes(entries[name].get_uncompressed_data())
            actual = part[detail]['meshes']
            assert len(actual) == len(mesh.meshes)
            for a, b in zip(actual, mesh.meshes, strict=True):
                assert a['vertices'] == b.vertices.tolist()
                assert a['faces'] == b.faces.tolist()
                assert a['material'] == b.material_index
    assert sum(len(m['faces']) for p in data['parts'] for m in p['l']['meshes']) > 0
    print(f"PASS: {len(placements['parts'])} classified placements; {len(expected)} solid placements match both reference collision sets; repeatable extraction")
