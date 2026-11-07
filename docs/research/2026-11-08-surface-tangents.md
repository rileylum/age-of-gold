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
