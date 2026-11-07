#!/usr/bin/env python3
"""Reject a changed player position through the comparison command."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import threading

VIEW = {'matrix': [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 20, 30, 1], 'fov': 1.0}
with tempfile.TemporaryDirectory(prefix='aog-pair-check-') as directory:
    root = Path(directory)
    target = dict(endpoint=str(root), window_class='game', anchor=VIEW,
                  player_anchor=[1, 2, 3], view=VIEW)
    (root / 'configuration.json').write_text(json.dumps({'source': target, 'host': target}))
    stop = threading.Event()
    def adapter():
        last = None
        while not stop.is_set():
            path = root / 'request.txt'
            if path.exists():
                command_id = path.read_text().split()[0]
                if command_id != last:
                    response = dict(VIEW, id=command_id, protocol=1, ready=True,
                                    active=False, player_anchor=[9, 2, 3])
                    (root / 'response.tmp').write_text(json.dumps(response))
                    (root / 'response.tmp').replace(root / 'response.json')
                    last = command_id
            stop.wait(.01)
    worker = threading.Thread(target=adapter)
    worker.start()
    try:
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('pair.py')),
                                 str(root / 'configuration.json'), str(root / 'result')],
                                capture_output=True, text=True, timeout=10)
    finally:
        stop.set()
        worker.join()
    assert result.returncode != 0, 'Accepted a different streaming position'
    assert 'player anchor differs' in result.stderr, result.stderr
    assert not (root / 'result' / 'report.json').exists()
print('PASS: changed player position cannot produce a comparison report')
