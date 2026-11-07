# Visual comparison tooling — 7 November 2026

Dated proposal and subsequent local spike evidence; later sections record what passed. Riley
asked to pause lighting experiments and investigate tooling that lets the agent launch both games,
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

## Local development decision

Riley approved using game data and third-party tools for local development, provided they are
not committed or shipped. This includes the camera controllers' runtime signatures and memory
layouts. Constraint 1 now records that boundary. It does not authorize changing either install
or using normal saves for destructive tests. No history rewrite is needed or performed for this
decision; external material stays outside the repository from the outset.

The first local host adapter may reuse FreecamMod's MIT-licensed camera knowledge and MinHook's
BSD-licensed hooking library outside the repository. The source adapter may use DSR Gadget's
GPL-3.0 interfaces locally. Preserve their licence notices. Neither is selected as a production
dependency or a change to Age of Gold's own licence.

## Acceptance evidence for the first spike

- One view can be requested repeatedly in both games, with measured pose/FOV readback and
  matching landmarks. Capture the same view twice to distinguish scene changes from noise.
- Failures to attach, load, align or capture produce an explicit failed result rather than
  screenshots labelled as a successful comparison.
- Source and host save paths resolve to test data; installs remain unchanged; both sessions
  stay offline. Normal saves are never reset or overwritten to make automation easier.
- A subsequent command launches, enters the world, restores that view and captures it without
  Riley navigating. Only then expand the view set and resume the lighting investigation.

## Local implementation results later the same day

The initial investigation above has now been followed by a developer spike in
`spikes/visual-compare/`. The command interface is ours; both game adapters and all game-specific
bindings remain external. This is not a production dependency decision.

Observed successes:

- Launched each game offline, entered the world with scripted menu input and used isolated saves.
  The source uses a complete cloned Proton prefix with its Documents path inside the clone.
  The host uses an offline me3 profile and a dedicated cloned save. Neither install was changed.
- Applied a requested camera frame/FOV, read it back, moved the view and restored it. A source
  follow-camera override initially changed only the character render path; inspection identified
  the scenery submission path and the corrected override visibly moved the world view.
- Moved the source's cloned character to Firelink and disabled its HUD through the normal menu.
  The host adapter can hide its HUD. Player/NPC hiding and animation freeze remain unimplemented.
- Captured the same Firelink view in both engines. Tree branches, arches and chapel windows
  align visually with the same native FOV. This makes the difference between the source's
  readable shaded stone and the host's almost-black shade directly inspectable.
- Checked source and host placement translation, rotation and scale across all 65 included
  scenery placements. Applied an additional measured host runtime-origin shift. This calibration
  belongs to that session; it is not evidence that a moving player or a new session shares it.

The repository controller rejects mismatched pose/FOV readback and ambiguous game windows.
The first repeated-pair attempt stopped on window focus; a second stopped on the normal follow
camera changing after a compositor focus switch. The pair command now checks player position
instead, because a follow-camera rotation is not evidence of player movement. The new external
position readback still needs its real-game restart proof.

Menu automation recognizes two successive frames of an externally stored template region,
checks focus before input, and stops on an unknown screen. Authored CLI checks cover these
failure cases without either install. Capture makes a game window fullscreen, waits four
seconds, checks camera readback on either side of the screenshot, and verifies dimensions.
This fixed settling interval is a temporary bound. It does not prove temporal rendering has
settled, and visible notifications still need inspection.

A fresh-launch attempt has not passed. With the source at its main menu, starting the host
left the host on a black screen and the source unresponsive. Normal key and close requests did
not recover either game. The cause is unresolved; concurrent startup is a hypothesis, not a
finding. The earlier individually captured alignment proof remains valid. A complete repeated
pair report and unattended fresh launch-to-capture remain required before claiming the requested
workflow is finished. No lighting build changed during this work.

