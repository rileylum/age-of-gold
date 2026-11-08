# Bonfire shading — 9 November 2026

The first native flame layer now uses a source-like colour response in a private live
build. A GPU capture proves that its compiled shader matches an independent replay
calculation exactly at the captured state. This is one appearance layer, not a complete
bonfire or an ordinary playtest update.

The preceding particle upload checks passed while the flame remained dim and red.
Reading the captured shading paths identifies a difference: the host separates colour
direction from colour strength, whereas the source applies its nonlinear response to
the combined texture and particle colour. The source also applies distance opacity and
an exposure factor. Changing only the colour operation overstates the correction.

A controlled replay supplies the host draw with the exact captured source particle
corners, colours, atlas coordinates and frame interpolation. All those generated outputs
match exactly. Native depth testing and the host scene remain in place. This removes
particle timing and random variation from the comparison. With native shading, none of
the selected bright source pixels agrees within the diagnostic relative-error threshold.
The source-response candidate greatly reduces that error and closely resembles the
isolated source draw on a common background.

The comparison is not pixel equivalence between games. Some edge and sampling error
remains. The source atlas uses anisotropic filtering; the captured host path uses linear
filtering. Their address modes also differ. These are observed differences, not proof that
filtering explains every remaining pixel. The full source screenshot includes other flame
layers, so the isolated comparison subtracts the source draw's preceding HDR target before
compositing its contribution onto the common background.

The live candidate changes one effect property to carry a private marker and replaces one
native pixel shader. Independent readback verifies that placement, particle behaviour,
colours, texture mip bytes and unrelated shader entries remain unchanged. Two builds match
across all 705 files. Shader generation also repeats exactly. The marker selects the new
response; other effects retain the native output path.

The successful live capture contains eleven particles. The existing size, radial-bound,
atlas, colour and opacity contract passes unchanged, and the marker reaches the GPU.
Replacing the compiled shader with an independently constructed replay program produces
byte-identical HDR and opacity targets. The flame changes 29,832 pixels in that frame.
Earlier draws remain byte-identical when the shader is replaced with the unmodified native
program. Removing the selected flame returns the preceding target exactly.

The first recording exhausted the temporary directory's user quota despite reported free
filesystem space. Its capture is corrupt, and one runtime trace record is incomplete; that
session cannot receive a full runtime pass. Only the stuck recording helper was terminated.
The game exited through its normal menu. A fresh session records on a separate shared-memory
filesystem, succeeds, exits normally and restores the camera profile. Its existing lighting
runtime checks pass over 2,308 draws. The only runtime source change is the trace destination.

Other retained harness failures include unavailable exporter field names and a readback
check that incorrectly expected an empty property and a constant property to have identical
tree shapes. Correcting those checks does not change the candidate.

Remaining work includes texture sampling, the other appearance layers, exact emission and
radial sampling, and lifecycle. The shader currently uses captured source daylight and
exposure state. The golden host resting-point effects remain visible. Full Firelink fidelity,
the world audit, NPC fidelity and the navmesh/enemy stretch remain unfinished.

All game-derived programs, builds, recordings and images remain outside the repository.
Private tools reuse the previously recorded licences: SoulsFormatsNEXT (GPL-3.0), the
[FXR library](https://github.com/EvenTorset/fxr) (Unlicense), RenderDoc (MIT), and DirectX
Shader Compiler with its LLVM/NCSA, component and distribution notices recorded in
[the surface-tangents research](2026-11-08-surface-tangents.md). No production dependency
or project licence decision is made here.
