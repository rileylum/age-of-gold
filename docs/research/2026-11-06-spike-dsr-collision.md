# Spike: reading Remastered's Firelink collision

A snapshot as of 6 November 2026, from `spikes/dsr-collision`, run against Riley's installs.
Not a spec. Follows `2026-11-06-spike-collision.md`. This proves reading source collision only;
it produces no host build and has not been tested in play.

## Result

Firelink Shrine is map `m10_02_00_00`. The source install has separate high- and low-detail
split collision binders in that map's directory, with `.hkxbhd` headers and `.hkxbdt` data.
Each set contains 53 parts. The parts read as Havok 2015 with one physics system and one rigid
body per part. The body's MOPP shape contains a custom extended mesh with material submeshes.

| Set | Parts | Submeshes | Stored vertices | Triangles |
|---|---:|---:|---:|---:|
| Low detail | 53 | 132 | 124,594 | 57,980 |
| High detail | 53 | 97 | 159,319 | 75,981 |

These are stored counts, not deduplicated geometry. Every part parsed, and every submesh's
vertices, faces and material assignment matched a separate read through soulstruct-havok's
`MapCollisionModel` API. Both paths use its underlying Havok parser; this is not an independent
validation of the binary format. No part was skipped or replaced.

The first low-detail part has five submeshes and 1,892 triangles. The command writes its local
mesh arrays to JSON under the system temp directory. Vertices and faces retain all four stored
components; the first three components provide positions and triangle indices. The result also
retains body and submesh transforms, extrusion and alternating-winding flags. It does not apply
map-layout placement or translate physics settings or materials into host equivalents.

All inspected index streams use 16-bit indices. The reader checks that index streams are
unambiguous, positions are finite, indices are in range, and geometry counts agree with the
triangle descriptors. Different Havok versions, extra bodies, non-triangle shapes and mismatched
submesh/material counts fail rather than falling back.

## Tooling and the game-data boundary

- [soulstruct-havok](https://github.com/Grimrukh/soulstruct-havok), commit
  `bf2d41fc83de4a43bd3ed3df8605741f143cad2c`, declares GPL-3.0-or-later.
- [soulstruct](https://github.com/Grimrukh/soulstruct), commit
  `12b69189a2ccebbc623a1b6565be89a18d6c9958`, declares GPL-3.0-or-later.
  Its map constants provided the map-name reference; the file layout was checked in the install.
- Both are external source checkouts under `/tmp`, loaded through `PYTHONPATH`. No community
  implementation was placed in this repository. This use is for the spike and does not settle
  the production language, libraries or licence.
- soulstruct-havok bundles collision templates and helper executables. Its high-level package
  initializer also imports unrelated event modules, which try to read bundled event metadata.
  A guarded import caught that read before it happened. The probe imports only `Binder` and
  `HKX`; the check loads the reference collision module directly without that initializer.
- A local change to soulstruct's `config.py` disables the `AUTO_SETUP_LOG` default, preventing
  import-time logging to the user's home directory. Neither source checkout otherwise needs
  changes. The probe disables bytecode writes and uses a Python audit hook to reject reads of
  non-code resources under the library roots, library writes, writes outside its one result,
  and subprocess/directory mutation calls. This also blocks the Havok reader's attempt to
  generate source modules when it encounters unknown types. This is a check on the exercised
  Python I/O path, not a security sandbox for native code.
- A guarded scan of both sets completed without any bundled resource reads. No template,
  executable, archive key, name dictionary or paramdef from a third-party package was needed.
- The libraries warn that Oodle is unavailable on Linux. Remastered's inspected collision did
  not need it. No Wine, Proton or Havok executable ran for this spike.

The isolated Python environment contains these support packages, with licences taken from
their installed metadata: constrata 1.3.3 (MIT), numpy 2.5.4 (BSD-3-Clause with bundled 0BSD,
MIT, Zlib and CC0-1.0 components), scipy 1.18.1 (BSD-style, with bundled component notices),
rich 15.0.0 (MIT), typer 0.27.3 (MIT), frozendict 2.5.1 (LGPL-3.0-only), zstandard 0.25.0
(BSD-3-Clause), colorama 0.4.6 (BSD), annotated-doc 0.0.5 (MIT), markdown-it-py 4.2.0 (MIT),
mdurl 0.1.2 (MIT), Pygments 2.21.0 (BSD-2-Clause), shellingham 1.5.4 (ISC).

## Repeating the check

Temporary setup for this session:

- `/tmp/aog-soulstruct` and `/tmp/aog-soulstruct-havok`: checkouts at the commits above.
- `/tmp/aog-dsr-collision-venv`: Python 3.14.5 with the support packages above.
- `/tmp/aog-firelink-first.json`: the first low-detail part's extracted mesh. Never commit it.

For a fresh setup, recreate the checkouts and environment and disable soulstruct's automatic
file logging as described above. These paths are temporary; they are not dependency management
for the tool.

```sh
export AOG_DARK_SOULS_REMASTERED='/mnt/storage/SteamLibrary/steamapps/common/DARK SOULS REMASTERED'
export AOG_ELDEN_RING='/mnt/storage/SteamLibrary/steamapps/common/ELDEN RING/Game'
export PYTHONPATH=/tmp/aog-soulstruct/src:/tmp/aog-soulstruct-havok/src

/tmp/aog-dsr-collision-venv/bin/python -B spikes/dsr-collision/check_installs.py
/tmp/aog-dsr-collision-venv/bin/python -B spikes/dsr-collision/probe.py /tmp/firelink-part.json
```

The command accepts `--detail h` or `--detail l` and a zero-based `--entry` ordinal. Output must
be a new file under the system temp directory, outside the repository and both installs. The
host executable must be readable, but this spike does not inspect host collision. The source
environment variable is provisional spike configuration; production configuration remains open.

The first CLI check was written before the reader and failed because the command did not exist.
It then passed against the real install. The manual checks cover the CLI result, exact mesh
comparison for every part in both sets, byte-identical repeated extraction and refusal to
overwrite an existing result. They require the installs and fail when either is missing; they
are not CI tests and explicitly reject a CI environment.

## Next

The host collision writer still handles at most 16 triangles in one section. Scale it using
meshes written from scratch, then feed in the source geometry. Section-local vertex indices,
section shape keys, the top-level section tree and the deeper SIMD tree all need to agree.
Material translation and map-layout placement remain unimplemented. Reading all the binder's
parts does not yet establish which ones the source map places or which role each part serves.
