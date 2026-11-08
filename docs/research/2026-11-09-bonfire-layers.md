# Bonfire filtering and tall flames — 9 November 2026

The private bonfire experiment now includes the tall central flame as well as the lower
flames. Its source-derived colour response passes a live GPU comparison. A separate
geometry comparison identifies why the first tall-flame candidate leans sideways: the
source rotates the sheet before stretching it, while the native host path rotates the
already stretched sheet.

The filtering comparison holds source particle corners, colours and atlas coordinates
fixed. Selecting an existing bound anisotropic sampler raises agreement among the selected
bright source pixels from about 77% to 97% within the same relative-error threshold. The
median relative error falls below 1%. Two address-mode alternatives produce identical
results at these captured coordinates. This establishes a filtering improvement, not exact
source sampler equivalence: the host anisotropy setting and source addressing still differ.

The first sampler patch changed the neighbouring volume sampler instead of the atlas
sampler. Its images therefore remained unchanged. The retained failed binding check names
that error; the corrected patch verifies the descriptor table and changes the atlas read.
The compiled private shader selects that sampler only for marked flame appearances.

Source draw exports distinguish the lower flames, tall central flame, additional narrow
flames, soft glow, sparks and a layered scrolling appearance. Comparing isolated draw
contributions shows that the tall flame supplies much of the missing brightness. Those
images use a common background for comparison; they are diagnostic composites.

The tall-flame writer reads the source definition and translates its lifetime, emission,
size curves, random ranges, acceleration, atlas animation, rotation and colour curves.
An independent reader checks the resulting effect. The lower-flame subtree remains
unchanged. The source texture's complete mip payload survives writing and GPU loading.
Repeated builds match, and unrelated effect and shader members retain their bytes.

A strict source emission-cadence hypothesis fails and remains recorded. A narrower check
finds feasible particle ages satisfying the captured colour, opacity, size, atlas and
movement bounds. This supports the appearance classification and candidate fields without
proving exact source emission phase, integration or random sampling. The live host particles
also pass those bounds. No tolerance was loosened to turn the cadence failure into a pass.

In the first two-layer live capture, the lower flame agrees exactly with the independent
pixel-response calculation. The tall layer differs by at most one stored HDR unit, and its
opacity target agrees exactly. Earlier targets remain byte-identical. Reverting to linear
filtering changes the flame pixels, confirming that the intended sampler is active.

The source geometry check reconstructs the captured corners from the particle inputs and
view constants. Rotating before the vertical stretch matches the source output within the
existing floating-point tolerance; rotating the stretched sheet fails clearly. The host
particle matrices independently confirm the opposite order. A scoped replay correction
matches the source transform calculation, produces an upright central flame, and leaves
the lower-flame vertices, colours, atlas coordinates and targets unchanged.

The next private build gives the tall appearance a distinct marker and applies the geometry
correction only to it. Independent readback finds that marker is the only changed effect
field. Both compiled shaders and both complete builds repeat exactly. A fresh live capture
shows an upright flame; its generated vertices agree with the source transform calculation.
The lower-flame upload contract and the tall-flame definition bounds still pass. Both host
sessions exit through normal menus and restore the ordinary camera profile. The existing
lighting runtime passes in both sessions without changes beyond separate trace destinations.

The corrected build also passes the independent pixel-response comparison. All affected
lower-flame pixels agree exactly; only one affected tall-flame pixel differs, by one stored
HDR unit. Both opacity targets agree exactly. The comparison uses the immediate target
before each appearance so intervening native effects do not inflate the measured footprint.

The correction remains private. The other appearances, exact timing and random behaviour,
source exposure beyond the captured daylight state, and the host resting-point effects
remain unfinished. No ordinary playtest update or production dependency decision follows
from this checkpoint.

Limited disk space is handled by lossless delta compression against an already preserved
capture. Reconstructing each new recording reproduces its complete hash and size. The
parent capture is an explicit preservation dependency. An initial decode needed a larger
memory allowance because the new recording exceeded the parent's window size.

All game-derived files, shader programs, captures and images remain outside the repository.
The private tools reuse the licences recorded in [the shading research](2026-11-09-bonfire-shading.md):
SoulsFormatsNEXT (GPL-3.0), the FXR library (Unlicense), RenderDoc (MIT), and DirectX Shader
Compiler with its LLVM/NCSA, component and distribution notices. The installed Zstandard
utility's licence file carries BSD-3-Clause terms. It is used only to preserve local evidence;
no compression library is added to the build.
