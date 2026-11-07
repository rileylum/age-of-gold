#!/usr/bin/env python3
"""Start an externally configured offline session using observed menu states."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import time
import uuid
from PIL import Image, ImageChops, ImageStat

import camera


def matches(frame, template, box, tolerance):
    """Compare a menu region, giving empty screens a large error."""
    images = []
    for path in (frame, template):
        with Image.open(path) as image:
            image = image.convert('RGB').resize((1024, 576))
            images.append(image.crop(tuple(round(v*n) for v, n in zip(box, (1024, 576, 1024, 576)))))
    difference = ImageChops.difference(*images).convert('L')
    signal = ImageChops.lighter(*(image.convert('L') for image in images)).point(lambda p: 255 if p > 40 else 0)
    if not signal.getbbox():
        return False
    return ImageStat.Stat(difference, signal).mean[0] <= tolerance


def enter(target, output, attach=False):
    """Load an isolated game through known screens, leaving it running on failure."""
    output = camera.temporary_path(output)
    output.mkdir(parents=True, exist_ok=False)
    endpoint = camera.temporary_path(Path(target['endpoint']))
    steps = target['startup']
    for step in steps:
        camera.temporary_path(Path(step['template']))
        if not re.fullmatch('[a-z0-9-]+', step['name']):
            raise ValueError('Invalid startup step name')
        box = step['box']
        if (len(box) != 4 or not all(type(v) in (float, int) and 0 <= v <= 1 for v in box)
                or box[0] >= box[2] or box[1] >= box[3]):
            raise ValueError('Invalid startup template rectangle')
        if not re.fullmatch('[A-Za-z0-9_]+', step['key']):
            raise ValueError('Invalid startup key')
        if not 0 < step['timeout'] <= 120 or not 0 <= step['tolerance'] <= 30:
            raise ValueError('Invalid startup timeout or tolerance')
    def windows():
        return [w for w in json.loads(subprocess.check_output(['hyprctl', '-j', 'clients']))
                if w.get('class') == target['window_class'] and w.get('title') == target['window_title']]
    if not attach:
        if windows():
            raise ValueError('Game is already open; launch refused')
        command = target['launch']
        if not isinstance(command, list) or not command or any(not isinstance(v, str) for v in command):
            raise ValueError('Launch must be an argument list')
        camera.temporary_path(Path(command[0]))
        # A previous fixed view must not be applied while the new world is streaming.
        (endpoint / 'request.txt').write_text(f'{uuid.uuid4().hex} status\n')
        with (output / 'launch.log').open('wb') as log:
            process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
        (output / 'launcher.json').write_text(json.dumps({'pid': process.pid, 'command': command}))
    deadline = time.monotonic() + 120
    while not windows():
        if time.monotonic() > deadline:
            raise TimeoutError('Game window did not appear; see launch.log')
        time.sleep(.5)
    window = camera.focus(target['window_class'], target['window_title'])
    address = window['address']
    for index, step in enumerate(steps):
        deadline = time.monotonic() + step['timeout']
        image = output / f'{index}-{step["name"]}.png'
        consecutive = 0
        while True:
            current = json.loads(subprocess.check_output(['hyprctl', '-j', 'activewindow']))
            if any(current.get(k) != window.get(k) for k in ('address', 'at', 'size')):
                raise ValueError('Startup window changed; no further input sent')
            x, y = window['at']; width, height = window['size']
            subprocess.run(['grim', '-g', f'{x},{y} {width}x{height}', str(image)], check=True)
            consecutive = consecutive + 1 if matches(image, step['template'], step['box'], step['tolerance']) else 0
            if consecutive >= 2:
                break
            if time.monotonic() > deadline:
                raise TimeoutError(f'Unrecognized startup screen at {step["name"]}; inspect {image}')
            time.sleep(.3)
        print(f'Observed {step["name"]}', flush=True)
        for state in ('down', 'up'):
            expression = f'hl.dsp.send_key_state({{mods="",key="{step["key"]}",state="{state}",window="address:{address}"}})'
            subprocess.run(['hyprctl', 'dispatch', expression], check=True, capture_output=True)
            time.sleep(.18)
        time.sleep(.5)
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        result = camera.request(endpoint, 'status')
        if result['ready']:
            time.sleep(3)
            result = camera.ready(endpoint)
            (output / 'loaded.json').write_text(json.dumps(result, indent=2) + '\n')
            return result
        time.sleep(.5)
    raise TimeoutError('Menus completed but the world did not load')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('configuration', type=Path)
    parser.add_argument('target', choices=('source', 'host'))
    parser.add_argument('output', type=Path)
    parser.add_argument('--attach', action='store_true')
    args = parser.parse_args()
    try:
        enter(json.loads(args.configuration.read_text())[args.target], args.output, args.attach)
    except (ValueError, TimeoutError, OSError, KeyError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error))
