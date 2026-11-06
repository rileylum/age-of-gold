# Spike: Firelink visual models and colour textures

A snapshot as of 7 November 2026. Not a spec or a production library decision.
Riley asked to continue after confirming stable rendering of the collision-based diagnostic.

## Intermediate build

The next diagnostic uses the source visual models in place of visible collision surfaces.
It writes 61 models, 65 placements and 296 mesh surfaces, using 63 source colour textures.
Source positions, triangle order, texture coordinates and backface settings are preserved;
normals are normalized and written in the host's packed representation. Map placements retain
source rotation and scale, with the same common translation as the collision build.

Opaque surfaces use a basic host colour material. Cutouts use its edge-tested counterpart.
The writer reads their sampler names from the host material binder and supplies explicit
texture bindings. Source colour images remain block-compressed: their image and mip payloads
are unchanged, while DDS headers declare sRGB and texture packages use the host's metadata.
New texture names occupy a checked-unused namespace. Both full and low-detail packages are
present; this checkpoint deliberately retains the complete source mip chain in both.

This is a colour-and-geometry diagnostic, not complete material translation. It uses the first
colour layer, neutral vertex colours, and host lighting. Secondary layers, source vertex
colours, normal/specular maps, baked lighting and foliage motion remain untranslated. Cutout
thresholds come from the host material. The report explicitly excludes 29 alpha-blended
surfaces, two additive surfaces, two water surfaces and one special sky surface. Unsupported
blend modes, vertex formats and texture formats still stop the build. Source detail variants
are classified, but this checkpoint writes only the highest-detail faces.

The three floating rendering controls and their now-unused writer were removed from the
current workflow. The preceding proven build remains under `/tmp/aog-firelink-bounds`.
The collision files, arrival event and offline profile are byte-identical to that checkpoint.

## Source vertex declarations

The first real-model run found invalid decoded vertex data. Investigation identified 13
source buffers whose declared fields do not match their stored vertex stride: eleven need a
tangent field, one needs a bitangent after an existing tangent, and one declares an extra
tangent. The generic library repair can remove a field when one needs adding, and it mutates
declarations shared by other buffers. A successful parse therefore did not establish correct
source geometry. An initial attempt to relax a normal-length check did not solve the issue.

The source adapter now reads metadata without vertices, clones each buffer's declaration,
and applies only those classified four-byte corrections. Each corrected declaration must
match the recorded stride before vertex decoding. The adapter uses the pinned library's
internal decoder through reflection; this is a throwaway-library constraint, not a proposed
production interface. It independently checks every decoded position against the leading
float vector at the recorded stride. No per-material correction table is used or imported.

Authored tests cover both additions, removal, shared-declaration isolation and rejection of
an unsupported stride. They failed before the correction was implemented. A separate authored
two-surface model tests material separation, face order, culling and texture coordinates beyond
the old signed-short range. It failed before the model writer existed, then passed with float
texture coordinates. The workflow's new visual-output check first failed against the preceding
build because it contained no translated source visuals.

## Verification and play

The real-installs workflow passes source reading, collision, arrival, geometry, model bounds,
texture-coordinate, material-binding, texture-name, texture-payload, placement and offline-profile
checks. All 13 source declaration mismatches are handled and recorded in the build report.
Publishing produced no warnings. A separate check of the written visual triangles found the
surface beneath the starting player within 0.06 units of the checked collision surface; this
is a local geometric check, not proof of whole-area alignment or engine rendering.

All 195 files match byte for byte between `/tmp/aog-firelink-textured` and
`/tmp/aog-firelink-textured-repeat`. The profile is
`/tmp/aog-firelink-textured/age-of-gold-firelink.me3`, using the same separate save. Geometry
fidelity, texture loading and basic materials are not yet proven in play.

The profile was launched with `me3 launch --online false`; me3 confirmed attachment. The log
is `~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_07-35-56.log`. Continuing the existing
test character is sufficient; arrival and the save profile have not changed.

### Texture play result and native UV packing

Riley's next screenshot shows the detailed scenery rendering, but with plain surfaces instead
of the source colour textures. The screenshot stays outside the repository. The loader log
confirms requests for the source visual models and multiple new full and low-detail texture
packages. This establishes that those paths resolve; it does not prove shader binding or
correct image sampling. The textured appearance is not yet proven in play.

