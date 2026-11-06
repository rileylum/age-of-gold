# Spike: Firelink rendering diagnostics

A snapshot as of 7 November 2026, following the arrival play check. Not a spec.
Riley authorized continuing the Firelink demo spike through useful play checkpoints.

## Finding

The diagnostic intended to write opaque white vertex colours, but called
`new FLVER.VertexColor(1, 1, 1, 1)`. SoulsFormatsNEXT has both byte and float overloads.
These integer constants select the byte overload, which divides each channel by 255.
Consequently, the saved colour and alpha channels were almost zero. The earlier readback
checked triangle positions and indices, but not these shader inputs.

The writer now passes `1f` for each channel, selecting normalized floating-point values.
Readback requires every written vertex to have colour data with full white and alpha.
This corrects a demonstrated output error. Riley's play check confirmed that the textures
are now visible, but distance/angle-dependent fading remains and the ground is invisible.
Riley can still stand and walk, so collision continues to support the player. Rendering
remains unsuitable for assessing navigation. The supplied screenshot stays outside the repository.

Inspecting the host template also showed that its selected mesh has only one face set,
without distance-detail flags. The earlier suggestion that the writer discarded its LOD
sets does not explain this particular template. Its material inputs and an opaque face-set
field are still altered by the diagnostic, so a broader rendering issue remains possible.
The proposed intact-host comparison was deferred when the constructor bug was found.

The only rendering change for this checkpoint is the normalized colour arguments. Geometry,
material selection, face-set settings, collision, placement and arrival logic are unchanged.
The diagnostic still displays collision surfaces with a host material; source visual models
and textures remain future work.

## Verification

The new check was added before the fix. Running the real-build workflow failed at the first
visible model with `Diagnostic terrain must read back with opaque white vertex colours`;
both collision sets had already passed. The failed output is under
`/tmp/aog-firelink-colour-red` and must never be committed or shared.

The source workflow was repeated after restoring the recorded temporary tooling. All
classified placements and both ordinary collision sets still match the reference reader,
and repeated extraction is identical. The existing transform and arrival fixtures pass.

After the correction, both full builds pass, including the new colour check on all visible
vertices. All 47 files match byte for byte between `/tmp/aog-firelink-colour` and
`/tmp/aog-firelink-colour-repeat`. Both collision sets also match the failed build's output
byte for byte. Publishing produced no warnings.

The playable profile is `/tmp/aog-firelink-colour/age-of-gold-firelink.me3`, with online play
disabled and the existing separate `AgeOfGoldSpike.sl2` save. Use New Game to exercise the
checked arrival. The play question was whether the terrain stays clearly visible while
moving and turning the camera; the result above is a partial improvement, not a pass.

## Tooling and boundaries

No new dependency or production decision was introduced. SoulsFormatsNEXT (`e25d1a5`, GPL-3.0)
supplies the `FLVER.VertexColor` overloads and serialization. HKLib, soulstruct and
soulstruct-havok remain at the versions and licences recorded in the preceding snapshots.
No community implementation or game data was added to the repository.

The temporary Wine prefix, Python environment and soulstruct checkouts were recreated at
the paths in the preceding handoff, including disabling soulstruct's automatic file logging.
The inspection project and host-derived output are under `/tmp/aog-render-inspect`.
All generated files stay under system temp; both installs are read-only.

## Following the invisible-ground report

Comparing triangle order with stored surface normals exposed another mismatch. The host
template's triangles overwhelmingly have a negative dot product between the triangle cross
product and the sum of their vertex normals. Our generated visible faces all have a positive
dot product: they use the collision writer's counter-clockwise convention directly.
The renderer's visible faces need the opposite order while retaining the outward normals.

This comparison reads the player's host model and the generated diagnostic; no shader or
executable code is decompiled. The temporary inspector reports sign counts only. The host
comparison has a few exceptions and is not a validator for arbitrary native meshes. Our
diagnostic uses one flat normal per triangle, so every generated face can be checked strictly.

A new real-build check first failed with `Visible terrain must use clockwise faces relative
to its outward normals`, after both collision sets passed. The visible writer then reversed
the triangle indices while retaining the positions and outward normals. Readback checks
that each triangle retains its three corners and that its facing opposes its normals.
This translation is confined to visible models; collision retains its checked convention.

Both corrected full builds pass. All 47 output files match byte for byte across
`/tmp/aog-firelink-facing` and `/tmp/aog-firelink-facing-repeat`; publishing had no warnings.
Compared with the colour checkpoint, only the 39 visible models and the build report change.
Collision, map layout, opening events and profile are byte-identical. The next offline play
profile is `/tmp/aog-firelink-facing/age-of-gold-firelink.me3`, using the same separate save.
Continuing with the character already on Firelink terrain is sufficient for this rendering
check. Riley reported the same problems: fading and invisible ground remain. Matching the
host's facing convention did not resolve those symptoms.

## Comparing model construction

The next diagnostic places three floating samples in front of the starting player. Their
main visible geometry is the same host mesh, read from the player's install. Each spans
four units along its longest axis; their centres are six units ahead, two units above the
player placement and five units apart. Left and right follow the starting player's yaw.
The samples have no new collision and use the same map-part settings as the terrain.

- Left: the complete native FLVER bytes are unchanged. Map placement supplies uniform scale
  and translation.
- Centre: the complete native model is scaled and translated inside the file, with identity
  map placement. Its stored mesh, node and overall bounds follow the same transformation.
