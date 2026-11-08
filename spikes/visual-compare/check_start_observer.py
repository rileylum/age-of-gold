"""An ordinary session can start and exit without a camera-control adapter."""

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

from PIL import Image


MODULES = Path(__file__).resolve().parent


with tempfile.TemporaryDirectory(prefix="aog-observed-start-") as directory:
    root = Path(directory)
    Image.new("RGB", (32, 32), "white").save(root / "menu.png")
    window = dict(address="0x1", **{"class": "game"}, title="Game", mapped=True,
                  hidden=False, at=[0, 0], size=[32, 32], fullscreen=1)
    scripts = {
        "hyprctl": "import json,pathlib,sys\n"
            f"w={window!r}\n"
            "r=pathlib.Path(__file__).parent\n"
            "if sys.argv[1:]==['-j','clients']: print(json.dumps([] if (r/'closed').exists() else [w]))\n"
            "elif sys.argv[1:]==['-j','activewindow']: print(json.dumps(w))\n"
            "else: print('ok')\n",
        "grim": "import pathlib,shutil,sys\n"
            "shutil.copyfile(pathlib.Path(__file__).with_name('menu.png'),sys.argv[-1])\n",
        "shutdown": "import pathlib\npathlib.Path(__file__).with_name('closed').touch()\n",
        "observer": "import json,pathlib,sys\n"
            "r=pathlib.Path(__file__).parent\n"
            "count=r/'observations'; n=int(count.read_text())+1 if count.exists() else 1; count.write_text(str(n))\n"
            "value=json.loads((r/'observation.json').read_text()); value['id']=sys.argv[1]\n"
            "if n==1: value['ready']=False\n"
            "print(json.dumps(value))\n",
    }
    for name, source in scripts.items():
        path = root / name
        path.write_text("#!/usr/bin/env python3\n" + source)
        path.chmod(0o755)
    measured = {
        "protocol": 1, "read_only": True, "ready": True, "process_id": 123,
        "camera_mode": "player", "map": [11, 22, 33, 44],
        "player": {"physics_position": [1, 2, 3], "map_position": [4, 5, 6], "yaw": 0},
        "matrix": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
        "fov": 1, "executable_sha256": "0" * 64,
    }
    (root / "observation.json").write_text(json.dumps(measured))
    target = dict(endpoint=str(root), window_class="game", window_title="Game",
                  observer=str(root / "observer"), shutdown=[str(root / "shutdown")],
                  startup=[dict(name="continue", template=str(root / "menu.png"),
                                box=[0, 0, 1, 1], key="e", timeout=2, tolerance=10)])
    (root / "config.json").write_text(json.dumps(target))
    driver = root / "driver.py"
    driver.write_text("import json,sys\nfrom pathlib import Path\n"
                      f"sys.path.insert(0,{str(MODULES)!r})\nimport start\n"
                      "r=Path(__file__).parent; t=json.loads((r/'config.json').read_text())\n"
                      "start.enter(t,r/sys.argv[1],attach=True)\n"
                      "start.leave(t,r/sys.argv[1])\n")
    environment = os.environ | {"PATH": str(root) + os.pathsep + os.environ["PATH"]}
    result = subprocess.run([sys.executable, str(driver), "success"], env=environment,
                            capture_output=True, text=True, timeout=20)
    assert result.returncode == 0, result.stderr
    loaded = json.loads((root / "success/loaded.json").read_text())
    assert loaded["camera_mode"] == "player" and loaded["map"] == [11, 22, 33, 44]
    assert int((root / "observations").read_text()) >= 2
    assert (root / "closed").exists()
    assert not (root / "request.txt").exists(), "Camera-control request was sent"

    (root / "closed").unlink()
    (root / "observations").unlink()
    measured["read_only"] = False
    (root / "observation.json").write_text(json.dumps(measured))
    result = subprocess.run([sys.executable, str(driver), "rejected"], env=environment,
                            capture_output=True, text=True, timeout=20)
    assert result.returncode != 0
    assert "read-only" in result.stderr, result.stderr
    assert not (root / "rejected/loaded.json").exists()
    assert not (root / "closed").exists()

print("PASS: observer startup, loading retry, normal exit and read-only validation")
