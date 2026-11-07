# Rigid surface tangents — 8 November 2026

The rigid geometry spike now transforms source tangents as well as positions and
normals. A normal map needs a surface frame: retaining a template's tangent after
replacing its geometry can rotate the texture's lighting response incorrectly.

The tangent follows the same palette-selected node hierarchy as the position. It
uses the ordinary linear transform, then removes its component along the transformed
normal and normalizes. The normal continues to use the inverse transpose. The tangent's
handedness is retained as a sign. A packed signed value may exceed unit magnitude by
one quantization step; other non-unit signs, nonfinite values and degenerate frames
are rejected. Existing weighted, cyclic, singular and mirrored-geometry restrictions
remain in force.

An authored nonuniform scale and quarter-turn fixture distinguishes tangent scaling
from normal scaling and checks orthogonality and handedness. A constant tangent failed
before implementation. Separate checks first exposed accepted zero directions,
invalid handedness and overflowing direction lengths; all now reject those inputs.
The existing rigid-position and normal checks still pass.

A private install adapter applies this helper to six selected single-layer stone
surfaces, covering 38,478 written vertices. Triangle correspondence and transformed
positions are checked before assigning tangents. Serialized tangents agree within
their packed precision; restoring the old tangents and material references reproduces
the previous model bytes. Two full builds match across 391 files.

These checks establish the tangent transformation and serialization, not material
fidelity. The first normal-texture and flat-texture comparisons showed no clear visual
response. GPU replay confirmed that the intended texture was loaded and bound, but
the two inspected test draws contributed no visible pixels in the chapel view. Pixel
history identified a different surface as the visible chapel face. Subsequent material
tests must use that visible surface before drawing conclusions about normal-map support.

The corrected trial targets four visible chapel surfaces. GPU replay shows the brick
and mortar relief in the normal render target. A separate material defect initially
masked much of the improvement: an authored zero-metal control texture had a malformed
DDS header, so the host bound a white fallback and treated the stone as metal. The
format library could read its own output, which did not establish runtime acceptance.

A header check fails on that control and passes after declaring its FourCC and
compressed linear-size fields. Only two texture packages change across the complete
build. A fresh background game capture then binds the intended small black texture,
retains the source normal texture, and restores the stone's brightness. The game exits
through its normal UI. An earlier workaround changed both format and package grouping;
the header-only comparison establishes that regrouping is unnecessary.

This remains a normal-map attribution test. Zero metal and maximum roughness are
diagnostic settings, not a translation of source specular response. Layered surfaces,
sky rendering and complete illumination are still unresolved.

