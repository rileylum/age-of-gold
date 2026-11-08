# Bonfire particle lifetime — 9 November 2026

The first source flame layer now has a visible native-host emitter at the derived bonfire
attachment. It is still dimmer and redder than the isolated source layer. This is a private
diagnostic build, not a completed flame or an ordinary playtest update.

Older source captures contain the other flame layers, but the low layer lies outside their
camera view. A fresh close capture supplies its draw and particle uploads. Texture-byte
matching identifies the draw independently of its event number. The upload agrees with the
source definition's size ranges, randomized starting atlas frames, rising motion and two
colour factors. The draw uses additive blending. Bounds reject the opposite sign for one
parent offset, but do not establish the exact distribution law or all parent semantics.

The native candidate adds the source-derived lifetime, emission schedule, disk, movement,
independent width and height variation, atlas variation and colour curves to the earlier
loading control. It uses the actual attachment without the diagnostic vertical lift.
Independent effect, texture and map readback passes; repeated builds are identical. Every
source texture mip is preserved. Only the effect bundle and placement map change from the
preceding illumination build.

The first candidate remains invisible. GPU inspection finds twelve particles with plausible
geometry and atlas values, but every packed opacity value is zero. The writer incorrectly
put the fade curve in a host colour field evaluated at emission time. The source fade is
evaluated over each particle's lifetime. This failure is retained as a regression witness.

The correction keeps the old colour factor constant and puts continuous opacity beside the
unrestricted colour factor in the host's particle-age field. It avoids reducing the fade to
the packed colour channel's precision. Generic readback verifies that only the three colour
properties change; emitter, transform and placement remain identical. A second pair of
builds is byte-for-byte identical.

The corrected capture contains eleven particles. The same GPU contract now passes size
ranges, atlas variation, radial bounds, combined colour and opacity. Maximum opacity error
is below 0.00006 and combined colour error below 0.0001. These are upload checks, not a claim
that the final pixels match. Orange flames are visibly present around the logs, while the
source layer remains brighter and more yellow. Exact sampling, emission cadence, start
phase and shading are still unverified.

The source session and both host sessions exit through normal menus. The host camera profile
is restored after each run. Existing illumination-runtime checks pass over 4,592 draws; its
only source change is a fresh trace destination. The established camera and player-anchor
checks pass. Exact-equality diagnostics still record tiny collision settling separately.

Two failed exporter attempts use incorrect RenderDoc field names. An initial readback check
also omits the helper's coordinate encoding and names the wrong atlas property slot. Those
failures are retained; correcting them does not change the effect. The later opacity failure
is a separate implementation error confirmed by live GPU data.

Next is the flame's shading response, followed by the other appearance layers and lifecycle.
The golden host resting-point effects remain visible. Full Firelink fidelity, the world
audit, NPC fidelity and navmesh/enemy stretch remain incomplete.

All game-derived files, programs, builds and captures remain outside the repository. Private
readback reuses SoulsFormatsNEXT under its previously recorded GPL-3.0 licence. Native
writing reuses [EvenTorset's FXR library](https://github.com/EvenTorset/fxr) under the
[Unlicense](https://github.com/EvenTorset/fxr/blob/main/LICENSE); its public colour-field
documentation distinguishes emission time from particle age. RenderDoc remains a private
MIT-licensed diagnostic tool. No production dependency or licence decision is made here.
