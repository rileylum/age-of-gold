# Firelink light direction — 7 November 2026

Riley clarified that brightness is acceptable. Light coming from behind the chapel leaves
its visible side dark and casts large shadows. The next diagnostic changes direction only;
it does not increase intensity or exposure. The complete native colour-only material remains
the input to this comparison; its source texture fidelity has not been explicitly confirmed.

## Reading the two installs

The temporary inspector reads twelve host graphics-parameter files found by hashing paths
our own code constructs against the host archive index. It does not use a community file-name
dictionary. The current map's main directional light has a two-component angle track with
28 entries across light IDs and time of day. Inspection suggests the first component controls
elevation and the second horizontal direction. The proposed change tests that interpretation
in play. A companion graphics file has no populated main-light angles.

The source install contains its own parameter-definition binder. Those definitions let the
inspector read all twelve tables in the source map's drawing-parameter binder without using
community schemas. Source lighting has several directional lights, separate specular settings,
shadow-source angles and light-scattering settings. A faithful translation must establish their
coordinate conventions and how the host combines them; this diagnostic does not settle that.
Extracted tables, definitions, values and files remain under `/tmp/aog-light-inspect`.

## Direction-only diagnostic

`FirelinkLighting.ReverseDirection` offsets every horizontal angle in the host's primary light
track by half a turn. It preserves elevation, colour, intensity, time of day, light IDs and all
other graphics settings. Angles remain unwrapped: wrapping individual keyframes could change
the interpolation path. The writer rejects an unclassified graphics format, an empty or
unexpected angle field, and non-finite values. Secondary lights and atmospheric effects retain
their host settings, so this is not a complete lighting translation.

The authored `firelink-lightcheck` fixture first failed with the unchanged direction. After
implementation it passes through serialization and readback, including an angle beyond half
a turn. Restoring the two authored angle values restores every fixture byte, proving unrelated
settings and timing are preserved. Publishing succeeded without warnings.

The temporary inspector's `lightdirection` mode invokes this writer against the extracted host
graphics file and adds the result to the checked native-material build. Readback verifies all
28 changed entries. Restoring only those angle values reproduces every uncompressed byte of
the host input, including unknown fields. All 196 input build files are byte-identical; models,
images, collision, placements, events and the offline separate-save profile are unchanged.
The diagnostic report records input and tool fingerprints and distinguishes inherited reports.

