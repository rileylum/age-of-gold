# Spike: placed Firelink terrain in the host

A snapshot as of 6 November 2026. This is an intermediate diagnostic for the Firelink demo,
not the walking skeleton. Riley authorized continuing until a demo or another useful play check.
The generated terrain has passed readback. The corrected arrival works in play, and Riley can
stand and walk on the collision. Rendering is broken, preventing a useful navigation check.

## What is built

The source map layout has 52 collision placements referencing 48 models. The reader classifies
every placement by its source hit-filter role. Of these, 39 placements use ordinary solid ground,
referencing 38 models. Both collision detail sets are joined to those placements by model name.
One ordinary placement has a translation; none requires a rotation or scale change. The source
respawn event supplies the initial position and orientation at runtime.

The diagnostic writes 39 new collision parts and 39 visible map parts into the Chapel host map.
Their relative source placement is preserved. A common translation positions the terrain above
the Chapel, keeping its existing loading setup available without overlapping its buildings.
The primary host player placement is moved to the source respawn's horizontal position, just
above the nearest supporting ground. All generated values stay under the system temp directory.

| Output | Parts | Nonzero-area triangles |
|---|---:|---:|
| Low-detail collision | 39 | 35,304 |
| High-detail collision | 39 | 51,110 |
| Visible collision surfaces | 39 | 35,304 |

These visible surfaces come from source collision, with a material and vertex layout read from
a host map piece. They are a way to see and test the terrain. Source visual models, textures and
surface behaviour are not translated by this diagnostic. Each collision retains host-template
physics settings. Source material identifiers are recorded in the local build report so that
this deliberate loss is explicit.

The thirteen special-role placements are recorded as outside this diagnostic: water, NPC-only
collision, death-camera and fall triggers, kill planes, group switching and map exits. Unknown
filters still fail. The host map's other content remains at its existing positions; this is not
the empty, player-only world required by the walking skeleton.

## Translation and checks

- Source submesh scale, rotation and translation are applied before the body transform, then
  map placement and the common diagnostic translation. The current placement adapter accepts
  only unrotated, unit-scale placements and fails on anything else.
- Source faces are reversed into the counter-clockwise convention of the checked host writer.
  The first spawn check failed because ground beneath the source respawn faced downward under
  the unconverted winding. Inspecting the source intersections exposed the mismatch. An authored
  triangle fixture then failed on winding conversion and passed after the adapter was corrected.
  That fixture also checks a rotated and translated body, placement, and a supporting surface point.
- Zero-area faces have no surface normal and are excluded explicitly: 49 in the placed low-detail
  geometry and 87 in high detail after float transformation. The local build report records the
  counts. The source reader preserves all raw faces before this diagnostic conversion.
- The source workflow test first failed because the layout command did not write a result. It
  now checks all classified placements, compares both collision sets for every ordinary placement
  against the external collision-model reader, and checks byte-identical repeated extraction.
  Both readers share the underlying Havok parser, so this is not independent binary-format proof.
- The host workflow test first failed because the build command did not exist. It now checks
  serialized triangle positions and winding, tree coverage and keys, compressed binder contents,
  visible-model geometry, written map placements, and support beneath the written player spawn.
- The build records hashes of the source files used, the host files read, the probe assembly and
  the intermediate geometry. The me3 profile explicitly disables online play and uses the
  existing separate spike save. Publishing the probe produced no warnings.
- A second independent command run produced byte-identical output for all 46 generated files,
  including the report and profile. me3 accepted the profile and was launched for Riley's check.

The established host-template locators and the round-trip of the remaining host collision parts
are retained from the successful cage test. The latter previously stuttered; this diagnostic does
not yet address that performance issue. Runtime placement and material values are never embedded
in the source code or this snapshot.

## Tooling and licences

No new runtime dependency was introduced. The external spike libraries remain SoulsFormatsNEXT
(`e25d1a5`, GPL-3.0), HKLib (`ac40bc9`, MIT), soulstruct (`12b6918`, GPL-3.0-or-later), and
soulstruct-havok (`bf2d41f`, GPL-3.0-or-later). Versions and support-package licences are recorded in
the preceding snapshots. Their use does not settle the production language or licence.

