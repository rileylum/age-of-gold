# Local visual comparison spike

This developer tool applies measured camera views through two external adapters and captures
matching game windows. It is separate from the mod and its build pipeline. The first local
Firelink views have been aligned visually; unattended startup is still being validated.

The repository contains only the controller and authored checks. Camera DLLs, third-party
sources, executable inspection, save clones, real bookmarks, menu templates and captures live
outside the repository under the system temporary directory. None belongs in a release.

## Commands

Requires Python, Hyprland 0.57 and grim. Menu recognition additionally uses an installed
Pillow. Game adapters and local configuration must already be prepared outside the repository.
The adapters expose the same file protocol but own all game-specific knowledge.

```sh
python spikes/visual-compare/camera.py --endpoint /tmp/example-control status
python spikes/visual-compare/camera.py --endpoint /tmp/example-control record /tmp/example-view.json
python spikes/visual-compare/camera.py --endpoint /tmp/example-control apply /tmp/example-view.json
python spikes/visual-compare/camera.py --endpoint /tmp/example-control release
python spikes/visual-compare/pair.py /tmp/example-pair.json /tmp/example-comparison
```

`pair.py` requires both isolated games to be loaded and the players at their calibrated
positions. It captures each view twice and writes a side-by-side HTML report with an opacity
overlay. The destination must be fresh. A failed run has no success report; its partial images
remain available for diagnosis. `--launch` starts externally configured sessions first; this
path is experimental until the fresh-launch proof is complete.

`start.py CONFIGURATION source|host OUTPUT` runs an external offline launcher and steps through
local menu templates. `--attach` starts menu handling on an already-open test game. It observes
two matching frames before each key, checks focus, and stops on an unknown screen. A failure
leaves the game running. It never terminates a process or replaces a save.

Launch wrappers must enforce offline operation and select isolated saves. The generic
controller cannot establish those properties from an arbitrary command. The source uses a
cloned Proton prefix; the host uses an offline me3 profile with its own save. Do not point a
wrapper at normal saves. Neither install is modified.

## Local configuration

Each of `source` and `host` has:

- `endpoint`: external adapter directory under the system temp directory.
- `window_class` and `window_title`: exact game-window identity, excluding injector consoles.
- `player_anchor`: three measured player coordinates; movement beyond 0.01 units invalidates
  the calibration. This is independent of the follow camera, which can rotate on focus changes.
- `view`: `matrix` containing sixteen native affine frame numbers and `fov` in radians.
- `hidehud`: optional adapter command. The source currently uses its normal HUD-off option.
- `launch`: optional external launcher argument list, with an executable under temp.
- `startup`: observed menu steps with a name, local template PNG, normalized crop `box`, key,
  timeout in seconds and comparison tolerance. Templates must distinguish the actual state;
  a logo shared by multiple menus is insufficient.

Additional `calibration` and `conditions` metadata is preserved in the local report. Record
install/build fingerprints, the checked placement translation, the host's runtime origin,
resolution, native FOV convention, HUD state and graphics settings. Do not infer the transform
from an adjusted player spawn. Recalibrate if the build, map or streaming anchor changes.

## Adapter protocol

The controller atomically replaces `request.txt` with a unique ID, an operation and optional
space-separated numbers. Operations are `status`, `set` (sixteen frame values and one FOV),
`release`, and optionally `hidehud`.

The adapter atomically replaces `response.json` with `protocol: 1`, the matching `id`, boolean
`ready`, `active`, and a render `frame` counter. A ready response includes measured `matrix`,
`fov`, and `player_anchor`. An `error` rejects the operation. Acknowledging `set` is insufficient:
readback must match the requested pose. Set and readback happen at the renderer's camera
boundary, so a later game update cannot immediately overwrite the requested view.

The matrix storage convention is adapter-native. Both adapters were checked against visible
landmarks with the same numeric FOV; this does not establish a universal FOV convention for
other render paths. A recorded follow camera must settle after loading before it is used.

## Checks and limits

```sh
python spikes/visual-compare/check_camera.py
python spikes/visual-compare/check_pair.py
python spikes/visual-compare/check_start.py
```

These checks use authored frames and fake adapter/window responses, never either install.
They reject wrong camera position/FOV, ambiguous windows, changed player position, and input
on an unrecognized startup screen. Real game checks are manual local development runs.

The current scope is a stationary Firelink streaming anchor. Camera movement does not load
distant maps. Both games retain their own rendering and postprocessing. NPCs, player models,
wind and particles remain live; captures are not expected to match byte for byte. The report
supports inspection, not an automatic fidelity score. The four-second capture settling delay
is a temporary bound, not a renderer-completion guarantee. Compositor captures require visual
inspection for notifications or other overlays.