All 198 files match across `/tmp/aog-firelink-light-direction` and its `-repeat` build. The
profile is `/tmp/aog-firelink-light-direction/age-of-gold-firelink.me3`, using the existing
`AgeOfGoldSpike.sl2` save. Riley confirmed quitting the preceding game and process inspection
confirmed its exit. The profile was launched offline; me3 confirmed attachment in
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_12-38-12.log`. The
expected observation was light falling on the opposite side of the chapel, with shadows cast
the other way. Riley confirmed the direction change works, but it gives little improvement:
the overhead tree and city walls leave little direct light reaching the area. Changing direction
alone does not resolve the darkness. Brightness settings remain unchanged.

## Ambient and environment lighting

Riley asked whether the source uses global or ambient illumination. Inspection of the source's
own parameter definitions identifies separate upper- and lower-hemisphere ambient colours,
each with an intensity multiplier expressed as a percentage. Both are nonzero in all five
lighting rows referenced by Firelink's map pieces. The main light table also contains diffuse
and specular environment-light settings, and the drawing binder includes an environment-texture
table. These are concrete source lighting inputs the current visual writer does not translate.
They establish ambient and environment-light configuration, not a complete account of the
source renderer or evidence of real-time light bouncing between surfaces.

The host's current graphics file also has populated upper- and lower-hemisphere colour tracks.
The diagnostic therefore has host ambient settings, rather than no ambient light at all. Its
source placements inherit the host template's default light-set selection; their source
lighting-row choices are not carried across. Raw intensity values from the two engines are
not enough to establish a faithful conversion: colour space, units and shader response still
need checking. The earlier direction diagnostic leaves all these fields untouched.

The existing visual inspection also contains three explicit baked-light texture bindings.
The current colour-only material omits those bindings, along with environment lighting and
source vertex colours. Their exact runtime contribution remains unmeasured. This makes the
missing source lighting a stronger next lead than another sun-direction change. The next
implementation should preserve each source placement's lighting selection and establish the
hemisphere mapping through an authored fixture and an isolated play comparison. It should
leave direct-light intensity and exposure alone while this contribution is checked.

The temporary `shadowinspect` mode checked the two map layouts. All 67 source map pieces have
the basic shadow-casting flag clear, as do all 87 map pieces in the written host layout. This
does not identify excessive casting caused by that flag, or prove equivalent shadow behaviour
between engines. Source shadow-receiving flags vary; the host placements uniformly inherit
the template's enabled setting. Material opacity, other loaded scenery, baked lighting and
the host's shadow mechanisms remain separate questions. No flags were changed during this
inspection. Detailed records stay in `/tmp/aog-light-inspect/shadow-flags.json`.

## Source hemisphere comparison

Riley agreed to match the source's ambient lighting next. `FirelinkAmbient` now reads the main
source light table with the parameter definition from the source install. It normalizes the
stored RGB channels to unit values and converts the declared percentage strengths to
multipliers. Zero intensity and values above full strength are preserved. Unclassified field
types, negative values, out-of-range RGB, missing rows and mismatched schemas stop the build.

This is an explicit first numeric mapping, not a claim of a visual match between renderers.
It adds no fitted gain or gamma curve. The source's static ambient colours and strengths are
held constant across the host's existing time keyframes. The play comparison must establish
whether that mapping gives the expected shaded-surface response. Environment maps, baked-light
textures, source vertex colours and source direct-light settings remain untranslated.

The visual builder now records the source placement name for each generated host placement in
its temporary report. The lighting stage reads each source placement's light-row choice and
allocates a distinct unused host light-set ID for each used row. It adds complete light tracks
cloned from the host's default set, replaces only the two hemisphere colours, and extends the
graphics file's light-set index. Existing host sets remain intact. Only the generated scenery
selects the new sets; the character and other host placements retain their current selections.
The file's one-byte track-count capacity is checked before writing.

Authored binary fixtures first failed for the missing source rows, then for unchanged host
placement selections. Both pass after implementation. The fixtures independently anchor RGB
normalization and percentage conversion, including zero and above-full strength; reject missing
rows and invalid colour/strength values; and check distinct/shared per-placement selections,
time tracks, direct-light preservation, the light-set index and byte-for-byte repeatability.
Removing the added tracks and restoring placement selections reproduces both authored inputs.

The complete source build workflow passes with the placement report added. All its files except
the report match the previous checked generic-material build. The established complete native
colour-only material and half-turn diagnostic were reapplied to that build. All 194 game files
in the prepared input match the preceding playable direction-only candidate byte for byte.
This keeps the ambient comparison on the same geometry, materials, textures and sun settings.

The manual `spikes/firelink-demo/check_ambient.py` workflow reads both install locations from
the existing environment variables, refuses CI and writes to a fresh system-temp directory.
It invokes the authored checks and the `firelink-ambient` stage. Real-install readback verifies
all 65 placements and five ambient settings. Each new track retains the host default's other
fields and timing. Removing the new tracks restores every uncompressed graphics input byte;
restoring only the light-set selections restores every uncompressed map input byte. All 196
other input files remain byte-identical. The additional report records source/input/tool
fingerprints, the mapping and remaining limits; inherited reports describe their input stages.

All 199 output files match between `/tmp/aog-firelink-ambient` and its `-repeat` build.
Publishing succeeded without warnings. The candidate profile is
`/tmp/aog-firelink-ambient/age-of-gold-firelink.me3`, with the existing separate test save.
Visual matching and improvement beneath the tree and walls still need Riley's play check.
The previous game was observed to have exited. This profile was launched offline and me3
confirmed attachment; log `~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_12-57-30.log`.

## Checking runtime light selection and hemisphere response

Riley reports that the ambient candidate appears to make no difference. The supplied images
still show almost-black shaded scenery. This does not establish runtime use of the new light
sets or an adequate hemisphere contribution. The loader log confirms that the candidate's
graphics file and map layout were opened; file redirection alone cannot prove their rendering
effect. No screenshots enter the repository.

The next comparison uses authored diagnostic colours. Main-light diffuse RGB is magenta for
the newly allocated light sets and cyan for the pre-existing sets, preserving each keyframe's
strength. Both hemisphere colours are green with an authored diagnostic strength across all
sets. Direction, specular and secondary lights, exposure, materials, geometry, collision and
placement selections remain unchanged. These colours test the route through the lighting
system; they are not a proposed appearance for the mod.

Magenta direct light on scenery would support selection of the new sets; cyan would instead
support use of an existing set. Green fill on shaded scenery would demonstrate a hemisphere
response. A green character with persistently dark scenery would point toward a difference
between character and scenery shading. Mixed colours and secondary lights must be considered
when interpreting play; no outcome is established yet.

The temporary inspector's `ambientprobecheck` failed on the preceding build for absent control
colours. `ambientprobe` writes the three controls and verifies every entry after serialization
and disk readback. Restoring those three value arrays restores every uncompressed input byte.
All 198 other input files remain identical. All 200 output files match between
`/tmp/aog-firelink-ambient-probe` and its `-repeat` build. Publishing succeeded without warnings.
The profile retains the same offline separate save; the source-valued candidate remains intact.
The preceding game was observed to have exited, and the colour diagnostic was launched offline.
Riley accidentally closed that launch; process inspection confirmed exit before relaunching.
me3 confirmed attachment on the relaunch; log
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_13-23-06.log`. Play evidence is pending.

