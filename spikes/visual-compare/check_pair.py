#!/usr/bin/env python3
"""Exercise sequential comparison and reject changed player positions through the CLI."""
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


def check_repeated_pair():
    import os
    from PIL import Image
    with tempfile.TemporaryDirectory(prefix='aog-pair-proof-') as directory:
        root = Path(directory)
        Image.new('RGB', (16, 9), (32, 64, 96)).save(root / 'fixture.png')
        windows = [dict(address=f'0x{i+1}', **{'class': name}, title=name, mapped=True,
                        hidden=False, at=[0, 0], size=[16, 9], fullscreen=1)
                   for i, name in enumerate(('source', 'host'))]
        (root / 'windows.json').write_text('[]')
        (root / 'active.json').write_text(json.dumps(windows[0]))
        fake = root / 'hyprctl'
        fake.write_text('''#!/usr/bin/env python3
import json,pathlib,re,sys
root=pathlib.Path(__file__).parent
windows=json.loads((root/'windows.json').read_text())
if sys.argv[1:]==['-j','clients']: print(json.dumps(windows))
elif sys.argv[1:]==['-j','activewindow']: print((root/'active.json').read_text())
elif 'hl.dsp.focus(' in sys.argv[-1]:
 address=re.search(r'address:(0x[0-9]+)',sys.argv[-1]).group(1)
 (root/'active.json').write_text(json.dumps(next(w for w in windows if w['address']==address)))
 print('ok')
else: print('ok')
''')
        fake.chmod(0o755)
        fake = root / 'grim'
        fake.write_text('#!/usr/bin/env python3\nimport pathlib,shutil,sys\n'
                        "shutil.copyfile(pathlib.Path(__file__).with_name('fixture.png'),sys.argv[-1])\n")
        fake.chmod(0o755)
        stop = threading.Event()
        config = {}
        workers = []
        def adapter(endpoint):
            last = None
            # The follow camera differs; the stationary player determines validity.
            view = dict(VIEW, matrix=VIEW['matrix'][:12]+[50, 60, 70, 1])
            active = False
            while not stop.is_set():
                path = endpoint / 'request.txt'
                if path.exists():
                    tokens = path.read_text().split()
                    if tokens[0] != last:
                        if tokens[1] == 'release':
                            active = False
                        elif tokens[1] == 'set':
                            active = True
                            view = dict(matrix=list(map(float, tokens[2:18])), fov=float(tokens[18]))
                        response = dict(view, id=tokens[0], protocol=1, ready=True,
                                        active=active, player_anchor=[1, 2, 3], frame=123)
                        (endpoint / 'response.tmp').write_text(json.dumps(response))
                        (endpoint / 'response.tmp').replace(endpoint / 'response.json')
                        last = tokens[0]
                stop.wait(.01)
        for name in ('source', 'host'):
            endpoint = root / name
            endpoint.mkdir()
            for action in ('launch', 'shutdown'):
                script = root / f'{name}-{action}'
                script.write_text('#!/usr/bin/env python3\nimport json,pathlib\n' +
                    f'root=pathlib.Path({str(root)!r})\n' +
                    'windows=json.loads((root/"windows.json").read_text())\n' +
                    ('assert not windows, "Concurrent games"\n' +
                     f'windows=[{windows[0 if name == "source" else 1]!r}]\n'
                     if action == 'launch' else 'assert len(windows)==1\nwindows=[]\n') +
                    '(root/"windows.json").write_text(json.dumps(windows))\n' +
                    f'with (root/"lifecycle.txt").open("a") as log: log.write({(name+" "+action+chr(10))!r})\n')
                script.chmod(0o755)
            config[name] = dict(endpoint=str(endpoint), window_class=name, window_title=name,
                                player_anchor=[1, 2, 3], view=VIEW, startup=[],
                                launch=[str(root/f'{name}-launch')],
                                shutdown=[str(root/f'{name}-shutdown')])
            worker = threading.Thread(target=adapter, args=(endpoint,))
            worker.start()
            workers.append(worker)
        (root / 'config.json').write_text(json.dumps(config))
        try:
            result = subprocess.run([sys.executable, str(Path(__file__).with_name('pair.py')),
                                     str(root / 'config.json'), str(root / 'result'), '--launch'],
                                    env=dict(os.environ, PATH=str(root)+os.pathsep+os.environ['PATH']),
                                    capture_output=True, text=True, timeout=60)
        finally:
            stop.set()
            for worker in workers:
                worker.join()
        assert result.returncode == 0, result.stderr
        report = json.loads((root / 'result' / 'report.json').read_text())
        assert report['pixel_size'] == [16, 9]
        assert len(report['captures']) == 4
        for capture in report['captures']:
            with Image.open(capture['image']) as image:
                assert image.size == (16, 9)
                assert image.getpixel((0, 0)) == (32, 64, 96)
            metadata = json.loads(Path(capture['metadata']).read_text())
            assert metadata['camera']['matrix'] == VIEW['matrix']
        assert (root / 'result' / 'index.html').exists()
        assert (root / 'lifecycle.txt').read_text().splitlines() == [
            'host launch', 'host shutdown', 'source launch', 'source shutdown']
        assert json.loads((root / 'windows.json').read_text()) == []


check_repeated_pair()
print('PASS: two games each produce two checked captures and a comparison report')
