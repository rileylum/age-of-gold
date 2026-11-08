# Bonfire curve translation — 9 November 2026

The private host build now animates the bonfire's illumination from the colour
curve read from the source install. Its phase uses the host's native per-frame
clock. This replaces the preceding fixed measured colour; it does not establish
the source emitter's creation phase, its exact attachment, or its flame visuals.

## Source controls

The previous investigation established the colour conversion and a clock on the
same emitter. A mathematical interpolation model now passes all 960 retained
clock observations. Maximum normalized error is below five ten-millionths,
against the unchanged two-millionths tolerance. The linear prediction fails.

A fresh source control gives the colour channels different authored shapes:
nonlinear, linear and asymmetric. An independent linear radius supplies phase.
All 960 updates pass the existing absolute-plus-relative colour tolerance. The
control changes only the colour and radius nodes; other graph structure and
other bundle members remain unchanged. The game exits normally.

This control also resolves the auxiliary layout. The key colours form a
contiguous block, followed by pairs of angular controls for successive
segments. The preceding snapshot's description of two whole auxiliary planes
was incomplete: zero-valued controls could not distinguish these layouts.
The terminal pair's general role and other angular ranges remain unproven.

Private executable inspection and runtime observation identify a time-wrapping
step before interpolation. A delayed observer records the input time and native
returned colour without changing either. The model matches all 1,282 evaluations,
with maximum channel error below six hundred-millionths. An unquantized-time
prediction fails. The independent radius-clock check also passes all 960 GPU
uploads from this run, and the bonfire position stays unchanged.

Effect time and wall time advance at different rates in this capture setup.
The host trial therefore consumes its native frame clock, as the earlier moving
effects do. It does not use the measured wall-clock period or try to synchronize
the two games' independently created emitters.

Two instrumented sessions fail and remain preserved. A hardware-watchpoint run
records useful location evidence but crashes before normal shutdown. Installing
the later evaluator observer during startup also crashes before a window opens.
Enabling that observer only after the world loads succeeds and exits normally.
The precise crash causes are not established. Neither failed run is counted as
a successful session, and no game process was terminated by the harness.

## Host trial

A fresh install reader supplies key times, colours, angular controls and radius.
Repeated reads are identical. The private writer accepts the observed zero-control
curve and rejects nonzero controls rather than silently approximating them.
Two host builds match all 703 files. Only the composite shader archive and its
own build metadata differ from the preceding local-light build.

The composite shader consumes the existing native frame-clock buffer. The
runtime explicitly preserves that buffer's descriptor in its own draw allocation.
The light root constants, native root tables, player-light calculation and
bonfire-position calculation are unchanged. Allocation space grows, while the
concurrent-draw limit and fence-based reuse policy stay the same.

A background host run loads, relocates the player, captures a GPU frame, moves
the player at a fixed camera, and exits normally. The audit covers 3,697 draws,
with three descriptor slices used and no binding or lifetime errors. Bonfire
position error stays below the existing one-ten-thousandth tolerance. The
ordinary preview profile is restored.

GPU readback identifies the clock by the descriptor actually accessed. Selecting
it by buffer length was wrong because the allocation is padded; selecting it by
the source shader register was also wrong after translation to bindless Vulkan
resources. These failed exports remain beside the corrected descriptor-based
export. No acceptance tolerance changes.

The captured shader's exact floating-point colour results are extracted before
display-format rounding. The native time and six controlled times all pass the
fixed colour tolerance across 384,401 participating pixels, including both
extrema and the loop boundary. The previous fixed colour fails as a prediction
of the captured animated result. A same-frame phase control checks that the
resulting 217,516 changed pixels remain confined to the bonfire calculation's
region and the classified source scenery family, with none outside either.

## Limits and provenance

The trial still uses a measured attachment offset. Deriving that offset from
the source object and emitter remains next, followed by the flame translation.
Activation, rest, reload and emitter restart must eventually agree with the
visual effect's lifecycle. Broader Firelink fidelity, world isolation and the
NPC work remain incomplete. The ordinary play build is not promoted here.

This work uses the player's source install, privately inspected, and the existing
external SoulsFormatsNEXT (GPL-3.0), MinHook (BSD 2-Clause), DirectX Shader Compiler
(University of Illinois Open Source License, component notices and additional
Microsoft distribution terms, recorded in the surface-tangents research), RenderDoc
(MIT), SPIRV-Tools (Apache-2.0), and LLVM-MinGW (its LLVM compiler declares
Apache-2.0 with LLVM exceptions; runtime components carry their own notices).
No new community implementation was copied. Executable inspection, evaluator constants, generated
programs, third-party binaries, game data and captures remain outside the repository.
This snapshot records experiment results in our own words and makes no production
language, dependency or licence decision.