Additional local tools used: llvm-mingw (LLVM/Clang and MinGW component licences), pefile
2024.8.26 (MIT), Capstone 5.0.9 (BSD-3-Clause), and the already-installed Pillow 13.0.0
(MIT-CMU). These supplied compilation, local executable inspection and menu-image comparison.
They and their outputs were not copied into the repository. Hyprland's own
[dispatcher documentation](https://wiki.hypr.land/0.57.0/configuring/core/dispatchers/)
was used for focus, fullscreen and key events. The external startup diagnostics also consulted
[phnt's thread-information declarations](https://github.com/winsiderss/phnt/blob/master/ntpsapi.h)
(MIT) without copying implementation code.


## Completed sequential launch proof

A subsequent complete run passed with both games initially closed. The controller launched the
host offline, recognized its menus, loaded the dedicated test save, restored the calibrated
camera and captured it twice. It then released the camera and completed the normal save-and-exit
UI before launching the source. The source followed the same sequence through its offline menu
and cloned save, with two captures and a normal exit. The report was written only after both
sessions exited. All four images were 2560 by 1440 and passed pose, FOV and player-position
readback. Visual inspection found aligned tree branches, arches and chapel windows, with no
menu or desktop overlays. Animated particles and character poses vary between repetitions.

Two additional restart issues were exposed and corrected during this proof:

- The source renderer could retain the tiled window's earlier drawable size when fullscreen
  was requested during startup. Its title text then shifted enough for template recognition
  to reject it. A second resize after the initial focus settled corrected the observed case.
- Positions changed slightly when reloading the evolving test saves. Both external launchers
  now restore fixed checkpoints into their own isolated test saves before launch. They refuse
  restoration while the corresponding game process is running. Normal saves are not selected.
  Positions were measured again from those checkpoints, and the final run passed the existing
  strict checks without relaxing tolerances.

The generic runner now calls an external normal-exit command between games and requires the
window to disappear. A failed exit stops the run and leaves processes running. There is no kill
fallback. The earlier hung source process was closed only after Riley explicitly authorized
that exception; the earlier hung host finished exiting during cleanup. That exception does not
apply to future sessions.

All authored CLI checks pass, including a full sequential workflow using two fake adapters and
a rejection when a player position changes. The real run's local configuration records hashes
for both executables, both native adapters, graphics settings, checkpoints, launch helpers and
every file in the selected build. These are recorded evidence, not automatic install-version
compatibility detection. Game-specific bindings, menu images, saves, native dependencies and
captures remain external. A private local archive outside Git preserves them across temporary
directory cleanup; it is not a distributable artifact.

This establishes the first autonomous Firelink comparison. It does not establish arbitrary
map navigation, automatic recalibration, player hiding, frozen time/weather or deterministic
pixels. The source shows readable shaded masonry where the current host build is almost black.
No lighting candidate was changed as part of this proof. The next lighting work can use this
comparison without Riley manually positioning cameras or supplying screenshots.

## Background capture, later on 7 November

Riley asked to keep using the Arch/Hyprland desktop during comparisons. A private headless
Gamescope compositor now supplies a separate Xwayland display, input focus and screenshot path.
The desktop compositor is not reconfigured. A virtual output in the same compositor would still
share its input focus, so the experiment uses a separate compositor process instead.

Gamescope and its missing library were extracted from official Arch packages into a temporary
private directory. Their SHA-256 hashes match the existing package database. No system package
was installed. A private Python environment supplies Xlib. The external compatibility adapter
implements only the window, focus, key, close and capture operations used by the existing harness;
unsupported operations fail. It checks the private display and compositor identity before input,
and refuses the desktop display. Gamescope supplies base-plane screenshots directly. Its process
wrapper scopes environment and PATH changes to the capture subprocess tree.

Proof proceeded from a Vulkan scene and an authored keyboard window to real games. The keyboard
window received the synthetic key while desktop focus stayed unchanged. Both games then completed
a fresh sequential launch, menu navigation, world load, three fixed views repeated twice and normal
UI exit. All twelve captures passed camera, FOV, position and dimension checks; host clock and
weather readback stayed fixed. A separate desktop monitor sampled window/focus state during the
run and found no source or host game windows on the physical desktop. All three views from both
games were visually inspected. The host views' average channel values differed from corresponding
foreground captures by less than one displayed intensity level; animation still prevents pixel
identity, and this measurement is not a general colour-accuracy proof.

The private output is 2560 by 1440 at 30 Hz with a 30 fps limit, no scaling, SDR and compositor
colour management disabled. Rendering still uses the shared GPU, CPU and memory; this is input
and display isolation, not resource isolation or a VM. Audio also remains shared. The compositor
was stopped only after both games had exited, and an empty restart/stop check passed.

Two diagnostics failed before the successful fresh run. A different menu highlight required a
separately observed private-display template. The input helper's startup overhead made the old
keypress timing long enough to repeat menu movement; shorter private pulses fixed the exit path.
Both failures stopped on unrecognized screens rather than claiming success or terminating games.
Normal-exit recovery was verified visually. The successful fresh run is the acceptance evidence.

External entry points are `/tmp/aog-background/{start-compositor.py,run.py,stop-compositor.py}`.
Start the compositor, run the existing external suite through `run.py`, then stop it once normal
exits finish. The stop command refuses while private windows or either game remain. Reuse this
background wrapper for future comparisons instead of asking Riley to leave desktop input idle.
Keep sequential game launches, dedicated checkpoints and offline enforcement unchanged.

Additional sources and licences:

- [Gamescope source and command options](https://github.com/ValveSoftware/gamescope/blob/3.17.0/src/main.cpp):
  core BSD-2-Clause; the Arch package additionally declares BSD-3-Clause and LicenseRef-Reshade.
  Used as an external executable, not copied into the repository or chosen for the released tool.
- [Gamescope screenshot command](https://github.com/ValveSoftware/gamescope/blob/3.17.0/src/steamcompmgr.cpp):
  same project licensing; inspected to select base-plane capture.
- [python-xlib](https://github.com/python-xlib/python-xlib): LGPL-2.0-or-later; version 0.33 in the
  private environment. X11 input/window operations only; not shipped.
- libXres: MIT/X11, extracted from the Arch package as an external Gamescope dependency.
- [Hyprland compositor](https://github.com/hyprwm/Hyprland/blob/v0.57.0/src/Compositor.cpp) and
  [Aquamarine backend](https://github.com/hyprwm/aquamarine/blob/v0.15.1/src/backend/Backend.cpp):
  BSD-3-Clause. Read to assess an alternative separate session; no implementation adopted.

## Ordinary rendering camera correction — 8 November

The host debug camera suppresses distance blur. This invalidated earlier negative lens tests:
changing a lens setting while using that camera did not test ordinary play's postprocessing.
The private host adapter now overrides the ordinary rendering camera after its normal update,
without selecting debug-camera mode. Its command interface is unchanged; game-specific hooks
and bindings remain external.

Three repeated-view controls establish the distinction. The ordinary rendering override without
a lens change preserved the aligned landmarks and sharp distance. Adding a distance-blur trial
softened distant trees while keeping foreground stone sharp. The same build and lens trial under
the earlier debug camera remained sharp. A selected clear-sky patch's mean adjacent-pixel change
fell from about 0.45 to 0.24 displayed intensity levels only with the ordinary rendering override;
the source reference measured about 0.20. This is a local diagnostic measurement, not an image-wide
fidelity score or a completed translation of the source lens settings.

Each control captured the view twice, passed camera/FOV/anchor readback, and exited through the
normal UI. Matching landmarks support alignment. The ordinary override is now selected for
private host comparisons so that future material and atmosphere checks include normal
postprocessing. The user's ordinary play launcher and save were not changed. The source adapter
already overrides its rendering camera and was not changed in this experiment.
