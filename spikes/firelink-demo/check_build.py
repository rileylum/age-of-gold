"""Build and verify the manual Firelink terrain and textured-scene spike using both installs."""

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import tomllib

if os.environ.get('CI'):
    raise RuntimeError('Install checks must never run in CI')
source = Path(os.environ['AOG_DARK_SOULS_REMASTERED']).resolve()
host = Path(os.environ['AOG_ELDEN_RING']).resolve()
output = Path(sys.argv[1]).resolve()
if not output.is_relative_to(Path(tempfile.gettempdir()).resolve()) or output.exists():
    raise ValueError('Use a fresh output directory under system temp')
if any(output.is_relative_to(p) for p in (source, host, Path(__file__).resolve().parents[2])):
    raise ValueError('Output must be outside the installs and repository')
probe = Path(__file__).resolve().parents[1] / 'er-collision/probe/bin/publish/Probe.exe'

def windows(path):
    """Address a native path through the Wine Z drive."""
    return 'Z:' + str(path).replace('/', '\\')

def run(args, env=None):
    """Run a build step and expose its failure output."""
    result = subprocess.run(args, env=env, capture_output=True, text=True)
    assert result.returncode == 0, result.stdout + result.stderr
    print(result.stdout, end='', flush=True)

wine_env = dict(os.environ, WINEDEBUG='-all', WINEDLLOVERRIDES='winedbg.exe=d',
                AOG_DARK_SOULS_REMASTERED=windows(source))
run(['wine', str(probe), windows(host), 'firelink-transformcheck'], wine_env)
run(['wine', str(probe), windows(host), 'firelink-arrivalcheck'], wine_env)
with tempfile.TemporaryDirectory(prefix='aog-firelink-stage-') as temporary:
    layout, geometry = (Path(temporary) / name for name in ('layout.json', 'geometry.json'))
    run(['wine', str(probe), windows(host), 'firelink-layout', windows(layout)], wine_env)
    run([sys.executable, '-B', str(Path(__file__).with_name('read_source.py')), str(layout), str(geometry)])
    expected = json.loads(geometry.read_text())
    run(['wine', str(probe), windows(host), 'firelink-demo', windows(geometry), windows(output)], wine_env)
    profile = output / 'age-of-gold-firelink.me3'
    assert profile.is_file(), 'firelink-demo must write the checked playable build'
    config = tomllib.loads(profile.read_text())
    assert config['savefile'] == 'AgeOfGoldSpike.sl2'
    assert config['start_online'] is False
    assert config['packages'] == [{'path': 'mod/'}]
    report = json.loads((output / 'check.json').read_text())
    assert report['placements'] == len(expected['parts'])
    assert report['collision_parts_checked'] == 2 * len(expected['parts'])
    assert report['spawn_supported'] is True
    assert report['opening_arrival_checked'] is True
    visuals = report['source_visuals']
    assert visuals['models'] > 0 and visuals['placements'] > 0 and visuals['textures'] > 0
    assert report['visible_parts_checked'] == visuals['placements']
    assert visuals['geometry_checked'] and visuals['texture_payloads_checked']
    assert visuals['source_fingerprints'] and visuals['material_limits']
    assert len(list((output / 'mod/asset/aet/aet999').glob('*.tpf.dcx'))) == 2 * visuals['textures']
    assert (output / 'mod/event/m10_01_00_00.emevd.dcx').is_file()
    assert report['source_fingerprints'] == expected['fingerprints']
    assert report['host_fingerprints'] and len(report['tool_sha256']) == 64
    print(f"PASS: {visuals['placements']} source visual placements, {visuals['textures']} colour textures, both collision sets, supported spawn, offline profile; {profile}")
