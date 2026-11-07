#!/usr/bin/env python3
"""Control an external local camera adapter without embedding game-specific bindings."""
import argparse
import json
import math
from pathlib import Path
import struct
import subprocess
import tempfile
import time
import uuid


def temporary_path(path):
    """Require game-derived output to stay under the system temporary directory."""
    path = path.resolve()
    if not path.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError('Camera data must stay under the system temporary directory')
    return path


def pose(view):
    """Validate an affine camera frame and a field of view in radians."""
    matrix, fov = view.get('matrix'), view.get('fov')
    if not isinstance(matrix, list) or len(matrix) != 16:
        raise ValueError('Camera matrix must contain sixteen numbers')
    values = matrix + [fov]
    if any(type(v) not in (int, float) or not math.isfinite(v) for v in values):
        raise ValueError('Camera pose must contain finite numbers')
    if not 0 < fov < math.pi:
        raise ValueError('Camera FOV must be between zero and pi radians')
    if any(abs(matrix[i] - v) > 1e-4 for i, v in zip((3, 7, 11, 15), (0, 0, 0, 1))):
        raise ValueError('Camera matrix must be affine')
    axes = [matrix[i:i+3] for i in (0, 4, 8)]
    for i in range(3):
        for j in range(3):
            if abs(sum(a*b for a, b in zip(axes[i], axes[j])) - (i == j)) > 1e-3:
                raise ValueError('Camera axes must be orthonormal')
    return values


def same_pose(actual, expected):
    """Reject an acknowledged command whose measured camera pose is different."""
    if max(abs(a-b) for a, b in zip(pose(actual), pose(expected))) > 1e-4:
        raise ValueError('camera readback differs from requested view')


def request(endpoint, operation, values=(), timeout=5):
    """Send one command and wait for its matching response."""
    command_id = uuid.uuid4().hex
    temporary = endpoint / 'request.tmp'
    temporary.write_text(' '.join([command_id, operation, *map(str, values)]) + '\n')
    temporary.replace(endpoint / 'request.txt')
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            response = json.loads((endpoint / 'response.json').read_text())
        except FileNotFoundError:
            time.sleep(.05)
            continue
        if response.get('id') == command_id:
            if response.get('error'):
                raise ValueError(response['error'])
            if (response.get('protocol') != 1 or type(response.get('ready')) is not bool
                    or type(response.get('active')) is not bool):
                raise ValueError('Invalid camera adapter response')
            if response['ready']:
                pose(response)
            return response
        time.sleep(.05)
    raise TimeoutError('Camera adapter did not acknowledge the command')


def ready(endpoint):
    """Read a camera only after the adapter reports a loaded world."""
    result = request(endpoint, 'status')
    if not result['ready']:
        raise ValueError('World is not loaded')
    return result


def record(endpoint):
    """Wait for the camera to settle after loading before recording its frame."""
    previous = None
    stable_since = time.monotonic()
    deadline = stable_since + 15
    while time.monotonic() < deadline:
        current = request(endpoint, 'status')
        if not current['ready']:
            previous = None
            stable_since = time.monotonic()
        else:
            values = pose(current)
            if previous is None or max(abs(a-b) for a, b in zip(values, previous)) > 1e-3:
                stable_since = time.monotonic()
            elif time.monotonic() - stable_since >= 2:
                return current
            previous = values
        time.sleep(.2)
    raise TimeoutError('Camera did not settle; no view recorded')


