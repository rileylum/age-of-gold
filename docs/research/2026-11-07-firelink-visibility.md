# Firelink visibility investigation — 7 November 2026

Riley approved investigating the distant scenery in the atmosphere preview. The investigation
separates retained host scenery from translated source scenery whose visibility groups were
flattened. The cliff and city wall occur in both games' matched views; their presence alone is
not a fault. Sky filtering and material fidelity remain separate work. Aqueduct repairs remain
deferred.

## Retained host scenery

The spike still retains native map pieces and props below and around the translated map. The
new `firelink-scenery` command reads their placement identities from the host install and disables
the corresponding placements in a fresh build. It checks both placement type and model identity;
sharing a model is insufficient to identify a native placement. It preserves the rest of the
map, including translated scenery, collision, environment regions and event references.

The implementation uses the map format's release-disable flag. Restoring just those flags in
the serialized result must reproduce the entire uncompressed input map byte for byte. Other
files remain identical. Both map fingerprints must match the input build's recorded installs.
This is a spike operation, not a complete removal of the host world: native effects, characters,
connections and collision remain.

The authored serialization check failed before implementation because retained scenery stayed
enabled, then passed. It includes an imported placement sharing a native model and unrelated
collision and lighting records. Two real-install builds produced identical files. The caller
check requires a fresh system-temp output outside both installs, the input build and repository,
and refuses CI. Environment repair, rigid geometry, transform and arrival fixtures also pass.

```sh
dotnet publish spikes/er-collision/probe/Probe.csproj -c Release \
  -o spikes/er-collision/probe/bin/publish
AOG_DARK_SOULS_REMASTERED=/path/to/source \
AOG_ELDEN_RING=/path/to/host/Game \
WINEPREFIX=/tmp/aog-scale-wine \
python spikes/firelink-demo/check_scenery.py /tmp/input-build /tmp/fresh-output
```

## Source visibility groups

The translated map currently gives every imported visual and solid collision broad visibility
masks. The source instead uses many distinct group combinations. An external trial widens
source visual draw groups and source solid-collision display groups into the host's arrays.
It reads all values and placement bindings at runtime; none enter the repository.

The trial deliberately leaves collision draw groups and collision masks broad. Some source
collision draw/display sets do not overlap themselves, and special group-switch collision roles
are not yet translated. Blindly transferring every mask risks hiding the player or scenery at
transitions. A correct serialized map alone does not prove correct runtime visibility.

The trial's source-mask check failed on the unchanged build and passed on its output. Restoring
only the changed masks reproduces the input map; all other files are unchanged. In the runtime
comparison it hides the player and native particles at the fixed checkpoint, without removing
the thin background artifact. It is rejected for the playtest build. Source and host group-bit
assignments need an explicit mapping that preserves host character visibility; copying the
source values into the same bit positions is not sufficient. Traversal is also still unproven.

## Background-tree artifact

The thin distant shape persists with retained host scenery disabled. A local ray intersection
against the converted models identifies a source background-tree surface. Its texture coordinates
are reduced by a model-wide packing scale, with compensation placed in the host texture record.
The tree surface itself fits the packed coordinate range without that reduction.

An isolated external trial restores this surface's source texture coordinates and unit sampler
scale. The source-coordinate check fails on the preceding build, then passes after writing and
reading back the trial. Restoring only those fields reproduces the prior model bytes. Geometry,
texture payloads, other surfaces and lighting are unchanged; two complete builds repeat exactly.
In the matched runtime view, the thin column disappears and distant tree silhouettes return.
The model-wide reduction and texture-record compensation therefore did not preserve the mapping
for this surface in the host shader. The chosen candidate is recorded in `HANDOFF.md`. Other
model-wide UV reductions remain to be investigated before generalizing this correction.

## Comparison conditions

The private capture suite now supports eight compass views around the existing camera point.
Each view is captured twice; camera frame, field of view, player position, noon clock and weather
type are checked. Both games use isolated saves and exit through their normal UI.

The first scenery candidate repeatedly settled a small distance away from the old player
checkpoint. The strict position check correctly stopped those runs. A separately saved, settled
checkpoint loaded correctly in the baseline but displaced again in the candidate. A background
configuration override also initially selected the old anchor; that was corrected without
widening any tolerance. Failed runs are not successful comparison evidence.

The external host camera adapter now supports a bounded correction to the calibrated player
position. It refuses corrections beyond one metre, writes once, and the runner requires both
immediate readback and a settled position check before capturing. This keeps the old camera
poses and comparison anchor. The command failed as unsupported before implementation. The
manual Firelink playtest save is separate and is not reset. This controller remains external;
it is development tooling, not code loaded by the normal playtest profile.

## Sources and licences

- The player's source and host installs: read-only inputs; all extracted metadata, builds,
  checkpoints and screenshots remain outside the repository and releases.
- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT), GPL-3.0: the existing external
  spike parser supplies the map fields and serialized readback. No library code was copied into
  the repository. Its use still does not settle the production library or project licence.
- [How drawgroups and displaygroups work](https://soulsmodding.com/doku.php?id=tutorial:how-drawgroups-and-displaygroups-work),
  by Thegreatgramcracker, CC BY-SA 4.0: background on collision-driven visibility, player
  inheritance and transitions. In particular, empty draw groups are not a reliable way to hide
  an object. No prose or code was copied.
- [FreecamMod](https://github.com/Logersnamed/FreecamMod), MIT: the existing external camera tool's
  player-physics position access informed the bounded correction. Its bindings and the adapter
  remain outside the repository, alongside the earlier comparison tooling.
