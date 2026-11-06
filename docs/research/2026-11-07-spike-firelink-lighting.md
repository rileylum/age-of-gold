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
`AgeOfGoldSpike.sl2` save. It is ready for a play check after the preceding game exits. The
expected observation is light falling on the opposite side of the chapel, with shadows cast
the other way. That outcome is not yet proven in play. Brightness remains unchanged.

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
