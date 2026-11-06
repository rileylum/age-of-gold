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

Riley reported that this comparison renders textures, though the surfaces are dark. This was
initially recorded as proof of source colour sampling. That conclusion was too strong: the
diagnostic retains host normal maps and material defaults, which can also produce visible
surface detail. The colour-pattern check below distinguishes those paths. Source lighting has
not been translated, and darkness is not yet attributed to lighting alone. The visually improved
build remains available unchanged.

### Isolating the missing material-settings reference

The proven native material's GX list contains one settings item. The inspected host model
using the basic colour shader contains that identical item plus another settings item. Our
written models already retain the proven template's list, but the newly constructed basic
materials leave their list reference unset. The next candidate changes only that reference,
using the value read from the host template. It retains the basic opaque/cutout definitions and
source colour bindings, so unrelated host normal maps are absent.

A new readback requirement compares every material's settings reference against the host
input. Running the full real-installs workflow before the implementation failed at that
requirement, after the collision and authored visual checks passed. The implementation now
assigns the existing template reference when constructing each basic material. This tests one
field; it does not claim that the missing reference caused the play failure until a play check
confirms the effect.

The corrected workflow passes for `/tmp/aog-firelink-gx-reference`. A raw-byte comparison of
all 61 written models against the earlier packed-UV build finds changes only in the 243 material
GX references. The settings bytes themselves were already present. The first comparison tried
reserializing parsed models, but the reader omits unreferenced lists; the raw-byte check avoids
that loss and verifies the stricter claim directly. All 133 non-model/non-report files are
identical, and the report changes only its tool fingerprint. All 195 files match
`/tmp/aog-firelink-gx-reference-repeat` byte for byte. Publishing produced no warnings.

After observing that the successful diagnostic's game had exited, the candidate was launched
offline with the same separate save. The previous successful native-material build is preserved.
The next play check is whether the basic materials now render the source colours, followed by
a comparison of brightness without the diagnostic's unrelated normal maps. Source lighting
remains untranslated. me3 confirmed attachment; log:
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_08-28-17.log`.

### Distinguishing colour overrides from native defaults

Riley's next screenshot remains particularly dark, with visibly smooth lit ground. Riley could
not confidently distinguish textures in the grey appearance. The GX-reference-only candidate
therefore does not establish a rendering fix. No screenshot is stored in the repository.

The next diagnostic returns to the complete native-material build and changes colour images
to authored, saturated checker patterns. Explicit source-colour package paths contain magenta
and yellow. Separately, the native material's default colour image contains cyan and green.
Both full and low packages are covered; secondary native images are unchanged. These are
temporary authored test fixtures, not replacement scenery for the mod. Geometry, model
materials, UVs, collision, events, profile and lighting are identical to the visually improved
native-material build.

The candidate is `/tmp/aog-firelink-colour-pattern/age-of-gold-firelink.me3`. Readback checks
126 source-path packages and two native-default control packages, including the untouched
secondary image payloads. All 70 non-texture input files are byte-identical. The additional
`colour-pattern-check.json` records the diagnostic and tool fingerprint; inherited reports
describe its input builds. All 201 files match `/tmp/aog-firelink-colour-pattern-repeat`.
The temporary inspector's `colourpattern` mode generates the pattern at each input dimension
and mip count, using ordinary BC1 blocks and preserving texture names.

An independent installed Pillow decoder confirms exactly the two authored colours and
alternating squares in each palette. It does not support the sRGB BC1 header tag, so the check
uses the equivalent linear BC1 tag in memory only; compressed blocks and written game files
are unchanged. This verifies block encoding, not runtime binding. The play result will identify
whether the explicit override, native default, or neither supplies the observed colour.

Riley confirmed exiting the prior game, and the pattern profile was launched offline with the
same separate save. me3 attached; log:
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_09-00-09.log`. The supplied play image
clearly shows magenta/yellow checks on the terrain. This proves that the explicit colour
override is sampled with the complete native material; native-default colour and normal-map
detail alone cannot account for those checks. The severe darkness remains a separate problem.
The screenshot is not stored in the repository.

### Complete native colour-only material

The next candidate is `/tmp/aog-firelink-basic-native/age-of-gold-firelink.me3`. It returns to
the unchanged source images and uses the complete colour-only material from one of the
previously inspected host map models, including its native material identity and both GX
settings items. Its definition uses the same basic opaque colour shader family as the first
writer, but is a material assigned to a real model rather than the generic base definition.
It has one colour binding and no normal or other secondary bindings. All surfaces temporarily
use that opaque material; restoring cutout behaviour remains part of the eventual mapping.

The temporary `nativematerial` inspector accepts the selected host binder as its template. It
records that binder's fingerprint and verifies all 243 material overrides across 61 models.
Restoring only materials and GX lists makes the serialized models identical to the checked
input. All 134 other input files are unchanged, including all source colour packages, geometry
placements, collision, events and the offline separate-save profile. All 196 files match
`/tmp/aog-firelink-basic-native-repeat`. The additional report describes this diagnostic;
the inherited build report describes its input. No lighting values were changed.

The preceding game was observed to have exited, and this profile was launched offline with
the same separate save. me3 confirmed attachment; log `~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_09-04-03.log`.
Its texture appearance and brightness still need play evidence.

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

Microsoft's [block-compression documentation](https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-block-compression)
describes the BC1 block and mip storage used by the authored pattern. Its documentation
repository is [CC BY 4.0](https://github.com/MicrosoftDocs/win32/blob/docs/LICENSE); no code or
illustration was incorporated. The already-installed Pillow 13.0.0 decoder (MIT-CMU) was used
only for an independent temporary image check, not added as a tool dependency.

All game-derived inspection output remains under `/tmp/aog-visual-inspect`,
`/tmp/aog-host-materials`, `/tmp/aog-host-textures` and `/tmp/aog-render-inspect`. The source
and host installs were opened read-only. No game-derived output enters the repository.
