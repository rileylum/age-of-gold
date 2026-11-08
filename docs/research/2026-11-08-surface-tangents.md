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

## Atmosphere composition controls — 8 November

The source's opaque surface program converts its accumulated lighting before applying
distance fog and directional scattering, then converts the result back. The earlier
albedo-only colour control therefore remains an approximation; saying that the source
converts only after lighting and fog omits the conversion before atmosphere composition.
The source also converts its tinted material colour before lighting; these are separate
operations. Adding atmosphere composition does not justify removing the material conversion.

A private replay control removes atmosphere from one visible source surface. Reapplying
the measured calculation to that control reduces mean absolute linear colour error from
about 0.00956 to 0.000147 over 47,883 interior pixels. Median relative error is about 0.56%,
with a 95th percentile near 1.98%. The exported inputs and output use a quantized HDR
format, so this is evidence for the calculation, not exact shader equivalence. An earlier
attempt selected a ground draw outside the camera view and is not usable evidence.

The host's volumetric atmosphere pass emits scattering and transmission; it does not read
the complete lit scene. The earlier lighting-composition pass does have the inputs needed
for the source colour order, with depth and camera data already bound. A replacement that
retains the native calculation gives a median final-image difference of zero against the
fog-disabled control; live animation and particles account for some remaining difference.
The first atmosphere replacement also passes a numerical check against its bound inputs
and output. These checks establish execution, not a source lighting match.

Applying one source atmosphere row to every opaque surface is incorrect. A pixel history
traces a bright horizon strip to the lower surface of the source sky model. A survey finds
separate source atmosphere combinations for it and the distant scenery. The initial
selection experiment duplicates lighting tracks unchanged and assigns those parts distinct
map IDs. Readback and a 533-file deterministic rebuild pass, as do three camera views twice
and normal shutdown. However, a targeted GPU check rejects the assumed correspondence
between map IDs and rendering indices. That experiment must not be promoted on the basis
of its apparent visual improvement.

The next private control carries an explicit atmosphere tag in the emission-strength
channel of selected distant materials. Every selected opaque shader path first checks that
its emission colour is identically zero. The intended invariant is unchanged lighting and
glow while carrying selection metadata to composition. The first runtime capture shows
that the selected program loads and writes the tag, but its render pipeline disables writes
to the emission target. The resulting selection remains zero, so that control is rejected.
A native material emission feature alone does not enable the writes either. Five native
plain/emissive shader pairs identify a shared single-bit difference in shader metadata.
Changing that bit, while preserving the rest of each selected metadata file, enables the
emission target in a fresh GPU capture. This establishes the feature's effect for these
families; it does not classify every bit in the field.

The successful control selects the intended sky tag at the traced pixel and preserves zero
emission colour and glow on tagged surfaces. Its numerical composition check covers about
2.68 million pixels, with median relative error near 0.65% and a 99th percentile near 2.46%
against its bound inputs. The cyan horizon band disappears. Two builds match across 533
files, and three distinct views captured twice pass pose/environment checks and normal
shutdown. The first material-flag capture started before its build finished and loaded host
scenery; it is rejected. The fresh capture used for that control is recorded separately.

A second source calculation check covers the distant lower sky surface. Reconstructing its
atmosphere reduces mean absolute linear colour error from about 0.0961 to 0.000770 over
47,846 interior pixels. A matched-view comparison also checks the host's reconstructed
distance, scattering angle and fog amount against source shader diagnostics for both the
near surface and distant sky surface. Their errors remain within the quantized diagnostic
precision and small geometry-boundary differences. This rules out a reversed viewing ray
in these views; it is not a complete camera proof.

The mechanism is now demonstrated, but the build still has too much blue haze and a flat
lower horizon. The source cloud surface is absent from the build; its source draw occurs
after opaque composition and uses transparency. The earlier fixed-origin sky bake also
remains provisional. Unexamined atmosphere rows, source lighting and specular response are
unresolved, and the ordinary play preview has not been replaced. All installed programs,
their reconstructions and comparison captures stay outside the repository under the
existing private-research authorization.

## Transparent cloud controls — 8 November