Riley's next play result shows magenta sunlit scenery and cyan illumination on the character.
The supplied images agree. This establishes that the scenery selects the newly allocated light
sets and that their main diffuse-light fields affect rendering. It rules out failed per-piece
selection as the explanation for the ambient candidate's apparent lack of effect. Riley sees
no other diagnostic colour; shaded scenery stays very dark. The green hemisphere contribution
is therefore not visibly established. Cyan on the character does not separately prove a green
hemisphere response, since its direct-light control is already cyan.

The next investigation is the host's environment-lighting coverage and the complete basic
material's ambient response. Firelink is placed above the host starting map while environment
data has not been translated. That positional mismatch is a hypothesis to check, not a proven
cause. The source numeric mapping should not be retuned until the rendering route is understood.

## Environment coverage diagnostic

Read-only inspection found twenty environment points and twenty effect boxes in the host map.
The generated arrival is above every box even when allowing the full height above its origin;
all boxes have only horizontal rotation. This establishes a coverage gap at the arrival, not
that it causes the dark scenery. The builder retained those native regions while moving the
player and generated scenery upward. One native light bank also parsed, but its lights are
not changed by this diagnostic. No external filename dictionary was used.

The next candidate starts from the source-valued ambient build, removing the temporary colour
controls. It shifts all native environment points and effect boxes by the difference between
the native player placement and the generated supported arrival. The points and boxes retain
their identities, dimensions, orientation and settings. No environment texture, intensity,
sun, material, geometry or collision changes are proposed. This uses the host environment to
test the rendering route; it does not translate the source's environment lighting.

The position check failed on the unchanged input for unmoved environment regions. The written
candidate passes serialization and disk readback for all forty regions. Restoring their positions
reproduces every uncompressed input map byte, preserving unknown fields and all other regions.
The extracted native map fingerprint matches the checked build input. All 198 non-map input
files remain identical, and all 200 files match the repeat build. The offline profile and
separate save are unchanged. These checks establish the diagnostic's scope, not its visual result.

The previous game was observed to have exited. The candidate was launched offline at
`/tmp/aog-firelink-environment/age-of-gold-firelink.me3`; me3 confirmed attachment in
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_13-36-45.log`.
The play question is whether shaded scenery becomes readable, particularly near the arrival.
Source environment translation and material ambient response remain unresolved.

Riley reports a partial improvement from the relocated environment regions: more brickwork
and rock detail is visible, but the average image remains too dark and some areas are almost
black. This supports an environment-lighting contribution in play. It does not establish
faithful source lighting or adequate coverage and fill throughout Firelink. No screenshots
were saved in the repository.

## Environment fill strength diagnostic

A read-only geometry check tests the generated scenery vertices against the moved boxes.
Using the map editor's bottom-origin box convention, about 99% of vertices within an authored
80-unit radius of arrival are inside at least one box. Across the whole generated scene,
about 74% are inside. These are vertex counts, not surface-area or engine-selection measurements;
box priority, blending, camera position and the lighting data remain separate concerns.
Coverage away from arrival is incomplete and is not fixed by a strength change.

The next controlled candidate doubles only the effect boxes' `PointLightMult` field. The
library identifies this as light supplied from environment points. Specular multipliers,
point and box placement, dimensions, directional lights, exposure and materials are unchanged.
The factor is an authored diagnostic gain, not a source-derived calibration or a fidelity claim.
It tests whether stronger environment illumination lifts the remaining nearby shaded surfaces.

The temporary inspector's `environmentgain` first failed with the unchanged multiplier, before
the write was added. The completed transformation verifies all twenty doubled values after
serialization and disk readback. Restoring those values reproduces every uncompressed input
map byte. All 199 non-map input files remain identical. All 201 output files match the repeat
build; publishing succeeded before each run. Both installs were available and the offline
profile retains the same separate save.

The previous game had exited. The candidate was launched offline at
`/tmp/aog-firelink-environment-fill/age-of-gold-firelink.me3`; me3 confirmed attachment in
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_16-10-41.log`.
Its effect on the dark chapel and walls still needs a play check.

