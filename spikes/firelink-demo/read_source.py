"""Join the runtime Firelink layout with both collision sets for a solid-terrain diagnostic."""

import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile

if os.environ.get('CI'):
    raise RuntimeError('Install checks must never run in CI')
source = Path(os.environ['AOG_DARK_SOULS_REMASTERED']).resolve()
host = Path(os.environ['AOG_ELDEN_RING']).resolve()
layout_path, output = (Path(p).resolve() for p in sys.argv[1:])
if not output.is_relative_to(Path(tempfile.gettempdir()).resolve()):
    raise ValueError('Output must be under the system temp directory')
if any(output.is_relative_to(p) for p in (source, host, Path(__file__).resolve().parents[2])):
    raise ValueError('Output must be outside both installs and the repository')
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'dsr-collision'))
from probe import extract, guard_io
guard_io(output)
with (host / 'eldenring.exe').open('rb') as stream:
    if not stream.read(1):
        raise ValueError('Empty host executable')
layout = json.loads(layout_path.read_text())
if layout['source_map'] != 'm10_02_00_00':
    raise ValueError('This diagnostic handles Firelink only')
from soulstruct.containers import Binder

binders = {}
fingerprints = {'layout': layout['layout_sha256']}
for detail in ('l', 'h'):
    path = source / 'map/m10_02_00_00' / f'{detail}10_02_00_00.hkxbhd'
    binder = Binder.from_path(path)
    names = [entry.name.replace('\\', '/').split('/')[-1] for entry in binder.entries]
    if len(set(names)) != len(names):
        raise ValueError('Duplicate collision filenames')
    binders[detail] = dict(zip(names, binder.entries, strict=True))
    for file in (path, path.with_suffix('.hkxbdt')):
        fingerprints[file.name] = hashlib.sha256(file.read_bytes()).hexdigest()

parts = []
excluded = []
for placement in layout['parts']:
    for detail in ('l', 'h'):
        name = detail + placement['model'][1:] + 'A10.hkx.dcx'
        if name not in binders[detail]:
            raise ValueError(f'Missing placed collision file: {name}')
    if placement['role'] != 'solid':
        excluded.append({'name': placement['name'], 'role': placement['role']})
        continue
    if placement['disabled']:
        raise ValueError('Disabled solid collision requires event translation')
    part = dict(placement)
    for detail in ('l', 'h'):
        name = detail + placement['model'][1:] + 'A10.hkx.dcx'
        part[detail] = extract(binders[detail][name].get_uncompressed_data())
    parts.append(part)
result = {'spawn': layout['spawn'], 'parts': parts, 'excluded_roles': excluded,
          'fingerprints': fingerprints}
with output.open('x', encoding='utf-8') as stream:
    json.dump(result, stream, sort_keys=True, separators=(',', ':'), allow_nan=False)
    stream.write('\n')
print(f'Read {len(parts)} solid collision placements; {len(excluded)} special-role placements recorded outside this diagnostic')