SoulsFormatsNEXT's MSB and FLVER readers and writers supply the format types. The source-role
classification was checked against soulstruct's
[collision hit-filter enum](https://github.com/Grimrukh/soulstruct/blob/12b69189a2ccebbc623a1b6565be89a18d6c9958/src/soulstruct/darksouls1ptde/maps/enums.py).
No community implementation, bundled resource, material preset or game data was placed in the
repository. Physics, renderer settings and shader references come from the player's host install.

## Opening arrival correction

Riley's first New Game remained in the Chapel. The me3 log confirmed that the modified map,
collision binders and all 39 new visible models were loaded, ruling out a missing map override.
Reading the opening events from the host install showed an intro cutscene and a separate
respawn-region assignment. Changing the MSB player part alone did not control arrival after
the intro.

The diagnostic now locates that event by its instruction types, reads its character and respawn
references at runtime, moves the referenced respawn region to the supported terrain position,
and appends a short warp after the existing opening event. Appending keeps existing instruction
indices and relative branch distances intact. Ambiguous or parameterized opening-event layouts
fail. No host event IDs, character IDs, region IDs, game coordinates or host script fragments were
embedded in the implementation.

An authored fixture first failed because its respawn region remained at the old position. It
now passes after serialization, including the warp's argument layout. The real-build check reads
the written event and map back, verifies the destination, and removes the new instruction from
the readback in memory: the remaining serialized event file must exactly equal the serialization
of the untouched host input. Both full builds pass; all 47 generated files match byte for byte.
The prior terrain files are unchanged. Only the map, build report and new event override differ.

Opcode signatures were checked against the public
[Elden Ring EMEDF reference](https://soulsmods.github.io/emedf/er-emedf.html).
Its hosting repository states no licence; it was used as format documentation, with no
implementation or bundled metadata imported. SoulsFormatsNEXT remains the only event-format
dependency. The host events inspected during diagnosis stay under `/tmp/aog-spawn-inspect/`.

The corrected profile is `/tmp/aog-firelink-arrival/age-of-gold-firelink.me3`. It was launched
offline after Riley exited the first test. New Game is required to exercise the opening event;
the transfer happens after the intro. Riley's second play check confirmed arrival on the terrain
and the ability to stand and walk. Collision felt right from memory, but the terrain appeared as
black shapes that faded in depending on distance and viewing angle, making navigation impractical.
This proves arrival and basic collision support in play; it does not prove complete traversal,
placement fidelity across the area or usable rendering. The rendering cause remains undiagnosed.
Work stopped after recording this result, as Riley requested for the night.

## Repeating the diagnostic

Publish the C# probe from `spikes/er-collision/probe` with
`dotnet publish -c Release -o bin/publish`. Then, from the repository root:

```sh
export AOG_DARK_SOULS_REMASTERED='/mnt/storage/SteamLibrary/steamapps/common/DARK SOULS REMASTERED'
export AOG_ELDEN_RING='/mnt/storage/SteamLibrary/steamapps/common/ELDEN RING/Game'
export WINEPREFIX=/tmp/aog-scale-wine
export PYTHONPATH=/tmp/aog-soulstruct/src:/tmp/aog-soulstruct-havok/src

/tmp/aog-dsr-collision-venv/bin/python -B spikes/firelink-demo/check_source.py
/tmp/aog-dsr-collision-venv/bin/python -B spikes/firelink-demo/check_build.py /tmp/aog-firelink-arrival
me3 launch --online false -p /tmp/aog-firelink-arrival/age-of-gold-firelink.me3
```

Use a fresh temporary output directory for each build. Both install checks refuse CI. The source
reader keeps its existing audit guard against bundled library resources and unexpected writes.
The generated files and report contain game-derived data and must never be committed or shared.

The play check uses New Game in `AgeOfGoldSpike.sl2`. The next session should address rendering
so Riley can see the terrain and assess navigation, then continue toward source visual models
and textures. The walking skeleton remains incomplete.