Riley confirms further improvement with doubled environment fill, but the chapel face in
shadow remains almost black. The gain stays at this level while isolating the remaining
suppression; raising general fill again is not the next comparison.

## Inherited light-mask diagnostic

The host's environment packages were found through the documented binder filename convention,
then verified by archive membership in the local install. Twenty-one binders parsed across
quality tiers and states. Their members include BC7 volume textures carrying GIIV channel
identities and BC6H reflection cubes. The volumes retain the host map's precomputed data even
when their effect regions move. This makes mismatched environment data a concrete concern,
but the inventory alone does not identify the cause of a particular black surface. No volume
pixels were decoded, edited or used to infer an undocumented coefficient mapping.

The current graphics file contains a scalar `LightMask Influence` track under its light sets.
All five new scenery sets inherited full influence. The next candidate sets only those five
values to zero. Existing light sets and every other graphics field remain unchanged, including
sun direction, colour and intensity, hemisphere values, exposure and shadow parameters. Map
shadow flags, moved environment regions, doubled fill, materials and geometry are untouched.
The exact runtime scope of this mask field remains a play question; the candidate does not
claim to preserve every visible shadow or to implement the source's shadow configuration.

The unchanged temporary writer failed first because the selected values retained influence.
After the transformation was added, all five values pass serialization and disk readback.
Restoring them reproduces every uncompressed input graphics byte. All 200 other input files
remain identical; all 202 output files match the repeat build. Publishing succeeded before
running each executable. The profile remains offline and uses the same separate save.

The preceding game was observed to have exited. The comparison was launched at
`/tmp/aog-firelink-light-mask/age-of-gold-firelink.me3`; me3 confirmed attachment in
`~/.local/share/me3/logs/age-of-gold-firelink/2026-11-07_16-23-54.log`.
The next observation is whether the black chapel face becomes readable at the retained fill
level. Neither removal of the mask's effect nor faithful source shadows are proven yet.

Riley reports that the chapel is still almost black and may look worse. This diagnostic has
not demonstrated an improvement. Keep the doubled-fill build as the earlier partially improved
comparison; do not promote the mask change as a fix. The game was subsequently observed to
have exited; it was not terminated by the agent.

Riley asked to pause lighting changes and investigate automated views in both games: launch,
enter the world, set camera position and orientation, and capture screenshots. Further lighting
work is parked while that comparison method is designed. See
`2026-11-07-visual-comparison-tooling.md`. No additional lighting candidate was made.

## Sources and licences

No new dependency was introduced. The existing external
[SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT) checkout (`e25d1a5`, GPL-3.0)
provides the graphics-parameter, source parameter-definition and parameter-table readers and
writers. It remains a spike dependency, not a production language or licence decision.

The community's [GPARAM tutorial](https://soulsmodding.wikidot.com/tutorial:ds3-gparam-basics)
describes directional lighting and graphics-file organization for another host version. Its
licence was not stated on the retrieved page. It supplied background only: its angle convention
was not assumed to apply to this host, and no text, code, presets, values or illustrations were
incorporated. Format observations here come from read-only inspection of Riley's installs.

The [fromsoft-formats documentation](https://docs.rs/fromsoft-formats/latest/fromsoft_formats/)
provided the map light-bank filename convention, then verified against the local install.
Its licence was not verified during this inspection; no code or dependency was incorporated.
SoulsFormatsNEXT's existing region and light-bank readers supplied the structural inspection.
Broader community descriptions of probe grids and lightmaps were not treated as proof of
this host's runtime lighting behavior.

[DSMapStudio's region rendering](https://github.com/soulsmods/DSMapStudio/blob/master/src/StudioCore/Scene/RenderableProxy.cs)
and [region transforms](https://github.com/soulsmods/DSMapStudio/blob/master/src/StudioCore/MsbEditor/Entity.cs)
confirm that its box display extends upward from the region origin and uses the stored full
dimensions. Its licence is MIT. Only the coordinate convention informed the independent
coverage calculation; no implementation, bundled metadata or game values were incorporated.

The [fromsoft-formats 0.17.0 environment documentation](https://docs.rs/fromsoft-formats/0.17.0/fromsoft_formats/envmap/index.html)
and [path documentation](https://docs.rs/fromsoft-formats/0.17.0/fromsoft_formats/paths/fn.map_environment_bundle.html)
provided environment-package naming and member-family descriptions. This version declares
GPL-3.0-or-later; no library code, test fixtures, bundled metadata or dependency was incorporated.
Our own filename candidates were checked against the player's install and existing
SoulsFormatsNEXT readers parsed the resulting binders, texture packages and DDS headers.
