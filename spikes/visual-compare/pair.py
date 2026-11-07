#!/usr/bin/env python3
"""Repeat two calibrated native camera views and write a local comparison report."""
import argparse
import json
import math
from pathlib import Path
import time

import camera


def anchor(value):
    """Validate the player position that keeps the required scenery loaded."""
    if (not isinstance(value, list) or len(value) != 3 or
            any(type(v) not in (int, float) or not math.isfinite(v) for v in value)):
        raise ValueError('Player anchor must contain three finite coordinates')
    return value


def same_anchor(actual, expected):
    """Reject captures after the player has moved from the calibrated position."""
    if max(abs(a-b) for a, b in zip(anchor(actual), anchor(expected))) > .01:
        raise ValueError('player anchor differs from calibrated position')


def run(config, output, launch=False):
    """Capture two repetitions per game, rejecting changed anchors or dimensions."""
    output = camera.temporary_path(output)
    if output.exists():
        raise ValueError('Comparison output must be fresh')
    targets = []
    for name in ('host', 'source'):
        target = config[name]
        endpoint = camera.temporary_path(Path(target['endpoint']))
        anchor(target['player_anchor'])
        camera.pose(target['view'])
        targets.append((name, target, endpoint))
    if abs(config['source']['view']['fov'] - config['host']['view']['fov']) > 1e-4:
        raise ValueError('Calibrated native fields of view differ')
    output.mkdir(parents=True)
    (output / 'configuration.json').write_text(json.dumps(config, indent=2) + '\n')
    reports = []
    size = None
    for name, target, endpoint in targets:
        if launch:
            import start
            start.enter(target, output / f'{name}-startup')
        camera.request(endpoint, 'release')
        # lazy: one stationary streaming anchor; add anchor relocation when views leave Firelink.
        same_anchor(camera.ready(endpoint).get('player_anchor'), target['player_anchor'])
        if target.get('hidehud', False):
            camera.request(endpoint, 'hidehud')
        for repetition in (1, 2):
            actual = camera.request(endpoint, 'set', camera.pose(target['view']))
            if not actual['ready'] or not actual.get('active'):
                raise ValueError(f'{name} did not activate the requested view')
            camera.same_pose(actual, target['view'])
            same_anchor(actual.get('player_anchor'), target['player_anchor'])
            image = output / f'{name}-{repetition}.png'
            result = camera.capture(endpoint, target['window_class'], image, target.get('window_title'))
            report = json.loads(Path(result['metadata']).read_text())
            same_anchor(report['camera'].get('player_anchor'), target['player_anchor'])
            if size is not None and report['pixel_size'] != size:
                raise ValueError('Capture dimensions differ; comparison is incomplete')
            size = report['pixel_size']
            reports.append(result)
    (output / 'report.json').write_text(json.dumps({
        'captured_at': time.time(), 'captures': reports, 'pixel_size': size,
        'alignment': 'Supplied calibration; inspect matching landmarks in the overlay',
        'limits': 'Animation, particles, NPCs and player may differ; no fidelity score is inferred',
    }, indent=2) + '\n')
    (output / 'index.html').write_text('''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Age of Gold — local comparison</title>
<style>body{font:16px system-ui;background:#16191e;color:#eee;margin:24px}
button,input{font:inherit} .pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}
img{width:100%;display:block}figure{margin:0}figcaption{padding:8px 0}
.overlay{position:relative;margin-top:12px}.overlay img+img{position:absolute;inset:0;opacity:.5}
label{display:inline-flex;align-items:center;gap:12px;margin:16px 20px 16px 0}
@media(max-width:700px){.pair{grid-template-columns:1fr}}</style>
<h1>Firelink comparison</h1>
<p>Matching native camera views. Inspect landmarks before judging appearance. Animation,
particles and characters remain live; the two repetitions show that variation.</p>
<label>Repetition <select id="repeat"><option value="1">1</option><option value="2">2</option></select></label>
<div class="pair"><figure><img id="source" src="source-1.png" alt="Source game capture">
<figcaption>Dark Souls: Remastered</figcaption></figure>
<figure><img id="host" src="host-1.png" alt="Host game capture">
<figcaption>Age of Gold in Elden Ring</figcaption></figure></div>
<label>Host overlay opacity <input id="opacity" type="range" min="0" max="100" value="50"></label>
<div class="overlay"><img id="base" src="source-1.png" alt="Source alignment reference">
<img id="blend" src="host-1.png" alt="Host alignment overlay"></div>
<p><a href="report.json">Capture metadata</a> · <a href="configuration.json">Calibration and conditions</a></p>
<script>
const repeat=document.querySelector('#repeat');
repeat.addEventListener('change',()=>{for(const [id,name] of [['source','source'],['base','source'],['host','host'],['blend','host']])document.getElementById(id).src=name+'-'+repeat.value+'.png'});
document.querySelector('#opacity').addEventListener('input',event=>document.querySelector('#blend').style.opacity=event.target.value/100);
</script></html>
''')
    return output / 'index.html'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('configuration', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--launch', action='store_true')
    args = parser.parse_args()
    try:
        print(run(json.loads(args.configuration.read_text()), args.output, args.launch))
    except (ValueError, TimeoutError, OSError, KeyError) as error:
        raise SystemExit(str(error))
