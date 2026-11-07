#!/usr/bin/env python3
"""Check a bug report through the command line using an authored read-only observer."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

REPORT = Path(__file__).with_name('report.py')
SAMPLE = {
    'protocol': 1, 'read_only': True, 'ready': True, 'process_id': 123,
    'camera_mode': 'player', 'map': [1, 2, 3, 4],
    'player': {'physics_position': [2, 3, 4], 'map_position': [12, 23, 34], 'yaw': .5},
    'matrix': [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 5, 6, 7, 1], 'fov': 1,
    'executable_sha256': 'a' * 64,
}
with tempfile.TemporaryDirectory(prefix='aog-report-check-') as directory:
    root = Path(directory)
    (root / 'mod').mkdir()
    (root / 'mod' / 'authored.txt').write_text('authored build fixture')
    profile = root / 'test.me3'
    profile.write_text('start_online = false\nsavefile = "test.sl2"\n[[packages]]\npath = "mod"\n')
    observer = root / 'observer'
    observer.write_text('#!/usr/bin/env python3\nimport json,pathlib,sys\n'
        'assert len(sys.argv)==2\n'
        'p=pathlib.Path(__file__).parent\n'
        '(p/"requests").open("a").write(sys.argv[1]+"\\n")\n'
        'data=json.loads((p/"sample.json").read_text())\n'
        'data.setdefault("id",sys.argv[1]);print(json.dumps(data))\n')
    observer.chmod(0o755)
    def invoke(sample, name):
        (root / 'sample.json').write_text(json.dumps(sample))
        return subprocess.run([sys.executable, str(REPORT), '--observer', str(observer),
            '--profile', str(profile), '--output', str(root / name)],
            capture_output=True, text=True, timeout=10)
    result = invoke(SAMPLE, 'report.json')
    assert result.returncode == 0, result.stderr
    report = json.loads((root / 'report.json').read_text())
    assert report['observation']['camera_mode'] == 'player'
    assert report['camera_map_position'] == [15, 26, 37], report
    assert report['observation']['player']['map_position'] == [12, 23, 34]
    assert report['declared_build']['files'].keys() == {'authored.txt'}
    requests = (root / 'requests').read_text().splitlines()
    assert len(requests) == 1 and len(requests[0]) == 32
    result = invoke(SAMPLE, 'report.json')
    assert result.returncode != 0 and 'exists' in result.stderr, result.stderr
    assert (root / 'requests').read_text().splitlines() == requests
    for name, changes, error in [
        ('stale', {'id': '0' * 32}, 'request'),
        ('unloaded', {'ready': False}, 'loaded'),
        ('writable', {'read_only': False}, 'read-only'),
        ('badmap', {'map': [1, 2, 3, -1]}, 'map'),
        ('badcamera', {'fov': float('nan')}, 'finite'),
    ]:
        result = invoke(dict(SAMPLE, **changes), name + '.json')
        assert result.returncode != 0 and error in result.stderr, (name, result.stderr)
        assert not (root / (name + '.json')).exists()
    profile.write_text(profile.read_text().replace('false', 'true'))
    requests = (root / 'requests').read_bytes()
    result = invoke(SAMPLE, 'online.json')
    assert result.returncode != 0 and 'offline' in result.stderr
    assert (root / 'requests').read_bytes() == requests
    profile.write_text(profile.read_text().replace('true', 'false'))
    observer.write_text('#!/usr/bin/env python3\nimport sys\nsys.exit("World is loading; retry")\n')
    result = invoke(SAMPLE, 'loading.json')
    assert result.returncode != 0 and 'World is loading; retry' in result.stderr, result.stderr
    assert not (root / 'loading.json').exists()
print('PASS: ordinary camera observation, map-relative camera, build fingerprint, freshness, output protection and invalid responses')