def focus(window_class, window_title=None):
    """Focus one unambiguous game window and make its capture rectangle visible."""
    windows = json.loads(subprocess.check_output(['hyprctl', '-j', 'clients']))
    candidates = [w for w in windows if w.get('class') == window_class
                  and (window_title is None or w.get('title') == window_title)]
    if len(candidates) != 1:
        raise ValueError('Game window is missing or ambiguous; specify its exact title')
    address = candidates[0]['address']
    if not isinstance(address, str) or not address.startswith('0x') or any(
            c not in '0123456789abcdefABCDEF' for c in address[2:]):
        raise ValueError('Invalid window address')
    active = json.loads(subprocess.check_output(['hyprctl', '-j', 'activewindow']))
    if (active.get('address') != address and active.get('fullscreen', 0)
            and active.get('class', '').startswith('steam_app_')):
        # Hyprland retains focus on a fullscreen game until its fullscreen state is released.
        subprocess.run(['hyprctl', 'dispatch',
                        'hl.dsp.window.fullscreen({action="unset"})'], check=True, capture_output=True)
    expression = f'hl.dsp.focus({{window="address:{address}"}})'
    result = subprocess.run(['hyprctl', 'dispatch', expression], check=True, capture_output=True, text=True)
    if result.stdout.strip() != 'ok':
        raise ValueError('Could not focus the game window')
    subprocess.run(['hyprctl', 'dispatch',
                    'hl.dsp.window.fullscreen({action="set"})'], check=True, capture_output=True)
    # lazy: settle for four seconds; replace with renderer signals when an adapter exposes them.
    time.sleep(4)
    window = json.loads(subprocess.check_output(['hyprctl', '-j', 'activewindow']))
    if window.get('address') != address or window.get('class') != window_class or not window.get('mapped') or window.get('hidden'):
        raise ValueError('Expected game window is not visible and focused')
    return window


def capture(endpoint, window_class, output, window_title=None):
    """Capture a focused game window with camera readback before and after."""
    output = temporary_path(output)
    metadata = output.with_suffix('.json')
    if output.exists() or metadata.exists():
        raise ValueError('Capture output already exists')
    if not window_class.replace('_', '').isalnum():
        raise ValueError('Invalid game window class')
    expected = ready(endpoint)
    if not expected.get('active'):
        raise ValueError('Apply a fixed camera view before capturing')
    window = focus(window_class, window_title)
    same_pose(ready(endpoint), expected)
    x, y = window['at']
    width, height = window['size']
    if min(width, height) <= 0:
        raise ValueError('Game window has no drawable area')
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(['grim', '-g', f'{x},{y} {width}x{height}', str(output)], check=True)
    with output.open('rb') as image:
        header = image.read(24)
    if len(header) != 24 or header[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('Capture is not a PNG')
    pixel_size = struct.unpack('>II', header[16:24])
    after_window = json.loads(subprocess.check_output(['hyprctl', '-j', 'activewindow']))
    if any(after_window.get(k) != window.get(k) for k in ('address', 'at', 'size')):
        raise ValueError('Game window changed during capture; image is not verified')
    actual = ready(endpoint)
    same_pose(actual, expected)
    report = {'camera': actual, 'window_class': window_class, 'logical_size': [width, height],
              'pixel_size': list(pixel_size), 'captured_at': time.time(),
              'capture': 'visible compositor rectangle; inspect for overlays'}
    metadata.write_text(json.dumps(report, indent=2) + '\n')
    return {'image': str(output), 'metadata': str(metadata)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--endpoint', type=Path, required=True)
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('status')
    sub.add_parser('release')
    sub.add_parser('hidehud')
    apply = sub.add_parser('apply')
    apply.add_argument('view', type=Path)
    save = sub.add_parser('record')
    save.add_argument('output', type=Path)
    shot = sub.add_parser('capture')
    shot.add_argument('output', type=Path)
    shot.add_argument('--window-class', required=True)
    shot.add_argument('--window-title')
    args = parser.parse_args()
    endpoint = temporary_path(args.endpoint)
    if args.command == 'apply':
        view = json.loads(args.view.read_text())
        result = request(endpoint, 'set', pose(view))
        if not result['ready'] or not result.get('active'):
            raise ValueError('Camera adapter did not activate the requested view')
        same_pose(result, view)
    elif args.command == 'record':
        output = temporary_path(args.output)
        if output.exists():
            raise ValueError('View already exists')
        result = record(endpoint)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2) + '\n')
    elif args.command == 'capture':
        result = capture(endpoint, args.window_class, args.output, args.window_title)
    else:
        result = request(endpoint, args.command)
    print(json.dumps(result))


if __name__ == '__main__':
    try:
        main()
    except (ValueError, TimeoutError, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error))