The header requirements were checked against Microsoft's
[DDS pixel-format documentation](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-pixelformat)
and [DDS header documentation](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header).
These references are subject to the
[Microsoft Learn terms](https://learn.microsoft.com/en-us/legal/termsofuse);
no implementation or sample code was copied.

The private adapter, material bindings, texture payloads, shader inspection and captures
remain outside the repository. The existing SoulsFormatsNEXT dependency is GPL-3.0;
the external RenderDoc tool is MIT-licensed. No code from either tool or either game
is incorporated into this change, and no production dependency decision is made.

## Wider single-layer trial

The private adapter now applies source normal textures and transformed tangents to 70
classified opaque single-layer surfaces, covering 187,876 vertices and 19 normal textures.
It reconstructs the packed-UV subdivision correspondence before assigning each tangent.
Restoring tangents and material bindings reproduces the previous model bytes, and two
complete builds match across 427 files. Three background camera views, each captured
twice, passed state checks and normal UI exit. Inspected chapel and stair views show
surface relief on the supported stone.

One subdivided surface has opposing tangent handedness at its source corners. Interpolating
those signs does not produce a valid tangent frame. The trial records that surface as
deferred and retains its previous material; it does not claim complete normal coverage.
Layered stone and source specular response remain separate unresolved translations.

## Layered-surface investigation

Source pixel history identifies the visible tree draw before material analysis. Its
surface blends two independently mapped colour and normal layers by vertex alpha.
Each normal layer has its own tangent frame. A survey of the three selected trees
finds blended triangles whose two mappings differ, so replacing both with a single
mapping would change the transition even though most vertices share coordinates.

Two native host controls are rejected for this translation. The first blends layers
using a common texture mapping; its extra vertex channels supply blend controls rather
than a second mapping. The second accepts two mappings but combines colour with a
nonlinear overlay operation instead of the source alpha blend. Authored colour textures
are bound in fresh GPU captures, so the rejection is based on executed material
behavior. Both game sessions exit through the normal UI.

Searching placed objects as well as map pieces locates another native candidate with
two mappings and two tangent frames. A third GPU capture shows that its blended layers
still share one mapping, with the other reserved for an additional overlay and mask. It
also cannot directly reproduce the source blend. Normal exit completed after temporary
storage was freed and the menu sequence was recovered. None of these controls establishes
a completed layer translation. A private build-time bake now combines the blended
regions; regions using a single layer retain their source textures. An authored check
first failed, then proved blend endpoints, an interior blend and equivalent normals
expressed in different tangent frames. The host-build experiment is still unverified
in play, with specular response, colour-processing equivalence and distant mip isolation
remaining explicit limits.
The inspection tools, native templates, shader text and captures remain private under
the existing local-tool authorization; this record contains only our own description.

## Private tree-layer bake checkpoint

The three-tree trial now runs with separate source mappings for both colour and normal
layers. Only blended regions and regions with varying vertex colour use atlases; uniform
single-layer regions retain their source textures. A perpendicular interpolated host
frame permits the blended normal to be encoded without reversing its hemisphere. The
writer retains the source triangles, checks packed UV and tangent readback, and verifies
that restoring the previous meshes and materials reproduces the previous model bytes.

The first in-game trial exposed corrupted compressed colour textures. The texture reader
computes its pixel offset from the current header type; changing that type before extracting
the pixels skipped part of the old payload. An authored legacy-header fixture failed on
payload preservation, then passed after extracting the pixels before changing the header.
The corrected build changes only the two affected textures and their low-detail packages.

Three views captured twice show clean bark and ground transitions after that correction.
A fresh GPU capture binds the intended colour atlas through an sRGB view and the normal
atlas through a linear view. Its sampler transform reverses the deliberate expansion used
to preserve precision in packed UVs. The blended draw contributes textured normal detail
at the tree's base. Both capture sessions exit through the normal UI. Two independent
builds match across all 455 files.

This is a private, limited material checkpoint. The scene remains darker and differently
coloured than the source. Source specular response, complete colour processing, other
layered surfaces and distant atlas filtering remain unresolved. Neither the experimental
bake nor its game-derived inputs have been added to the repository or the user preview.


## Stair bake and shared-mapping control

The private bake now accepts an explicitly selected surface inside a larger model.
It checks correspondence against the preceding packed geometry, retains the other
surfaces, and verifies triangle coverage and serialized attributes. An authored packing
check failed on the earlier narrow atlas layout, then passed with bounded widening.
The stair build and its repeat match across 470 files. Matched close views in both games
completed twice with normal exits. Stair texture detail is present, but adjacent ground
still lacks its separate moss blend, and overall shading differs from the source.

The stair's two source mappings and tangent frames are identical. A separate native
blend control therefore tests whether that surface can avoid a large atlas. Its packed
blend weight is checked within the analytically bounded half-step error of the stored
field. The first runtime control renders almost black despite textured colour and normal
render targets. GPU replay traces this to a nearly zero ambient contribution. Integer
colour arguments selected a byte-valued library constructor, so a nominal white channel
was stored near black. A normalized-colour readback check fails before correcting the
constructor arguments to floating-point values. The corrected close view renders the treads properly in two checked captures and exits
normally. It is visually close to the baked stair control without its large atlas pair;
that comparison does not establish equivalence to source shading.

A survey of 85 two-layer source surfaces identifies only four with identical mappings,
identical tangent frames and uniform white vertex colour. The native control is restricted
to that classified subset. Its linear-colour blend and normal combination still differ
from the source operations, so it is not an exact general replacement. The nearby ground
has distinct mappings and higher-resolution detail; the current bake rejects that input
at its declared density limit. It has not been silently reduced to fit.

All new adapters, install-derived inputs, native templates, bindings and captures remain
private. Existing SoulsFormatsNEXT and RenderDoc licence qualifications above still apply.

The four shared-mapping controls now coexist in one private build. Two independent
builds match across all 500 files. Three standard views captured twice pass camera,
anchor and clock checks, followed by normal exit; the inspected views show no new
black surfaces or texture corruption. The close stair comparison remains the stronger
material check, since not every selected surface is visible in these wider views.

## Private shader reconstruction controls

A wider installed-shader survey examined the vertex signatures of 952 programs and
the sampling operations of selected blend families. The additional candidates still
combine detail, overlays or several layers on a common mapping; none of those examined
directly supplies the required independent mapping and vertex-alpha blend. This is a
bounded survey, not a claim that every possible host material has been exhausted.

A private utility now rebuilds a compiled shader container from its local intermediate
representation. An authored root-signature fixture exposed a missing root-signature
part in the first reconstruction. Preserving that part passes the fixture and compiler
validation. An unchanged installed pixel program then rendered correctly in two close
captures. A separately named clone with a temporary colour marker changed only the
selected stairs in the inspected view. Both game sessions exited normally. These
controls establish that the override is loaded and that unrelated materials can retain
their existing shader; they do not establish source shading fidelity.

The compiler is Microsoft's DirectX Shader Compiler, release v1.9.2609. Its Linux archive
was checked against the release's published digest. The project describes its compiler
licence as the University of Illinois Open Source License; its licence file also contains
component-specific notices. The downloaded distribution includes additional Microsoft
terms, so these observations do not settle redistribution rights for every bundled file.
Only private development tooling is being used here. No compiler, game program,
intermediate representation or derived output enters the repository, and no production
dependency decision has been made. Primary references:
[release](https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.9.2609),
[project description](https://github.com/microsoft/DirectXShaderCompiler/blob/v1.9.2609/README.md),
[licences](https://github.com/microsoft/DirectXShaderCompiler/blob/v1.9.2609/LICENSE.TXT).

## Independent ground-mapping experiment

The nearby ground has varying vertex colour as well as two different mappings.
A private adapter preserves both mappings, colour and blend weight, and the source
frame information. Its check first rejects the earlier build for missing layer-frame
attributes. The converted geometry then passes correspondence and serialized-attribute
checks, preserves topology and positions, and restores unrelated model data exactly.
Source texture payloads and their mip chains are retained without atlas reduction.

The source ground contains triangles whose tangent handedness differs across their
vertices. A fresh source GPU capture, identified by matching both bound colour textures
against installed texture payloads, confirms that its bitangents are formed before
interpolation. Each layer's resulting normal is normalized before blending, followed by
another normalization. It also confirms a shared tangent axis in that program's two
normal transforms. The private prototype carries the interpolated bitangents explicitly
instead of reconstructing them after interpolation. Earlier private bake assumptions
about these operations are therefore not sufficient evidence of exact source normals.

The first host capture omitted the selected ground. The host was using a second vertex
program variant whose interface had not been extended. Extending that variant restores
the ground, including the moss placement around the stairs. GPU replay confirms the
added floating-point colour, mapping and frame fields are consumed, both colour textures
and both normal textures match the source mip-zero payloads, and their full mip ranges
are bound. The normal render target contains the ground's detailed surface relief.

Two complete private builds match across all 513 files. The close GPU capture and a
wider capture sequence both exit normally. The latter checks three named views twice;
two names share an angle, so it represents two distinct orientations. Inspected views
show no renewed holes or black surfaces. This establishes a working independent-layer
path for one selected ground surface without a large atlas. It does not establish exact
per-pixel source normals or colour: the host remains warmer and more contrasty in the
close comparison. Colour-processing order, specular response, remaining shader passes
and wider surface applicability are unresolved. All experiment inputs, code that rewrites
installed programs, generated bundles and captures remain outside the repository.

## Colour order and the wider opaque family

The captured source ground filters raw colour values, blends the layers and applies
vertex colour before its final colour-space conversion. A paired private control uses
raw colour views and moves that conversion after the material blend in the host program.
Texture payloads and mip chains remain unchanged. GPU replay confirms the raw views and
the changed pixel program. This is an approximation: the source converts its final lit
and fogged result, whereas this control converts albedo before host lighting.

In the matched close view, mean absolute colour difference over the selected ground
region decreases from about 20.0 to 18.5 on an eight-bit scale. This is a modest improvement,
not a general fidelity score; lighting, fog, filtering and animation remain in the final
images. The host still has a visibly different overall colour and contrast.

A separate replay control exports the source's computed surface normals for comparison
with the host normal target. The first export lost negative components in an unsigned
target and is rejected. Encoding the signed range before export fixes that diagnostic.
Across the intersection of the ground draw masks, with boundaries eroded, the median
angular difference is about 4.3 degrees and the mean about 5.2 degrees. This establishes
substantial agreement, not exact normal equivalence. The captures use different anisotropic
filter limits, and rasterization and packed frame attributes also differ. Tightening the
coordinate readback tolerance passes the existing wider build, so no coordinate-format
change was made on the basis of that hypothesis.

The normal-export experiment also uses the installed SPIRV-Tools assembler and disassembler.
Local package metadata identifies version 1.4.363.0 and the Apache-2.0 licence. They are
private diagnostic tools only; no utility code or shader-derived text enters the repository.

All 85 selected opaque two-layer surfaces share the checked source material definition,
unit tiling and supported colour/normal formats. A batch adapter now extends the ground
path to that family. The readback check first rejects the preceding build for missing
independent frame fields, then passes all converted surfaces. It checks source correspondence,
both mappings, vertex colour and frames; topology and positions remain unchanged, and
unrelated model content is restored exactly. Reused textures are deduplicated without
atlas reduction. The result contains 125,703 serialized vertices and 110,330 triangles
across the selected surfaces.

Two complete builds match across all 530 files. Three distinct views captured twice pass
camera, anchor and environment checks and exit normally. The inspected views show layered
detail without renewed holes or black surfaces. A separate wide GPU capture verifies the
same private pixel program and source texture payloads on 43 sampled draw calls, covering
30 distinct textures. Six additional matching draw calls report no sampled descriptors;
they are not counted as texture-binding proof. This view does not cover every converted
surface. The GPU session's normal-exit helper completed and the game process disappeared,
although its outer wrapper returned a termination status before its final progress message.

This remains private experimental work. Source specular response, other material families,
other shader passes, and the overall fog/lighting match are unfinished. The ordinary play
preview is unchanged. Controls, code that rewrites installed shaders, third-party tooling
and game-derived outputs are preserved outside the repository; the earlier licence and
production-dependency qualifications continue to apply.