A source replay that removes cloud opacity confirms that a separate transparent surface
supplies the missing horizon detail. The private build now translates that surface using
a native transparent family with depth testing and no depth writes. Its check first
rejects the preceding build, then verifies geometry, source frames, both coordinate sets,
vertex opacity, material state and all texture mip payloads. Restoring the previous model
lists reproduces its earlier bytes; the placement is unchanged. Two builds match across
538 files.

A fresh GPU capture confirms the new draw and intended blend/depth state. Both cloud
textures retain all eleven source mip payloads on the GPU. Native illumination alone gives
too much cloud contrast. A second private control applies the already checked source sky
atmosphere calculation in the transparent forward pass. Its runtime diagnostic covers
310,415 pixels, with mean absolute linear error about 0.000831 and a 99th percentile
relative error near 1.64% against its bound inputs. The unfogged control's mean error is
about 0.1005. Quantized diagnostic inputs limit precision; this verifies execution rather
than equivalence to all source shading.

Two builds of that control again match all 538 files. Three distinct views captured twice
pass camera, anchor and environment checks and normal shutdown. Clouds are visibly softer
and horizon detail is restored. Source cloud illumination and texture scrolling remain
untranslated, and the scene's broad blue haze and material response still differ. The
ordinary play preview remains unchanged.

A separate source replay removes opacity from ten draws sharing a mist program. In the
tested view, about 1.9% of pixels change by more than one byte level in any colour channel;
mean absolute colour difference is about 0.07 byte levels. This is a shared-program control,
not an isolated surface test. It makes the missing mist a weaker explanation for the broad
haze in that view, so the next comparison addresses lighting and material response.

All reconstructed programs, source-derived controls and captures remain private under the
existing authorization. The previously recorded tool licences and production-dependency
qualifications still apply; no new library decision is made.

## Diffuse environment controls — 8 November

A source replay separates the light contributions on one visible surface. Its diffuse
environment accounts for about 79.7% of the accumulated light before ambient occlusion,
with hemisphere illumination about 19.8% and environment specular about 0.5%. Local light
is zero on this sample and shadow transmission is one. These proportions describe this
surface and view, not Firelink generally. Summing the measured terms reproduces the
pre-occlusion output within the diagnostic format's precision. Ambient occlusion reduces
the sampled light by about 2.4%.

The diffuse environment is a cubemap. All six compressed faces from a source-install
texture match the source GPU payload exactly. A survey of 53 draws using the layered
source program finds the same environment texture and selected lighting coefficients.
Using its diffuse environment and hemisphere terms, followed by the checked atmosphere
calculation, approximates the selected source surface with median relative error about
0.78% and a 95th percentile near 3.66%. This approximation excludes specular, ambient
occlusion, local light and shadow attenuation; the latter is neutral on this sample.

A private host control applies those terms to 80 classified layered surfaces. It keeps
source colour filtering, vertex colour and pre-lighting conversion, and bypasses the old
native diffuse calibration. The emission target carries accumulated light with zero glow
strength. Native diffuse and reflectance are zero on the selected surfaces, avoiding a
second lighting contribution. Materials using other source lighting rows retain their
previous shading. Geometry and unrelated material bindings are checked unchanged.

GPU inspection finds the new program in 43 visible draws, confirms all six source cube
faces and enabled emission-target writes. A numerical replay checks 7,891 interior pixels
against the bound inputs: mean absolute linear error is about 0.00000087, and the 99th
percentile relative error is below 0.10%. The first diagnostic failed because its assembly
step changed the program format version; retaining the captured version fixes it. The
initial sample-size guard also expected more visible pixels than this draw contains; its
replacement accepts at least 5,000 interior pixels without changing the arithmetic
tolerance. Neither failed attempt counts as passing evidence.

Two builds match across 541 files. Three distinct views captured twice pass anchor,
camera and environment checks, followed by normal shutdown. The tree, ground and some
masonry regain warmer colour and directional detail. Several simpler materials still
show a blue cast. This is a useful lighting control, not visual fidelity, and the ordinary
play preview remains unchanged. Coefficients are measured from the source runtime in this
private experiment; deriving them from installed data for a released build remains work.

The control extends to 51 classified single-layer surfaces, reusing the checked program
with identical inputs for both layers. A separate source-program survey checks 29 visible
draws against the same environment texture and lighting coefficients. It also confirms
neutral additive colour and detail-normal contribution for those draws. The new build
restores source vertex colour and interpolated frames on 136,470 vertices; geometry and
unrelated model state remain unchanged. Its workflow check rejects the preceding build
before the writer is added, then passes the written bindings and duplicate layer inputs.
Shared native materials and distant parts with explicit atmosphere tags are left alone.

