# Spike: collision across several sections

A snapshot as of 6 November 2026, from `spikes/er-collision/probe`. Not a spec. Follows
`2026-11-06-spike-dsr-collision.md`. This checks synthetic geometry through serialized host-format
readback; the larger collision has not been tested in play, and no Firelink build was made.

## Result

`Build.ReplaceGeometry` now handles more than 16 triangles. It splits the input across sections,
writes the tree over those sections, builds a deeper SIMD tree, and updates shape keys and
geometry-dependent tables consistently.

The `geometrycheck` command reads a physics template from the host install, replaces its geometry
with triangles written from scratch, writes a standalone Havok file under the system temp
directory, and reads that file back. Both installs must be readable; this check reads only a
collision header from the source install, not its geometry.

| Synthetic fixture | Triangles | Sections | What it exercises |
|---|---:|---:|---|
| One triangle | 1 | 1 | Smallest mesh and key width |
| Disjoint triangles | 17 | 1 | SIMD depth beyond the old ceiling |
| Exactly 256 referenced vertices | 86 | 1 | Local vertex slot 255, mapped to a different global index |
| Disjoint triangles | 86 | 2 | Splitting at the local vertex limit |
| Repeated triangle, three vertices | 128 | 2 | Splitting at the primitive limit |
| Disjoint triangles, 65,520 vertices | 21,840 | 257 | Section index 256, wider keys, larger trees |

All six pass. Each written triangle's position and winding are compared with the synthetic input
within 0.001 units after quantisation. The checks also cover section and primitive reachability,
shape-key mapping, SIMD coverage and enclosure, interior-bit range, parameter-table references,
and byte-identical repeated serialization from fresh template reads in the same process. A
second command run also produced byte-identical files for all six fixtures across processes.

The scalar-tree decoder was also checked against a multi-section collision part read from the
host install: every node and leaf was accounted for, including each section's reference back to
its top-level leaf. HKLib provides the binary serialization and reading; our checks walk the
stored trees and reconstruct the geometry. This does not establish game performance or physics
behaviour.

The first 17-triangle test failed at the old 16-triangle limit before the deeper tree was written.
It then passed. The 86-disjoint-triangle test next failed at the single-section vertex limit;
it passed after section splitting was implemented. The remaining boundary and size checks were
then added and passed.

## Encoding learnt and checked

- A section has at most 127 primitives in this writer. Primitive corners use byte-sized local
  indices, so there are at most 256 vertex slots. Its local shared-vertex table maps those slots
  to the mesh's shared vertices.
- A primitive's key holds the section above its low eight bits. The low eight hold the local
  primitive index shifted left once, with the final bit selecting the triangle half of a quad.
  We write triangles, so that final bit is zero. The triangle-to-key table stores these keys
  left-aligned, with unused low bits set.
- The maximum key and key width must include gaps between sections. The interior bit field
  covers that key range, not just twice the number of input triangles.
- Top-level nodes use the high bit of their two-byte payload to distinguish branches from
  section leaves. A branch holds half its right-child distance; a leaf holds the section index.
  Each section records the index of its leaf in that tree.
- Section nodes use the payload's low bit to distinguish branches from primitive leaves. A
  branch holds its even right-child distance with that bit set; a leaf holds twice the primitive
  index. The earlier one-section writer already used this encoding.
- SIMD node zero is an empty sentinel. The inner root is node one; each nonempty lane names
  either a child node or a primitive key. The writer recursively groups at most four lanes per
  node. Its boxes include a collision margin and an allowance for shared-vertex quantisation.

## Spike choices and limits

- Triangle order is preserved. Sections are filled in input order until either capacity is
  reached. Spatial reordering is not implemented.
- Every vertex uses shared-vertex storage. One part can have at most 65,536 vertices; a larger
  part fails. Packed vertices and additional shared-vertex pages remain future work.
- The scalar trees retain their parent's bounds at every node. This conservatively encloses
  the mesh but provides little spatial pruning. SIMD boxes are calculated per subtree. Tight
  scalar boxes need attention if whole-map play exposes traversal cost.
- A part retains one host-template material and physics settings. Source material translation
  and map-layout placement remain open. The tests do not flatten Firelink's material submeshes
  into that template.
- The top-level encoding permits at most 32,768 section leaves. The writer rejects overflow,
  invalid vertex indices, nonfinite positions and unrepresentable extents.
- No new runtime dependency was added. Production language, library and licence choices remain
  undecided.

## Sources and licences

HKLib (`ac40bc9`, MIT) and SoulsFormatsNEXT (`e25d1a5`, GPL-3.0) are the same external spike
libraries as before. HKLib's public field definitions supply the serialized types.

Smithbox's [mesh reader](https://github.com/vawser/Smithbox/blob/5827c4c7c6709701ed32a304fa8a61897cabafaa/src/Havok/HKX2/Manual/hknpCompressedMeshShapeData.cs)
and [tree builder](https://github.com/vawser/Smithbox/blob/5827c4c7c6709701ed32a304fa8a61897cabafaa/src/Havok/HKX2/Builders/BVH.cs)
were format references. Its root licence at that revision is MIT. No implementation or embedded
physics defaults were copied; the writer uses our own implementation and reads physics settings
from the player's host install. These files are references, not build dependencies.

## Repeating the check

Build the probe with `dotnet publish -c Release -o bin/publish` from its project directory.
On this Linux machine, an isolated Wine prefix under `/tmp` keeps the test's Windows temp
directory under the system temp directory too:

```sh
mkdir -p /tmp/aog-scale-wine
WINEPREFIX=/tmp/aog-scale-wine WINEDEBUG=-all WINEDLLOVERRIDES='winedbg.exe=d' \
  AOG_DARK_SOULS_REMASTERED='Z:\mnt\storage\SteamLibrary\steamapps\common\DARK SOULS REMASTERED' \
  wine spikes/er-collision/probe/bin/publish/Probe.exe \
  'Z:\mnt\storage\SteamLibrary\steamapps\common\ELDEN RING\Game' \
  geometrycheck 'C:\users\riley\AppData\Local\Temp\aog-scale-check'
```

Use a fresh output directory for each run: results are created exclusively, never overwritten.
The command rejects output outside its system temp directory or inside either install, and
refuses to run the check in a CI environment. No game is launched.

The passing results from this session are under
`/tmp/aog-scale-wine/drive_c/users/riley/AppData/Local/Temp/aog-scale-large/`.
They contain host-derived physics settings and must stay outside the repository. A host-layout
inspection and its output also live under `/tmp/aog-scale-hostlayout*`; never commit those.

Next: validate the larger synthetic collision in play, then decide with Riley how to carry
Firelink's placement and material submeshes into a host map.