- Right: the selected native mesh goes through the existing visible-model writer, including
  generated normals, texture coordinates, vertex colours, face settings and calculated bounds.

All three use model-only binders, like the diagnostic terrain; the host template's grass
companion is excluded. The first two retain the native model's other mesh, while the right
sample exercises the writer's single-mesh output. These are rendering controls, not translated
source visuals. The original terrain remains present. The earlier facing change is retained
to follow the observed host convention, with no claim that it improved visibility in play.

The comparison check was written first and failed because the first sample file was missing.
After implementation it verifies sample placement and visible extents, exact native FLVER
bytes on the left, and matching geometry. Restoring only positions and bounds in the centre
sample must reproduce the native model's serialized bytes exactly, proving the other data
is preserved. The right sample's triangles must match the selected native triangles in
position and order; it also passes the writer's normal/facing and colour checks.

The native template's library-labelled mesh bounds are not conventional enclosing minima and
maxima. The centre control transforms those stored values without reinterpreting them; this
keeps that metadata difference available for diagnosis instead of silently normalizing it.

Both full builds pass, and all 50 files match between `/tmp/aog-firelink-samples` and
`/tmp/aog-firelink-samples-repeat`. Only the three sample files, map placements and report differ
from the preceding checkpoint. Terrain models, collision, opening events and profile are
unchanged. Publishing had no warnings. A temporary ray/triangle check found no intervening
terrain between an estimated starting camera position and the three sample centres; it does
not establish actual camera framing or visibility.

The offline play profile is `/tmp/aog-firelink-samples/age-of-gold-firelink.me3`, using the same
separate save. New Game places the player facing the row. Riley reported that the left sample
stays visible but is quite dark; the centre is almost always hidden; the right looks correctly
shaded but disappears before it leaves the screen. Two screenshots illustrate the premature
disappearance and remain outside the repository. This narrows attention to model placement and
mesh culling bounds, while the native sample's darkness may be a separate shading issue.

## Mesh culling bounds

The comparison exposed a format interpretation error in our writer. In the checked host
FLVER version (`0x2001A`), the library's mesh-bound fields have these meanings:

| Library property | Meaning observed in the host model |
|---|---|
| `Min` | Half-extents along the box's own axes |
| `Max` | Euler rotation in radians, applied Z, X, Y |
| `Unk` | Box centre in model coordinates |

Reading both native meshes and transforming their vertices into that box frame encloses
every vertex within floating-point rounding. Comparing rotation orders identified Z-X-Y;
treating the first vector as full size instead of half-extents cannot enclose the meshes.
This comes from the player's model data and ordinary geometry calculations, without reading
executable or shader code. The library's property names and comments had led us to write
corner coordinates and leave the box centre at zero. That explains why our bounds did not
follow the visible geometry, though the play check is still required to confirm the symptom.

Generated meshes now use unrotated boxes centred on their geometry, with half-extents and a
0.001-unit rounding margin. The centre comparison sample scales its native half-extents,
translates its native centre and preserves rotation. Header and node bounds retain their
existing minimum/maximum representation. Geometry, normals, colours and materials are unchanged.
The writer rejects other FLVER versions rather than applying this interpretation to them.

The new box decoder first passed both untouched native meshes. The old centre sample failed
the corrected bound-transformation check, and the real terrain workflow failed on invalid
half-extents before the encoding was fixed. Readback now checks enclosure of every generated
vertex and the transformed native control, as well as the existing geometry and metadata checks.
No new dependency was introduced; SoulsFormatsNEXT supplies the stored fields, and our own
code performs the mapping and geometry checks.

Both corrected full builds pass and all 50 files match byte for byte under
`/tmp/aog-firelink-bounds` and `/tmp/aog-firelink-bounds-repeat`. A separate comparison of
serialized models confirms that all 41 changed models differ only in mesh culling bounds.
The left native control, map, collision, opening events and profile are unchanged. The report
changes to record the new tool fingerprint. Publishing had no warnings.

The offline profile is `/tmp/aog-firelink-bounds/age-of-gold-firelink.me3`, with the same
separate save. Riley confirmed that everything remains rendered, the visible terrain matches
the collision, and lighting looks good. The culling correction is therefore proven in play
for this diagnostic. Riley could not yet judge exact geometry fidelity without the source
textures. The current visible geometry is still the source collision mesh, not its visual
models; the next step is reading and translating those models and their textures. Full-area
traversal and visual fidelity remain unproven. The supplied screenshot stays outside the repository.

## Source visual inspection

Read-only inspection after that play check resolved all 67 source map-piece placements to 63
visual models. These contain 330 meshes, distinct from the collision surfaces used by this
diagnostic. Every referenced material definition was readable, and all 208 explicit texture
references resolved inside parsed source texture packages. This establishes input availability,
not a working translation or host texture-loading path.

The materials include opaque surfaces, alpha cutouts, blended and additive surfaces, layered
textures, baked lighting, foliage, water and sky effects. Texture coordinates and material
assignments belong to the visual models; applying textures to collision surfaces would not
recover the source's appearance. The next checkpoint needs visual geometry and an explicit
material translation, with its limitations recorded. No material mapping was chosen or
implemented during this inspection, and the proven play build was not changed.

The temporary inspector used the same external SoulsFormatsNEXT checkout already recorded
above (GPL-3.0), without copying library code. Source-derived metadata and the inspector live
under `/tmp/aog-visual-inspect`; none enters the repository. No new dependency was introduced.