One unproven change in the first writer was replacing the native packed UV layout with floats.
Readback preserved those numbers, but did not establish that the host shader interpreted them
correctly. The source uses a wider coordinate range: 36,384 vertices in this build exceed the
host's direct packed range, so merely changing the field type would corrupt their UVs.

The next check restores the host template's packed UV layout. A per-model power-of-two divisor
brings the stored coordinates into range; the material's repeat scale restores the source
coordinates. Readback requires that product to equal every source UV exactly, without rounding
loss. The authored wide-coordinate fixture also checks its expected fourfold sampler scale.
The new native-layout assertion failed on the float writer before the change and passes now.
The real-installs workflow passes for `/tmp/aog-firelink-packed-uv`. All 61 visual models differ
from the first textured candidate only in UV layout, stored coordinates and sampler scaling;
a separate serialized-model comparison verifies that restriction. The other 133 files are
identical, including all texture packages, collision, map placement, arrival and profile files.
The report changes only its tool fingerprint. The float-layout hypothesis still needs play
evidence; material selection, texture images and source geometry are unchanged.

All 195 files also match `/tmp/aog-firelink-packed-uv-repeat` byte for byte. Riley confirmed
exiting the preceding game process, and the new profile was launched offline with the same
separate save. me3 confirmed attachment; the log is
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_08-09-44.log`. Its play result is pending.

### Packed UV result and complete native material comparison

Riley's next screenshot still shows plain scenery without source colour patterns. Native UV
packing did not resolve the observed failure. The screenshot remains outside the repository.

Read-only inspection found three host meshes using the basic colour shader. Their vertex
declarations match the layout already used by our writer, but they reference GX material
settings; our newly constructed materials have no GX reference. The generic colour definition
also shares its basename with a different legacy definition. Neither observation establishes
the runtime cause. A scan of the Chapel's model declarations stopped at a reference whose
same-map archive path does not exist; the three inspected meshes come from two files read
successfully before that point. No missing model was silently substituted.

The next isolated comparison is `/tmp/aog-firelink-native-material/age-of-gold-firelink.me3`.
A temporary inspector starts with the checked packed-UV build and replaces its material setup
with the complete host material proven in the culling play check, including its GX settings.
Only that material's colour binding is overridden with each surface's existing source texture
and repeat scale. Its native secondary texture bindings are retained. This is deliberately a
material diagnostic: its opaque shading and host normal maps are not the final translation.

Readback checks all 243 material bindings across 61 models. Restoring the preceding materials
and GX lists makes each serialized model identical to the input, proving that no other model
field changed. All 134 other existing files are byte-identical, including every texture package,
collision, map, event and offline separate-save profile. The additional `material-check.json`
records the diagnostic and its tool fingerprint; the inherited `check.json` describes its
input build. All 196 files repeat byte for byte in `/tmp/aog-firelink-native-material-repeat`.
The inspector is `/tmp/aog-render-inspect/Program.cs`, mode `nativematerial`; its own source and
all derived output remain temporary. The repository's visual writer is unchanged pending play
evidence. No new dependency or production decision was introduced.

After observing that the preceding game had exited, the comparison profile was launched with
`me3 launch --online false` and the same separate save. me3 confirmed attachment; its log is
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_08-17-56.log`. The next play check is
whether source colour patterns appear with this complete native material.

## Sources and licences

No new dependency was introduced. SoulsFormatsNEXT (`e25d1a5`, GPL-3.0) supplies FLVER, MSB,
material, DDS and texture-package types. HKLib (`ac40bc9`, MIT), soulstruct (`12b6918`,
GPL-3.0-or-later), and soulstruct-havok (`bf2d41f`, GPL-3.0-or-later) remain at the previously
recorded versions. The installed soulstruct source documents Remastered's malformed vertex
declarations; its material-specific repair table and implementation were not imported or used
by this visual reader. Our correction follows the inspected strides and format fields.

The [Souls Modding FLVER reference](https://www.soulsmodding.com/doku.php?id=format:flver)
(CC BY-SA 4.0) documents material references and per-model texture overrides. The community's
[standalone texture tutorial](https://soulsmodding.wikidot.com/creating-standalone-retextured-assets-using-aegs-and-aets)
(licence not stated on the retrieved page) supplied background on texture packages and colour
space. No text, code, presets or screenshots from those pages were incorporated.

All game-derived inspection output remains under `/tmp/aog-visual-inspect`,
`/tmp/aog-host-materials`, `/tmp/aog-host-textures` and `/tmp/aog-render-inspect`. The source
and host installs were opened read-only. No game-derived output enters the repository.
