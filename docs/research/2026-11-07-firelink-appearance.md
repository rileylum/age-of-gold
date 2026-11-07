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

## Follow-up experiments: atmosphere and cliff contrast

Private background captures separated several possible causes of the remaining differences.
Changing only the sky texture's colour encoding overexposed it. Reducing its material intensity
recovered brightness but retained the colour blocks. Restoring source vertex RGB on the sky and
cliff surfaces also did not establish a correction. These experiments are not part of the next
preview. The alternative sky texture found in the install is not selected by this source map;
its presence alone does not justify substituting it.

The source's scene settings include distance blur and atmospheric scattering as well as fog.
Those are missing from the current translation. Distance-blur experiments in both the area's
graphics configuration and the shared configuration left the sky visibly unchanged. Their runtime
effect is unresolved; they are not a proven translation and are excluded from the candidate.

Increasing height-dependent fog improves the horizon transition and reduces the cliff's contrast.
A red material diagnostic confirms that the native lit shader reads the diffuse colour control.
Its material definitions are shared by rock, tree and masonry surfaces, so changing those shared
definitions would affect unrelated scenery. The candidate instead gives the nine cliff surfaces
three separate definitions and attenuates only their colour intensity. This remains a visual
calibration for the spike, not a source-derived production material rule.

Readback verifies the material colours and model references. Restoring those references reproduces
the previous model bytes; removing the added definitions reproduces the previous material binder.
Restoring the fog values reproduces the previous graphics bytes. Collision, placement and texture
payloads are unchanged. Two candidate builds contain the same 342 files byte for byte.

Three matched views were captured twice at noon and twice at night, with both runs ending through
the normal game UI. All three views were visually inspected at each clock. The cliff is less
harsh and the horizon blends more smoothly; shaded masonry remains readable. The static source
sky stays bright at night while the host scenery darkens. Sky colour blocks, that atmosphere
mismatch and the untranslated material features remain unresolved. The previous preview is
preserved separately. The private comparison report includes both previews and the source views.

The background menu observer twice stopped at a valid Continue screen because the animated
highlight exceeded its saved tolerance. The local capture configuration now accepts the measured
highlight variation while still rejecting the observed title screens. Camera, player-position and
clock checks are unchanged. Failed attempts remain separate from completed capture records.

## Sources and licensing

- Own installed source and host games: proprietary; read locally, no data included here.
- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT): GPL-3.0. The existing spike
  dependency supplies parsed model nodes and their local-transform method. No library source or
  game-specific fixtures copied into this change; no production dependency decision.
- [Souls Modding Wiki's graphics configuration research](https://www.soulsmodding.com/doku.php?id=tutorial:modifying-gparams):
  CC BY-SA 4.0, as stated on the page. Consulted as a secondary check on the available control
  families; the experiment outcomes above come from local readback and captures. No text or code
  copied into the implementation.
- [Soulstruct](https://github.com/Grimrukh/soulstruct): GPL-3.0-or-later according to the local
  package metadata. Its external host depth-of-field quality definition was inspected during
  research; no source copied and no new dependency introduced.
