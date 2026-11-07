#!/usr/bin/env python3
"""Exercise the controller CLI against an authored camera adapter response."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading

CAMERA = Path(__file__).with_name('camera.py')
VIEW = {'matrix': [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 20, 30, 1], 'fov': 1.0}


def check_mismatched_pose(change, message="camera readback differs"):
    with tempfile.TemporaryDirectory(prefix='aog-camera-check-') as directory:
        root = Path(directory)
        (root / 'view.json').write_text(json.dumps(VIEW))
        stop = threading.Event()
        def adapter():
            while not stop.is_set():
                if (root / 'request.txt').exists():
                    command_id = (root / 'request.txt').read_text().split()[0]
                    response = dict(VIEW, id=command_id, protocol=1, ready=True, active=True, frame=123)
                    change(response)
                    (root / 'response.tmp').write_text(json.dumps(response))
                    (root / 'response.tmp').replace(root / 'response.json')
                    return
                stop.wait(.01)
        worker = threading.Thread(target=adapter)
        worker.start()
        try:
            result = subprocess.run([sys.executable, str(CAMERA), '--endpoint', str(root),
                                     'apply', str(root / 'view.json')], capture_output=True, text=True, timeout=8)
        finally:
            stop.set()
            worker.join()
        assert result.returncode != 0, 'Controller accepted a different camera position as the requested view'
        assert message in result.stderr, result.stderr


check_mismatched_pose(lambda response: response.update(matrix=VIEW['matrix'][:12] + [99, 20, 30, 1]))
check_mismatched_pose(lambda response: response.update(fov=1.5))
print('PASS: mismatched position and FOV readback are rejected by the CLI')
check_mismatched_pose(lambda response: response.update(active="false"), "Invalid camera adapter response")
print("PASS: malformed active state is rejected by the CLI")


def check_ambiguous_window():
    with tempfile.TemporaryDirectory(prefix='aog-window-check-') as directory:
        root = Path(directory)
        (root / 'view.json').write_text(json.dumps(VIEW))
        fake = root / 'hyprctl'
        fake.write_text("#!/usr/bin/env python3\nimport json,pathlib,sys\n"
            "if sys.argv[1:] == ['-j','clients']:\n"
            " print(json.dumps([{'class':'game','address':'0x1'},{'class':'game','address':'0x2'}]))\n"
            "else:\n pathlib.Path(__file__).with_name('focused').touch();print('unexpected focus')\n")
        fake.chmod(0o755)
        stop = threading.Event()
        def adapter():
            last = None
            while not stop.is_set():
                path = root / 'request.txt'
                if path.exists():
                    command_id = path.read_text().split()[0]
                    if command_id != last:
                        response = dict(VIEW, id=command_id, protocol=1, ready=True, active=True, frame=123)
                        (root / 'response.tmp').write_text(json.dumps(response))
                        (root / 'response.tmp').replace(root / 'response.json')
                        last = command_id
                stop.wait(.01)
        worker = threading.Thread(target=adapter); worker.start()
        try:
            result = subprocess.run([sys.executable,str(CAMERA),'--endpoint',str(root),
                'capture',str(root/'shot.png'),'--window-class','game'],
                env=dict(os.environ,PATH=str(root)+os.pathsep+os.environ['PATH']),
                capture_output=True,text=True,timeout=8)
        finally:
            stop.set();worker.join()
        assert result.returncode != 0 and 'ambiguous' in result.stderr, result.stderr
        assert not (root/'focused').exists(), 'Ambiguous game windows were focused before rejection'

check_ambiguous_window()
print('PASS: ambiguous game windows are rejected before focusing or capturing')
