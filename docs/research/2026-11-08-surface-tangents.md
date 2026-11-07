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

The private adapter, material bindings, texture payloads, shader inspection and captures
remain outside the repository. The existing SoulsFormatsNEXT dependency is GPL-3.0;
the external RenderDoc tool is MIT-licensed. No code from either tool or either game
is incorporated into this change, and no production dependency decision is made.
