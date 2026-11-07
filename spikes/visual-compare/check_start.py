#!/usr/bin/env python3
"""Unknown startup screens must time out without sending a key."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from PIL import Image

with tempfile.TemporaryDirectory(prefix='aog-start-check-') as directory:
    root = Path(directory)
    Image.new('RGB', (32, 32), 'white').save(root / 'expected.png')
    Image.new('RGB', (32, 32), 'black').save(root / 'unknown.png')
    window = dict(address='0x1', **{'class': 'game'}, title='Game', mapped=True,
                  hidden=False, at=[0, 0], size=[32, 32], fullscreen=1)
    fake = root / 'hyprctl'
    fake.write_text('#!/usr/bin/env python3\nimport json,sys,pathlib\n'
        f'w={window!r}\n'
        "if sys.argv[1:] == ['-j','clients']: print(json.dumps([w]))\n"
        "elif sys.argv[1:] == ['-j','activewindow']: print(json.dumps(w))\n"
        "else:\n"
        " if 'send_key' in sys.argv[-1]: pathlib.Path(__file__).with_name('key-sent').touch()\n"
        " print('ok')\n")
    fake.chmod(0o755)
    fake = root / 'grim'
    fake.write_text('#!/usr/bin/env python3\nimport pathlib,shutil,sys\n'
                   "shutil.copyfile(pathlib.Path(__file__).with_name('unknown.png'),sys.argv[-1])\n")
    fake.chmod(0o755)
    target = dict(endpoint=str(root), window_class='game', window_title='Game', startup=[
        dict(name='continue', template=str(root / 'expected.png'), box=[0, 0, 1, 1],
             key='e', timeout=.1, tolerance=10)])
    (root / 'config.json').write_text(json.dumps({'source': target}))
    result = subprocess.run([sys.executable, str(Path(__file__).with_name('start.py')),
                             str(root / 'config.json'), 'source', str(root / 'out'), '--attach'],
                            env=dict(os.environ, PATH=str(root)+os.pathsep+os.environ['PATH']),
                            capture_output=True, text=True, timeout=12)
    assert result.returncode != 0, 'An unknown screen was accepted'
    assert 'Unrecognized startup screen' in result.stderr, result.stderr
    assert not (root / 'key-sent').exists(), 'Input sent on an unknown screen'
print('PASS: unknown startup screen sends no key and cannot report success')
