# Bonfire glow translation — 9 November 2026

The private candidate now contains four flame appearances and three persistent glow
appearances. Independent readback and repeated complete builds pass. This establishes a
build checkpoint; the new glows have not been verified in a running host game or promoted
to the ordinary playtest.

Each glow uses one persistent native particle. Its dimensions, placement, colour, startup
opacity and repeating brightness come from the source definition. Brightness varies
independently of opacity. The writer preserves the four preceding flame subtrees exactly
and retains all mip bytes of the source glow texture. The first field checker assumed the
loop flag was part of the interpolation name; the corrected reader checks those properties
separately. Both reports remain private.

The translation uses the native soft-particle path to obtain scene depth. A scoped marker
selects the glow response in the domain shader and both soft-particle pixel variants. The
glow projects the front of its depth volume and applies the source depth fraction, colour
response and distance opacity. Existing haze and unrelated effect responses remain behind
their preceding selection conditions. The texture uses the existing clamp sampler. No new
shader resource binding is introduced. These choices still need live binding verification.

An interpreter evaluates the actual post-assembly domain instructions with captured source
particle inputs. Expected front depth, thickness, projected depth and opacity come from
the source GPU outputs. A flat-depth control fails; the compiled volume translation agrees
within floating-point tolerance for all three appearances. Constructed unrelated markers
retain their fallback outputs. This check exercises the compiled arithmetic, not the host's
native particle upload. The captured frame does not cross the near plane, so the new glow
near-camera transition remains unproven.

All three shader binaries compile identically on repetition. Complete builds also match,
and the large effect bundle reconstructs exactly from its immutable parent, the new native
effect and the recorded writer. The private archive preserves the remaining files and
links; reconstruction avoids storing another full bundle. Earlier checkpoints are unchanged.

No game was launched. The save-reset preflight found insufficient space to stage the whole
isolated save beside its destination before atomic replacement. The test therefore stopped
before changing the save or selected profile. Capture output can use shared memory, but
that does not resolve save staging or persistent checkpoint capacity. The prepared session
checks this requirement before launch.

Live marker routing, native geometry, pixel sampling, startup, repeating brightness and
near-camera behaviour remain pending. The shader response is still tied to the captured
source daylight state; exact attachment, emission and random behaviour retain their earlier
limits. Sparks, the scrolling appearance and host resting-point effects remain unfinished.

The private tools reuse the licences recorded in [the preceding research](2026-11-09-bonfire-secondary-layers.md).
NumPy is BSD-3-Clause and is used only by the private checks. No new production dependency
or licence decision is made. Game data, shader listings, compiled binaries and screenshots
remain outside the repository.
