# Packed texture coordinates and retained world content — 7 November 2026

This is a spike result, not a production format or library decision. Runtime inputs, generated
builds, third-party definitions and captures remain outside the repository.

The appearance converter reduced every surface's texture coordinates when any surface in the
same model exceeded the host's packed range. Its texture-record scale compensated numerically
on readback, but some host materials ignored that compensation in play. The earlier distant
tree repair addressed one instance of this wider problem.

Two floating-point coordinate trials were rejected. Both ordinary floats and packed-unit floats
serialized successfully, but produced flat, untextured surfaces in the checked host shader.
Serialization is not sufficient evidence that a vertex layout matches a shader's input contract.

`FirelinkUvPacking` instead retains the checked signed packed format. For repeating textures,
subtracting the same integer tile offset from every corner of a triangle preserves its sampled
colour and gradient. Triangles wider than the usable range are subdivided, with barycentric
interpolation preserving their position and vertex attributes. An explicit limit rejects unusually
wide input rather than allowing unbounded subdivision.

The authored fixture failed before implementation and passes afterwards. It checks packed bounds,
common integer offsets, positive winding and complete coverage for wide, negative and ordinary
coordinates. A private adapter applied the helper to the source diffuse channel across 314 meshes
and 526,533 triangles. Eight source triangles needed subdivision. Readback checked generated
topology, positions, normal quantization and coordinate quantization. Restoring changed
vertex/index/sampler fields reproduced the preceding model bytes. Texture payloads, placement,
collision and lighting remained unchanged. Two complete builds matched all 345 files.

Eight matched compass views, each captured twice, render textures with the corrected mapping.
This does not establish full-area traversal, complete material fidelity or correct sky filtering.
The older repository visual converter still has its preceding packing behaviour; the new helper
is exercised by the private appearance-stage adapter. These spikes are not yet one release tool.

`FirelinkWorldIsolation` extends the earlier scenery-only trial to retained host characters,
dummy placements, collision and connections. Selection comes from the installed native layout's
placement identity, not a range of model IDs. It preserves player placements and translated
content, even when that content uses a host model. Native map effects start disabled. An authored
serialized fixture failed first, then passed; restoring only the selected flags reproduces the
input map bytes.

The install trial disables 1,440 native placements and 12 map effects. Four matched compass views
still render the translated scene, but golden particles remain. Map flags therefore do not prove
complete world replacement. Weather effects, native events, location text, sounds and broader
streaming behaviour need separate investigation. No NPC or bonfire capability is implied.

Sources and licences: [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT), GPL-3.0,
provides the external spike's format reader/writer. The packing algorithm and fixtures are authored
here; no community implementation or game data was copied into them. The shader observations
come from private tests against both installed games.