The expanded build matches its independent repeat across 578 files. Three views twice
pass camera and environment checks and normal shutdown. A fresh GPU capture confirms
23 visible draws use the previously checked program, enabled light-carrier writes, raw
colour views, and all source mip payloads for 17 colour textures. Chapel masonry and
nearby walls regain clearer directional detail. The matched source views also show that
some blue city-wall colouring is intentional. The host remains brighter and hazier overall;
final colour processing and the excluded lighting terms need further comparison. The
ordinary play preview is still unchanged.

## Final-colour controls — 8 November

Captured inputs allow independent numerical reconstruction of each game's final-colour
pass. The source adds bloom before exposure and its tone curve, then applies display
encoding and a colour matrix. The host uses a different curve and a sampled colour table.
Both reconstructions match every colour channel of all 3,686,400 output pixels within
one 8-bit step. Mean errors are about 0.027 and 0.029 steps respectively. This establishes
the calculations in these captures, not their equivalence across areas or display modes.

The initial host calculation was wrong because it assigned the scene and glare resources
to the wrong scaling terms. Inspecting the actual descriptor indices resolves that error;
the corrected reconstruction uses the same tolerance. The export also now distinguishes
multiple constant-buffer descriptors sharing one reflection index, preventing one export
from overwriting another. Neither failed reconstruction counts as passing evidence.

A private control replaces only the host's selected final-colour program with the observed
source curve, exposure clamp and colour matrix. It omits bloom to isolate this calculation
and does not use the host colour table. The exposure value is fixed to this source capture;
adaptive exposure and other display modes are not translated. A fresh GPU capture confirms
the intended program and input. Its output again agrees with the independently checked
source calculation within one 8-bit step over the whole frame, with mean error about 0.029.

This removes some excessive brightness from masonry and ground but makes the sky too dark.
The earlier sky control still halves its measured radiance. A second control restores full
strength, retaining exactly the same sky texture and other build files. The first control
changes only the selected system program; both preserve unrelated shader entries. Both
builds match independent repeats across 579 files. Each has three views captured twice,
checked camera and environment state, and normal shutdown. The second run also records a
GPU frame; its comparison images contain a small capture overlay at the upper left.

The combined result brings sky colour and nearby masonry closer to the matched source
views, without establishing full fidelity. Vegetation remains too dark, distant softness
differs, and the earlier lighting exclusions remain. Removing bloom from the source's own
captured calculation changes mean colour by about 5.68 byte steps, with larger differences
around bright regions. That effect needs a separate translation, rather than compensation
through sky brightness or general illumination. The ordinary play preview remains unchanged.

All reconstructed programs, measured controls and images remain outside the repository
under the existing private-tool authorization. Previously recorded tool licences apply;
no production dependency or licence choice is made.

## Bloom and foliage attribution — 8 November

Seven captured source bloom stages are independently checked against their captured inputs.
They downsample the scene, apply a depth-dependent threshold, filter at two resolutions,
and finish with two directional filters. The last two passes write colour only: their
retained alpha comes from earlier target contents and carries the separate depth weight.
Treating those passes as ordinary four-channel filters fails the check. With the captured
write masks respected, every checked component is within one half-precision step. The
threshold pass includes four adjacent depth choices at exact point-sampling boundaries;
this bounds that raster sampling ambiguity rather than proving a unique tie direction.
These are individual-stage checks, not yet an end-to-end bloom translation.

The host uses colour-only bloom targets and two contributing filter chains. Its final
colour stage also has access to scene depth through a global resource table. A faithful
mapping still needs to account for the source's separate depth weight and target history.
Descriptor offsets must be obtained from the captured access record: an array index alone
does not identify the same descriptor. No bloom replacement is installed by this research.

The cutout survey finds 70 source surfaces across six material families. Nearby foliage
and ivy account for 45 of them, share one source lighting selection, and still use a generic
host cutout material. Twenty-eight captured source draws share the measured diffuse and
atmosphere constants. The distant canopy has a separate treatment and is excluded from
this nearby-foliage control.

