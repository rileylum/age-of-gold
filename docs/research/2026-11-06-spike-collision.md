# Spike: writing Elden Ring collision ourselves

A snapshot as of 6 November 2026, from `spikes/er-collision/probe` on the `spike/er-collision`
branch, tested in game by Riley through me3 under GE-Proton11-3. Not a spec. Follows
`2026-11-06-spike-archives.md`.

## Result

Elden Ring accepts collision built entirely by our own code, and the player collides with it. In
the Chapel of Anticipation, invisible walls built from our triangles stopped the player, with the
camera behaving normally. No proprietary Havok tool was involved. The sloped ramp was not
confirmed: it fell inside the chapel's own walls.

## How it was built

- A template part is read from the player's own install. It supplies the physics scene, material,
  motion properties and body settings, and the tool replaces only the geometry: the compressed
  mesh, its trees, the triangle-to-shape-key table and FromSoftware's per-triangle parameters.
- Vertices are stored as shared vertices: 64 bits each, x, y and z quantised to 21, 21 and 22 bits
  across the mesh's bounding box, which is the vertices' extent plus a 0.015 margin.
- A triangle is a primitive of four vertex indices with the last repeated. It faces the side from
  which its vertices run counter-clockwise (right-handed, Y up).
- A section's bounding-box tree is binary and depth first. A leaf's byte is its primitive index
  doubled; an inner node's byte is odd, and half of it, doubled, is the distance to its right child.
  Each node's box is stored as shrinkage from its parent's; we write zero, which is loose but always
  encloses. The game accepted this.
- The SIMD tree is an empty node, a root with up to four leaves, and leaves holding up to four
  triangles' exact boxes and shape keys. A shape key is the primitive index doubled.
- The spike handles one section of up to 16 triangles. Larger meshes need several sections (at most
  127 primitives each, since a node byte holds the index), the top-level tree over sections, and a
  deeper SIMD tree.
- HKLib writes standalone tagfiles that carry their own type information, where the game's own
  parts refer to the map's compendium. The game accepts standalone files.

## What the in-game tests taught

- Only parts placed in the map layout (MSB) are loaded. A binder can hold parts the map never places;
  changing those has no effect.
- Each placed collision part has a hit filter in the map layout: Standard, Kill, FallDeathCam and
  others. The early cage tests put our geometry into every part, so it also became a kill and a
  fall-death surface: the camera swung down into the player as if falling, and the player died.
  Collision must go in parts whose filter matches its role, and our own parts will need their own
  map-layout entries.
- In the Chapel, the player and camera collided with the low-detail set (`l…hkxbhd`). Replacing the
  high-detail set (`h…hkxbhd`) changed nothing the player could see. What each set is for in
  general is not yet known.
- Rewriting every original part through HKLib unchanged played correctly but stuttered. Standalone
  files are larger than compendium-referencing ones and presumably slower to load. Worth measuring
  before the tool writes whole maps this way.
- Material ID 29 (from a special-purpose part) versus 1 made no visible difference; the hit filter
  was the cause.

## Still open

- Sloped surfaces, tight boxes, multiple sections, packed vertices.
- What the high-detail set is used for.
- Reading Dark Souls: Remastered's Havok 2015 collision, which HKLib does not read.
- Adding our own collision parts to a map layout rather than taking over existing ones.
- Navmesh, which this spike did not touch.
