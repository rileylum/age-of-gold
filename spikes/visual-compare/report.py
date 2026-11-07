#!/usr/bin/env python3
"""Record a running offline playtest through an external read-only observer."""
import argparse
import datetime
import hashlib
import json
import math
from pathlib import Path
import subprocess
import time
import tomllib
import uuid

from camera import pose, temporary_path


def fingerprint(path):
    """Hash one file without modifying it."""
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def vector(value):
    """Validate a measured position at the observer boundary."""
    if (not isinstance(value, list) or len(value) != 3
            or any(type(v) not in (int, float) or not math.isfinite(v) for v in value)):
        raise ValueError('Player position must contain three finite numbers')
    return value


def observation(value, request_id):
    """Validate a fresh, loaded observation without requiring a controlled camera."""
    if value.get('protocol') != 1 or value.get('id') != request_id:
        raise ValueError('Observer response does not match this request')
    if value.get('read_only') is not True:
        raise ValueError('A read-only observer is required')
    if value.get('ready') is not True:
        raise ValueError('World is not loaded')
    if type(value.get('process_id')) is not int or value['process_id'] <= 0:
        raise ValueError('Observer process identity is invalid')
    if value.get('camera_mode') not in ('player', 'fixed'):
        raise ValueError('Unknown camera mode')
    map_id = value.get('map')
    if (not isinstance(map_id, list) or len(map_id) != 4
            or any(type(v) is not int or not 0 <= v < 256 for v in map_id)):
        raise ValueError('Invalid map identity')
    player = value.get('player', {})
    vector(player.get('physics_position'))
    vector(player.get('map_position'))
    yaw = player.get('yaw')
    if type(yaw) not in (int, float) or not math.isfinite(yaw):
        raise ValueError('Player yaw must be finite')
    pose(value)
    executable = value.get('executable_sha256')
    if (not isinstance(executable, str) or len(executable) != 64
            or any(c not in '0123456789abcdef' for c in executable)):
        raise ValueError('Observer must identify the measured executable')
    return value


def declared_build(profile):
    """Fingerprint the supplied offline profile and build without reading its save."""
    config = tomllib.loads(profile.read_text())
    if config.get('start_online') is not False:
        raise ValueError('The supplied profile must explicitly remain offline')
    packages = config.get('packages', [])
    if len(packages) != 1 or not isinstance(packages[0].get('path'), str):
        raise ValueError('The playtest report requires one build directory')
    root = (profile.parent / packages[0]['path']).resolve()
    if not root.is_dir():
        raise ValueError('Build directory is missing')
    files = {str(p.relative_to(root)): fingerprint(p) for p in sorted(root.rglob('*')) if p.is_file()}
    if not files:
        raise ValueError('Build directory is empty')
    return {'profile': str(profile), 'profile_sha256': fingerprint(profile),
            'directory': str(root), 'files': files,
            'provenance': 'User-supplied profile; file hashes do not prove which files the running game loaded'}


def record(observer, profile, output):
    """Write one private report without launching, focusing or controlling the game."""
    output = temporary_path(output)
    if output.exists():
        raise ValueError('Report output already exists')
    build = declared_build(profile)
    request_id = uuid.uuid4().hex
    started = time.time_ns()
    result = subprocess.run([str(observer), request_id], check=True,
                            capture_output=True, text=True, timeout=30)
    measured = observation(json.loads(result.stdout), request_id)
    finished = time.time_ns()
    player = measured['player']
    # The camera uses rebased physics coordinates; the player supplies the current map offset.
    offset = [m - p for m, p in zip(player['map_position'], player['physics_position'])]
    report = {
        'schema': 1, 'recorded_at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'sample_interval_unix_ns': [started, finished], 'observation': measured,
        'camera_map_position': [p + d for p, d in zip(measured['matrix'][12:15], offset)],
        'declared_build': build, 'observer_sha256': fingerprint(observer),
        'limits': 'Read-only snapshot; map-relative positions are not global coordinates. No screenshot, save or game file is included.',
    }
    with output.open('x') as stream:
        json.dump(report, stream, indent=2, allow_nan=False)
        stream.write('\n')
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--observer', required=True, type=Path)
    parser.add_argument('--profile', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    print(record(args.observer.resolve(), args.profile.resolve(), args.output))


if __name__ == '__main__':
    try:
        main()
    except subprocess.CalledProcessError as error:
        raise SystemExit('Observer failed: ' + error.stderr.strip()) from None
    except (ValueError, OSError, subprocess.TimeoutExpired) as error:
        raise SystemExit(str(error)) from None
