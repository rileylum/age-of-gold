# Read-only observations of ordinary play — 7 November 2026

Riley asked for player and camera location reporting from a user-run game so future bugs can be
reproduced. The existing capture adapter only demonstrated a controlled comparison session.

The new `spikes/visual-compare/report.py` calls an external observer with a unique request
identifier, validates the returned pose and player/map identity, and writes a private JSON
report. It accepts the ordinary player camera. It does not launch or focus the game, send input,
change memory, read a save or take a screenshot. File fingerprints describe the supplied offline
profile and build; they are explicitly not claimed as proof of which files the game loaded.

The observer remains external. It opens the existing process with query/read permissions only,
resolves the current camera and player, and requires two consistent consecutive reads. It uses
the running game's Wine prefix and runtime. The executable fingerprint gates its private layout
bindings. No injected DLL, hook, writable handle or game mutation is part of this observation.
Both rebased physics position and map-relative player position are recorded. Their difference
also places the measured camera in the current map's coordinate frame. This does not establish
global coordinates or cross-map replay.

## Evidence

- The authored command-line workflow failed before implementation, then passed. It checks an
  ordinary camera, the derived camera position, build fingerprints, request freshness, loaded
  state, invalid coordinates, offline configuration and refusing to overwrite an existing report.
- An observer error was initially hidden by a subprocess exception. A new failing workflow check
  demonstrated that problem; the CLI now reports the observer's error and writes no report.
- A live observation during the camera comparison successfully read the fixed camera and player.
- A separate offline play session loaded the accepted scenery preview and a dedicated test save
  with **no camera DLL**. Menus were driven only on the private headless display. The observer
  recorded the ordinary player camera, current map and player location; process mappings confirmed
  the controller was absent. A private screenshot confirmed Firelink. The game exited through its
  normal UI. The user's dedicated playtest save was not selected or reset.

The real test proves observation for this installed host version and one loaded Firelink map.
Moving-camera sampling, loading transitions, a different game version and cross-map coordinate
stability require further evidence. Reports deliberately fail when a coherent observation is
unavailable. There is no claim that a bug report alone reproduces environment, animation or
persistent game state.

## Sources and licensing

The existing external [FreecamMod](https://github.com/Logersnamed/FreecamMod) checkout is MIT and
supplies camera/player layout references. The previously inspected
[Minecraft-Ring controller](https://github.com/siddoff/Minecraft-Ring/blob/main/bridge-base/elden-ring/er-bridge/src/game.cpp)
is MIT and supplies the distinction between rebased physics and map-relative player positions.
Their code, signatures, field bindings and game-derived metadata remain external, with notices
preserved. The private observer is independently written against Windows process-read APIs.
The repository contains only the generic protocol client and authored fixture. No production
library, language or licence decision is made here.