A visible ivy draw supplies 33,292 interior pixels for component checking. About two thirds
of its measured diffuse illumination comes from the hemisphere term; the measured specular
contribution is zero and shadow transmission is one on this sample. This is evidence for
that surface and view, not a general claim about foliage. The first selected draw was
occluded and supplied no pixels, so it cannot prove anything. A visibility survey identifies
the later sample. Fixed relative-error and nearest-rounding assumptions fail on its reduced-
precision diagnostic output. Conservative intervals of one step in each stored component
cover the albedo conversion, hemisphere term and diffuse sum without any outlying component.
The interval check records that precision limit explicitly.

The private foliage control now supplies source colour, vertex colour, mapped normals and
interpolated tangent frames on those 45 surfaces. An initial vertex-colour layout placed
source colour in the host control channel; a new semantic-order check rejected it and the
writer was corrected. The next build passed file checks but made selected foliage disappear.
The compiled vertex and pixel programs assigned different physical interpolator slots to the
same semantic. A contract check rejects that pair; matching the slots restores the ivy and
wall-top grass in the host. This demonstrates why compiler validation and file readback alone
do not establish rendering. The corrected generator emits the tested stage payloads exactly.

Fresh host capture identifies 21 draws using the program; 19 have visible texture accesses.
All mip payloads of their ten colour textures match the source, with raw colour sampling,
and all six environment-cube faces match. A visible sample supplies 31,477 interior pixels:
the lighting sum has a 99th-percentile relative error below one tenth of a percent, and colour
conversion below a quarter of a percent, against the independently captured source constants.
Three camera views captured twice confirm restored coverage; camera and environment checks
pass, and the game closes normally. The final build and its repeat match across 624 files.

This remains a private diffuse-lighting control. Other vegetation families, source wind,
specular, occlusion, local lights and bloom remain incomplete. The ordinary preview is
unchanged. No production dependency or licence decision is made; all game-derived programs,
measurements and images remain outside the repository under the recorded private-tool
permission and tool licences.

The next control adds thirteen nearby standard grass surfaces, for 58 in total. Their ten
captured source draws use the same pixel program and diffuse/atmosphere constants as the
foliage control. Across 38 source draws, the additional vertex-colour multiplier is identity,
texture scrolling is zero and the pixel colour offset is zero. This checks the input
modifiers that could otherwise invalidate reuse of the lighting calculation.

The expanded workflow first rejects the prior build for missing grass bindings. The new
build and repeat match across 628 files; three camera views twice pass pose/environment
checks and normal exit. Fresh GPU capture contains the same two checked pixel programs,
27 selected draws and 23 visible texture accesses. Their twelve colour textures retain the
source mip payloads, including both newly covered grass textures. The arithmetic proof from
the unchanged program still applies within its stated scope. Other lighting selections,
wood cutouts and distant canopy remain outside this extension.

A separate bloom experiment moves depth weighting before the last two filters to avoid
carrying a fourth channel. It fails the existing final-colour tolerance: mean error is about
0.65 byte steps and the 99th percentile is 17. The experiment starts with a captured earlier
buffer to isolate ordering, and changes no live shader. It rules out that particular shortcut;
a faithful mapping still needs the separate depth weight or an independently checked equivalent.

## Private bloom translation — 8 November

The source depth reduction selects a point sample at a pixel boundary. Every captured output
matches one of the four neighbouring full-resolution depths; it is not an average. Successive
reductions make the exact boundary convention matter. A full-chain numerical control using
the lower-side convention meets the existing final-colour tolerance in this frame; two other
conventions fail. This is a measured approximation at depth edges, not exact source sampling.

Moving the depth weight after the final filters but before upsampling also meets that
frame's tolerance, but the selected implementation retains the separate channel instead.
It stores each half-precision value's bits as exactly representable integers in a native RGB
target, with the four channels occupying neighbouring pixels. All 65,536 bit patterns survive
the storage model. Filtering happens only after decoding. A GPU survey finds the selected
downsample and blur programs used only by bloom in the captured frame; the existing passes
can perform the source filters without adding a renderer hook.

The first live build reveals a precision difference: the host's legacy conversion rounds
these values downward. Modelling that behaviour reproduces all four stages within one half
step, but it is not the source render-target rounding. An explicit nearest-even conversion
passes an independent check of every positive finite half midpoint and adjacent floats,
range boundaries, and one million seeded positive finite float values.

