# Visual comparison tooling — 7 November 2026

Research and proposal, not an accepted implementation or a proven capability. Riley asked to
pause lighting experiments and investigate tooling that lets the agent launch both games,
enter the world, position the camera, and capture matching views without repeated manual
navigation. The latest light-mask experiment did not improve the black chapel.

## Recommendation

Build a separate developer comparison harness with one controller and two game adapters.
Prefer existing external camera controllers where their provenance and licences permit use;
add only the command interface needed for exact camera control and state readback. Keep this
out of the build pipeline. A visual check must not become a runtime dependency of the mod.

The first proof should be one Firelink view captured twice in each game. Prove camera control
and repeatability before automating menus. Then complete unattended launch and loading, and
expand to the arrival, the dark chapel and the better-lit rock face. A manually loaded first
proof is an intermediate milestone; the requested outcome remains autonomous comparison.

## Proposed workflow

1. Validate both installs and the selected build. Launch one game offline with an isolated
   test save, or attach to a compatible test session. Observe startup and world-loaded state;
   do not infer readiness from a fixed sleep. Report a timeout or unexpected menu state.
2. Move the player or other streaming anchor near the intended view. Moving only the camera
   must not be assumed to load distant scenery. Wait for loading to settle before capture.
3. Apply a saved position, orientation and vertical field of view, then read them back from
   the game. The adapter owns axis, angle and FOV conventions. Check these against actual
   landmarks before calling views aligned.
4. Use the same resolution and aspect ratio. Hide HUD and player for scenery comparisons.
   Record time, weather, graphics settings and any freeze controls actually supported. Let
   streaming, exposure and temporal rendering settle before freezing or capturing. Preserve
   each game's normal lighting and postprocessing when comparing its appearance.
5. Capture the visible game rectangle, verify dimensions and state, and save image plus
   metadata outside the repository. Repeat the same view to measure variation from animation,
   particles and temporal rendering. Do not promise byte-identical screenshots.
6. Produce a local side-by-side view and aligned overlay, with optional luminance measurements
   over selected surfaces. Distinguish alignment errors from appearance differences. Pixel
   differences between engines are evidence to inspect, not an automatic fidelity verdict.

A saved view uses source-world coordinates, a camera orientation, vertical FOV, and a nearby
streaming anchor. The host adapter applies the build's actual source-to-host transform. The
current builder computes a placement offset but does not export a complete comparison-view
contract. Export and check that transform rather than independently reconstructing it from
the adjusted player spawn. Authored synthetic transforms can test the conversion without
committing real map coordinates.

Real view bookmarks, images, save clones, runtime addresses, and reports remain under the
system temp directory during the spike. Repository tests use only authored fixtures. A
reference capture can be reused while its source install and rendering conditions match;
routine host iterations need not launch the source game every time.

## What is available here

The existing host workflow already launches offline through me3 and selects a separate test
save. The inspected me3 checkout does not support the source game, so this cannot simply be
reused for both adapters. Source launch, offline enforcement and save isolation need their
own proof. An isolated Proton prefix is a candidate, but its Documents mapping and Steam Cloud
behavior must be checked before assuming that it protects the normal save.

This machine runs Hyprland. Both `hyprctl` and `grim` are installed, and window enumeration
works. There was no game window when inspected, so no automated game capture was demonstrated.
The first capture adapter can obtain the game rectangle from Hyprland and use grim's region
capture. It must verify the game is visible at the expected size; a hidden or covered window
would capture the wrong pixels. Menu input and focus control remain untested.

No camera dependency was installed, no controller was attached, and neither game was launched
for this investigation. No new game-derived output or third-party code entered the repository.

## Camera candidates and sources

| Candidate | Relevant evidence | Remaining gap | Licence |
| --- | --- | --- | --- |
| [FreecamMod](https://github.com/Logersnamed/FreecamMod) | Host free camera, saved states, FOV, freeze controls, HUD control and reported me3 support. Its camera-state implementation represents position, rotation and FOV. | No verified external command protocol; likely needs a small bridge. Installed host version and Proton compatibility untested. | MIT; dependencies need review before adoption. |
| [DSR Gadget](https://github.com/JKAnderson/DSR-Gadget) | Source position restore and follow-camera snapshot restore. Public hook interface exposes loaded state and position operations. | Camera snapshots do not establish arbitrary independent camera pose/FOV control. Windows .NET 4.7.2 application; archived; local compatibility untested. | GPL-3.0; not a production dependency decision. |
| [dscontrol](https://github.com/awforsythe/dscontrol) | Prior art for scripted source inputs and position recording; waits for a loaded character. | User still loads the character. Requires the Windows ViGEmBus driver, so not a direct fit for this Linux setup. | No root licence found in the inspected repository; do not copy or adopt code. |
| [DSR ImGui debug menu](https://github.com/horkrux/DSR-ImGuiDebugMenu) | Another source-game control candidate. | Minimal documentation; camera interface and provenance not established. | No root licence found; not selected. |
| [grim](https://github.com/emersion/grim) | Documented Wayland rectangle capture; already installed locally. Repository points to its newer SourceHut home. | Captures visible compositor pixels, not an independently rendered hidden game window. | MIT. |

Source interfaces inspected include FreecamMod's `camera_state_manager` and `free_camera`,
and DSR Gadget's `DSRHook`. No implementation or memory-layout constants were copied.
The local [me3](https://github.com/garyttierney/me3) configuration reference documents external
native DLL paths; its game enumeration excludes the source game. me3 is already used by the
spike, rather than a new dependency selected here. Its workspace declares MIT OR Apache-2.0.

## Decision needed before adopting camera controllers

The candidates use game-specific memory layouts or executable signatures. Constraint 1 in
`docs/core.md` forbids reliance on game-derived data supplied by third-party tools, while
`AGENTS.md` requires asking Riley about unclassified cases. Keeping such a tool outside the
repository does not by itself settle whether depending on its hooks is allowed.

Recommendation for Riley's decision: permit external camera controllers solely as local
development instruments, with their game-specific hooks kept outside Age of Gold and never
used by the shipped tool or build pipeline. This would not permit copied game code or data in
the repository. It still requires reviewing the selected controller and dependencies before
use. No such exception is assumed or recorded as accepted here. If it is not permitted, the
camera adapters need a separate investigation into deriving their runtime bindings from the
player's installs.

## Acceptance evidence for the first spike

- One view can be requested repeatedly in both games, with measured pose/FOV readback and
  matching landmarks. Capture the same view twice to distinguish scene changes from noise.
- Failures to attach, load, align or capture produce an explicit failed result rather than
  screenshots labelled as a successful comparison.
- Source and host save paths resolve to test data; installs remain unchanged; both sessions
  stay offline. Normal saves are never reset or overwritten to make automation easier.
- A subsequent command launches, enters the world, restores that view and captures it without
  Riley navigating. Only then expand the view set and resume the lighting investigation.
