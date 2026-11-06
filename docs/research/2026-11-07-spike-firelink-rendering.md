# Spike: Firelink diagnostic vertex colours

A snapshot as of 7 November 2026, following the arrival play check. Not a spec.
Riley authorized continuing the Firelink demo spike through useful play checkpoints.

## Finding

The diagnostic intended to write opaque white vertex colours, but called
`new FLVER.VertexColor(1, 1, 1, 1)`. SoulsFormatsNEXT has both byte and float overloads.
These integer constants select the byte overload, which divides each channel by 255.
Consequently, the saved colour and alpha channels were almost zero. The earlier readback
checked triangle positions and indices, but not these shader inputs.

The writer now passes `1f` for each channel, selecting normalized floating-point values.
Readback requires every written vertex to have colour data with full white and alpha.
This corrects a demonstrated output error; whether it resolves all the black surfaces and
distance/angle-dependent fading still needs Riley's play check.

Inspecting the host template also showed that its selected mesh has only one face set,
without distance-detail flags. The earlier suggestion that the writer discarded its LOD
sets does not explain this particular template. Its material inputs and an opaque face-set
field are still altered by the diagnostic, so a broader rendering issue remains possible.
The proposed intact-host comparison was deferred when the constructor bug was found.

The only rendering change for this checkpoint is the normalized colour arguments. Geometry,
material selection, face-set settings, collision, placement and arrival logic are unchanged.
The diagnostic still displays collision surfaces with a host material; source visual models
and textures remain future work.

## Verification

The new check was added before the fix. Running the real-build workflow failed at the first
visible model with `Diagnostic terrain must read back with opaque white vertex colours`;
both collision sets had already passed. The failed output is under
`/tmp/aog-firelink-colour-red` and must never be committed or shared.

The source workflow was repeated after restoring the recorded temporary tooling. All
classified placements and both ordinary collision sets still match the reference reader,
and repeated extraction is identical. The existing transform and arrival fixtures pass.

After the correction, both full builds pass, including the new colour check on all visible
vertices. All 47 files match byte for byte between `/tmp/aog-firelink-colour` and
`/tmp/aog-firelink-colour-repeat`. Both collision sets also match the failed build's output
byte for byte. Publishing produced no warnings.

The playable profile is `/tmp/aog-firelink-colour/age-of-gold-firelink.me3`, with online play
disabled and the existing separate `AgeOfGoldSpike.sl2` save. Use New Game to exercise the
checked arrival. The play question is whether the terrain stays clearly visible while
moving and turning the camera. A passing readback does not establish the answer.

## Tooling and boundaries

No new dependency or production decision was introduced. SoulsFormatsNEXT (`e25d1a5`, GPL-3.0)
supplies the `FLVER.VertexColor` overloads and serialization. HKLib, soulstruct and
soulstruct-havok remain at the versions and licences recorded in the preceding snapshots.
No community implementation or game data was added to the repository.

The temporary Wine prefix, Python environment and soulstruct checkouts were recreated at
the paths in the preceding handoff, including disabling soulstruct's automatic file logging.
The inspection project and host-derived output are under `/tmp/aog-render-inspect`.
All generated files stay under system temp; both installs are read-only.