The corrected build and its independent repeat match across 629 files. Seven selected
programs read back correctly; unrelated programs and build files remain unchanged. Three
camera views twice pass pose/environment checks and normal exit. Actual GPU storage remains
integral and the spatial repetitions are exact. All four translated stages match independent
calculations from their captured inputs within one half step; their 99th-percentile error
is zero. Compositing preserves the encoded bloom exactly. Final colour matches all
3,686,400 pixels within one byte per channel, with mean error about 0.028 byte steps.
The captured projection coefficients match the earlier control. Short capture intervals
retain the background harness's 30-fps rate; this is not an uncapped performance benchmark.

This establishes the private bloom mechanism, not full visual fidelity. The control still
uses captured coefficients and exposure, a fixed observed host depth projection and an
explicit depth-boundary convention. Other camera modes and resolutions remain unproven.
The source image is still softer and some surface responses differ; the remaining
postprocessing and excluded specular/occlusion contributions need attribution. The ordinary
preview is unchanged. All reconstructed programs, game-derived measurements and images
remain private under the existing permissions and tool licences; no new production
dependency or licence choice is made.

## Sharpening and temporal-filter controls — 8 November

The host applies a log-domain sharpening pass after temporal antialiasing. Replaying that
pass with its centre sample as output changes about 44% of final pixels by more than one
byte step and reduces harsh outlines. A private bypass build preserves the native sampler;
the first candidate's different sampler is retained as a rejected contract even though its
captured centre samples happened to match. The corrected candidate and independent repeat
match across 629 files. Three views twice pass pose/environment checks and normal exit.
Its GPU output is byte-identical to its HDR input over the entire captured frame. The
ordinary preview remains unchanged.

The source's remaining softness is partly temporal filtering. In a stationary capture, two
later postprocess stages leave their colour input unchanged. The temporal stage differs from
its current input by about 1.83 final-colour byte steps on average, but from its history by
only about 0.058. These comparisons hold captured bloom fixed, isolating those stage outputs;
they do not reproduce a changing temporal sequence or prove a depth-of-field effect.

The source temporal colour calculation compresses brightness, bounds history using the
current neighbourhood, and blends according to motion and colour range. The host uses a
different colour transform and logarithmic interpolation. An authored float ramp establishes
that the captured source packed colour target rounds downward: all 3,686,400 pixels match
that rule, while nearest rounding fails. This differs from the earlier half-target check.

A private diagnostic exports intermediate float bit patterns through exactly representable
integer fields. An independent model of the source colour-history core, using those measured
texture samples, motion gain and history flag, matches every interior output within one
representable packed-colour step and rejects a current-frame-only control. It excludes
sampling, reprojection and the one-pixel border. Earlier checks remain failed: approximating
the sampled inputs produces larger outliers, and a tighter float-relative error bound is
not met. Even the instrumented floating result can quantize one step away from the captured
unmodified output. This is a format-tolerance result, not bit-exact temporal reproduction.

Native history categories in this capture distinguish the moving character from the static
world. Their other categories and the source's moving foliage history remain unclassified.
Any trial using the host motion/history inputs must retain that scope and check camera
movement and changing visibility before promotion. All programs, captures and measurements
remain private under the existing permissions and tool licences; no production dependency
or licence decision follows from these controls.

## Host temporal translation and motion controls — 8 November

A private trial now places the source colour-history calculation in the host's active
temporal variant, retaining native motion, exposure compensation and object-history inputs.
The first attempt changed an unused variant. The second selected the active variant but
combined two texture-coordinate semantics incorrectly; the temporal draw disappeared and
the frame became grey. A verifier that accepted an empty selection also failed its purpose.
The corrected verifier requires exactly one active draw and checks the actual program and
inputs. Retaining the native semantic names, indices and masks restores the draw.

An independent calculation from captured host inputs initially matches every interior pixel
within one packed-colour step. Extending that check to the border rejects the candidate:
out-of-range integer loads introduce black instead of the source's clamped edge samples.
The corrected build and repeat match across 629 files. Three views twice pass pose and
environment checks, GPU activation is verified, and the game exits normally. The full-frame
calculation now matches all 3,686,400 pixels within one packed-colour step and rejects a
current-frame-only control. This check retains the observed full-resolution mapping and
eight-bit approximation to hardware filtering; it is not an exact floating-point proof.

