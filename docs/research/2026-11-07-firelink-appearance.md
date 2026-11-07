# Firelink appearance experiments — 7 November 2026

## Finding: rigid source scenery still has transforms

Static scenery is not necessarily stored directly in model space. In the inspected source
models, a vertex selects an entry in its mesh's node palette even when weighted deformation is
disabled. The selected node and its parents supply the transform. Using the mesh's default node
or treating the vertex as already transformed loses the placement of grass and climbing plants.
The missing vegetation was therefore not solely an opacity problem.

The spike now bakes that rigid hierarchy before writing geometry. Positions use the composed
transform; normals use its inverse transpose and are normalized. Weighted vertices, absent
palette entries, invalid or cyclic parents, singular transforms and mirrored transforms stop the
conversion. Mirroring needs a separate triangle-facing translation; it is not silently accepted.

An authored two-node fixture checks palette selection independently of the mesh's default node,
parent composition and nonuniform normal scaling. Its expected position and normal were worked
out before implementation. The passthrough implementation failed that check; the baker passes.
Invalid palette, cycle and weighted-vertex cases also pass their rejection checks.

## Local sky, fog and material preview

An external experimental converter adds source colour textures and sky geometry, separates
opaque and cutout material choices, and applies the rigid transform baker. Native material
presets must be referenced by the complete path expected by the host. A bare generic name
produced grey surfaces in the first attempt. Custom material definitions can retain the native
shader setup while carrying source colour and intensity inputs, although not every input's
visual effect is established.

The final local preview visibly restores grass and ivy in the three compared views. Lowering an
alpha threshold alone did not restore them. Source colour texture payloads are retained, with
colour-format headers adjusted for the host; the sky's visible colour/block artifacts show that
its colour handling still needs investigation.

The fog experiment changes only existing volumetric-fog controls. A constant-density attempt
washed out the sky, and a short depth cutoff introduced a visible boundary. Height-dependent
fog softens the horizon while retaining cloud detail. It is a local approximation, not a proven
translation of the source's fog tables or atmospheric scattering. Restoring the changed values
reproduces the uncompressed input graphics bytes exactly.

The combined preview retains the earlier room-environment repair. Readback and file comparisons
show unchanged map placement, collision and other pre-existing files except the rewritten visual
models and graphics settings. Two builds contain the same 341 files, byte for byte. Three matched
views, repeated twice at noon, have checked camera/FOV, player anchor and host clock/weather
readback. All three views were inspected. The local report's view/repetition selectors and
comparison overlay were exercised in headless Chromium.

## Remaining differences

The cliff is harsher than the source, the sky colour has artifacts, and host gold particles remain.
Normal/specular textures, layered materials, moving cloud layers and faithful translucency are
unfinished. Some source alpha-blended surfaces currently use a cutout approximation. The preview
has not been checked at night. This is appearance progress, not a complete match or a production
material decision. The experimental material bindings, game-derived metadata, builds and images
remain outside the repository.

## Player review

Riley played the appearance preview and reports it works well, with the same remaining visual
problems identified in the comparison. This adds in-play confirmation of the preview, not a
complete appearance match. Riley also noticed aqueduct oddities and explicitly deferred them:
the current appearance pass focuses on Firelink Shrine proper.

## Sources and licensing

- Own installed source and host games: proprietary; read locally, no data included here.
- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT): GPL-3.0. The existing spike
  dependency supplies parsed model nodes and their local-transform method. No library source or
  game-specific fixtures copied into this change; no production dependency decision.