Lossless video targeted at the private game window is checked against independent Gamescope
stills. The preceding candidate, the native temporal filter and the source game each complete
the same acknowledged camera sweep and four cuts, then exit normally. The candidate settles
more slowly than the native filter, resembling the source recording. On the half-turn cut,
mean proxy-image change one frame later is about 2.9 byte steps for the candidate, 3.5 for
the source and 0.9 for the native filter; twenty frames later these are about 0.38, 0.47 and
0.07. These differences include live animation and exposure. Inspected frames show no
persistent previous-scene image, but do not establish all disocclusion or movement cases.
The host still applies its own motion blur on the first frame after an abrupt cut.

Native point-sampled object categories approximate the source's interpolated history
metadata. Wind history and other native categories remain outside the trial. The ordinary
preview is unchanged pending broader play checks. All programs, measurements, captures and
videos remain private; existing tool licences apply and no production dependency is chosen.

## Opaque foliage omitted by the cutout pass

A foreground pixel trace identifies a plant still using the host's basic material. Its
source and host draw positions and topology match the corresponding installed and written
mesh. The source material is foliage with opaque blending, so the earlier cutout-only survey
never selected it. Seventeen surfaces share this family. The six unconverted cutout surfaces
considered first were not identified as the traced plant; they were left unchanged.

Four visible source draws share the measured diffuse and atmosphere coefficients used by the
nearby foliage control. The traced draw has alpha rejection disabled. Independent component
checks cover 78,797 interior pixels within the captured colour format's rounding intervals.
Specular is zero and the shadow multiplier is one there; ambient occlusion does reduce the
result. This supports a diffuse-environment control, not omission of those terms everywhere.

The new private workflow first rejects the existing build for missing bindings. It then
restores source vertex colour and normal frames on the seventeen opaque surfaces, reuses the
checked textures, and preserves native opaque depth and velocity stages. A pre-launch review
catches an unintended metadata edit in the first writer adaptation; the corrected writer
checks all metadata bytes against the intended single-flag change. Its build and repeat match
across 642 files. Changes are confined to the selected models and the two binding archives;
the previous haze, light rays and cutout surfaces are retained.

Eight views captured twice pass cross-game pose, player-anchor, image-size and fixed host
environment checks. The game closes normally and the private profile is restored. All eight
first repetitions are inspected: the foreground plants regain visible green shading closer
to the source, with no missing scenery noticed. The host remains sharper, with broader tone
and shadow differences; this is not full visual fidelity.

The live capture identifies five draws using the new opaque program. Colour, normal and
first-face diffuse-environment payloads and view formats match the source capture; the traced
plant also retains its positions and topology. An independent calculation from captured
source constants checks 78,832 interior host pixels. The 99th-percentile relative difference
is below 0.1% for illumination and 0.2% for albedo, using half-precision diagnostic outputs.
Source wind, ambient occlusion, local lights and general shadow response remain outside this
trial. The ordinary preview is unchanged. Programs, game-derived measurements and captures
stay private; no new production dependency or licence decision is made.

## Final display brightness attribution

A matched city-wall capture separates material illumination from the later display stages.
Before ambient occlusion, the source and host mean diffuse response differs by less than
0.4% over approximately 1.67 million shared interior pixels. The host's atmosphere and
tone-mapped outputs are slightly brighter than the source's at this view, despite its final
displayed wall appearing darker. Both measured source exposures lie below the same clamp,
so changing exposure does not explain this comparison.

The missing step is the source's final brightness lookup. Applying the captured lookup to
its captured colour input reproduces all 3,686,400 output pixels exactly. The lookup also
matches the earlier chapel capture. The host instead combines scene and interface and then
applies its own display exponent; an independent calculation reproduces that full frame
within one byte step. Earlier candidate draws were interface rendering, not this final pass.

Holding the old host scene input fixed, substituting the measured source lookup reduces
mean absolute colour difference against the source wall from about 12.47 to 2.56 byte steps.
This is an attribution prediction on the shared wall mask, not a full-scene fidelity score.
It establishes why increasing ambient illumination would compensate at the wrong stage.

A private candidate applies the captured source lookup to the scene. Before the native
interface blend it reverses the host display exponent, so that exponent is not applied
twice to the scene. The native interface contribution retains its display response. The
workflow first rejects the native output against the source lookup, then checks installed
program readback and unrelated-file preservation. Two builds match across 644 files.

Eight views captured twice pass pose, player-anchor, dimensions and held host environment
checks, followed by normal exit and profile restoration. All eight first repetitions are
inspected: darker stone and bark are substantially closer to the source. Sharper foliage,
haze differences and a dark patch on one tree remain. A live GPU capture verifies the active
replacement and reproduces every displayed scene pixel exactly from independently read
inputs. That frame contains no interface contribution, so a separate menu capture exercises
the blend. Its full-frame calculation passes within one byte step, including 65,215 opaque
interface pixels and more than two million pixels with another interface contribution.
The opaque interface also matches the native display result within one byte step. The first
menu attempt stops before GPU capture because the old recognition template includes a
different scene background; the inspected menu becomes the next attempt's template, without
loosening its tolerance. Both attempts exit normally and restore the private profile.

The lookup is a private captured-setting control. General brightness settings, other display
modes, source exposure outside the observed clamp, and complete visual fidelity remain open.
The ordinary preview is unchanged. All programs, lookup contents, captures and measurements
stay private under the existing permissions and tool licences; no production dependency or
licence is selected.

## Tree darkness attribution — 8 November

A new calibrated source/host pair checks the tree patch that looked darker in the
display-curve comparison. The earlier visual impression was misleading: all three
sampled patches are brighter in the host. The tree's captured vertex positions and
unoriented triangle connectivity match exactly, as do the first subresources of its
two colour textures, two normal textures and diffuse environment texture.

Across about 311,000 shared interior pixels, mean accumulated response before source
ambient occlusion differs by about 0.9–1.3%. The independent host lighting calculation
matches within 0.13% at the 99th percentile. Source ambient occlusion lowers the mean
by about 2.5–2.8%; the sampled shadow multiplier is one. These measurements do not
justify adding light to this tree. They do not establish general shadow equivalence.
The source diagnostic includes its small specular contribution; the host control
contains diffuse lighting alone.

Both games bind complete material mip chains with zero sampler bias. Their captured
anisotropic filtering limits differ. That is a possible contributor to local detail,
not an established explanation of the remaining foliage differences. Source foliage
motion remains untranslated and is the next isolated control. No build or ordinary
play profile changed for this attribution. Captures and diagnostics remain private
under the previously recorded tool permissions and licences.

## Source foliage motion — 8 November

Source captures distinguish foliage driven by a shared direction from ivy driven by
its vertex tangents. An independent calculation of the source motion and rigid
transforms matches over 103,000 vertices in one capture and over 20,000 in another.
The second capture exercises all three observed program variants at a different
clock value. Two partial draws are explicitly excluded from the first comparison.

A private writer carries the source motion weights, phase and transformed directions
into 62 selected surfaces. It rejects ambiguous attribute seams and subdivisions
that would require interpolating nonlinear motion. Static grass retains its previous
material. Twenty rigid vertex variants cover colour, depth, forward and velocity
programs. The first candidate fails a new displacement-envelope check: its static
culling bounds do not contain the motion. The corrected candidate expands mesh,
model and root-node bounds. Two corrected builds match across 645 files, with changes
confined to the selected model binders and their material and shader bundles.

Two live host frames verify 54 draws, comprising about 264,000 vertex evaluations.
Captured motion attributes agree with the source survey; independently calculated
clip positions differ by less than 0.000011. Colour and shadow geometry agree after
their different projections are undone. Nearly 60,000 checked colour vertices move
between frames. A static control fails the same position tolerance for every draw.
These captures exercise cutout foliage and ivy, not the opaque foliage family.

The host clock and timestep are active, but no foliage velocity draw is observed.
Compiling previous-time variants therefore does not establish temporal motion-vector
coverage. An independent [EldenRingDLSS investigation](https://github.com/masonpetrosky/EldenRingDLSS/blob/main/docs/recon.md)
also reports absent native foliage velocity draws. Its [licence is MIT](https://github.com/masonpetrosky/EldenRingDLSS/blob/main/LICENSE);
it is consulted as research only, with no code copied or new dependency selected.
The source's shared wind direction remains a captured control, not a general
parameter translation. All programs, motion data and captures stay private.

Eight views captured twice in each game pass camera, anchor and held-host-environment
checks. Both games exit normally and the private profile is restored. Two short
fixed-camera recordings agree with independent screenshots within 0.4 mean byte steps.
Inspection of the paired views and sampled video frames finds no gross missing foliage
or displaced scenery. Some host foliage remains paler than the source, and distant
softness and atmosphere detail still differ. This is not a phase-synchronised comparison
or proof of temporal equivalence. The next attribution should isolate the pale foliage
response; another general lighting adjustment is not supported by these comparisons.

## Foliage composition attribution — 8 November

Matching source and host captures isolate three cutout foliage surfaces. Captured
rigid transforms, triangle connectivity and the first colour, normal and diffuse
environment subresources agree. Their mean diffuse response differs by about one
percent before source ambient occlusion. Source specular and local-light terms are
zero in these sampled pixels; this does not establish their general absence.

The larger colour mismatch occurs afterward. The host adds native lighting to the
already lit foliage result before applying the reconstructed source fog. Its blue
contribution accounts for much of the pale leaf appearance. Changing the native
specular field reduces that contribution without reliably removing it. A replay-only
explicit source-lighting marker instead makes composition use the supplied lighting
and suppress extra glow. More than half a million marked pixels follow that path,
while unmarked composition pixels remain exactly unchanged. The independent source
fog calculation agrees within its existing quantization tolerance. Sampled mean
colour errors fall from roughly 15–40% to about 1–4%.

The private candidate carries that marker in two cutout foliage shader families.
It preserves their depth, motion, textures and geometry. Other source-lit materials,
including opaque foliage, remain outside this change. Ambient occlusion, general
source specular and complete temporal equivalence remain open. Captured programs,
reconstructions and screenshots stay private under the previously recorded tool
permissions and licences; no production dependency or licence decision is made.

Two private builds match across 646 files. Only the two intended shader bundles and
the build record change. A fresh host capture checks more than 542,000 marked pixels
against the independent atmosphere calculation; the 99th-percentile relative error
is 2.34%, with no added glow. Sixteen screenshots cover eight fixed views and pass
pose, anchor and environment checks. Inspection finds the foliage closer to the
source without gross missing geometry or new seams. The game exits normally and
the private profile is restored. The ordinary preview and save remain unchanged.

## Source-lit scenery composition — 8 November

A fresh component export from the foliage-corrected host capture checks the tree in
that same frame. About 310,000 shared source/host interior pixels establish that the
host still adds native lighting after the translated environment response. The
source atmosphere rows agree with the selected host atmosphere control. Applying
that atmosphere independently explains both the source result and the host result
with its extra lighting within the existing quantization tolerance.

The first source prediction incorrectly added specular a second time: the captured
post-occlusion term already contains it. Correcting that accounting resolves the
failed prediction without changing the game. The earlier tree attribution above now
names the measured source term as accumulated lighting rather than diffuse alone.

A replay tags the opaque source-environment shader for the existing composition
path. More than three million newly marked pixels exclude native lighting. Supplied
lighting values remain identical, glow is zero, and composition pixels outside the
new marker remain exactly unchanged. The sampled tree's mean colour changes from
roughly 11–12% above the source to within 0.2% in each channel. This agreement in an
average does not establish texture-filtering or complete shading equivalence.

The build survey identifies 131 active scenery surfaces using this shader, including
the checked tree. Its pixel program matches the earlier private environment
control. The candidate changes only its final lighting marker; existing foliage
markers and the composition program remain unchanged. The checker first rejects the
previous build for the missing opaque marker. All programs, material bindings and
captures stay private under the existing permissions and licences. Source occlusion,
specular and local-light translation remain incomplete; no production decision is made.

Two builds match across 647 files, with differences from the prior build confined to
one shader bundle and the new build record. A fresh host capture checks 3.69 million
marked pixels against the independent atmosphere calculation; the 99th-percentile
relative error is 2.48%, with zero added glow. Sixteen screenshots pass the fixed
camera, anchor and held-environment checks. Inspection of all eight views finds the
tree and stone closer to the source without gross missing geometry or new seams.
Fine detail, atmosphere and some foliage still differ. The game exits normally,
the private camera profile is restored, and the ordinary preview and save are unchanged.
