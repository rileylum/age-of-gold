# Bonfire light data and timing — 9 November 2026

The preceding private host build uses a measured bonfire light at one phase.
This investigation identifies the corresponding effect in the source install
and establishes its colour conversion. It does not yet replace that measured
phase with an install-derived animation in the host.

A reader inspects the source object, its attachment records, its map placement,
and candidate shared effects. The object bundle contains no animation-event
file. Public effect documentation supplies a candidate family and identifies
the illumination action; a controlled source run establishes which member
actually supplies the observed bonfire light.

An authored private observer records the source's light-buffer uploads before
they are unmapped. It reads the data without changing it and uses an exclusive
trace destination. This yields time series without a separate GPU capture for
every frame. A separate control redirects reads of one shared-effect bundle to
a private diagnostic bundle. Both installs remain unchanged. Each control is
read back and checked: exactly one effect member changes, and every other
member remains byte-identical.

Five successful background sessions yield 5,760 updates. They cover the
unchanged effect, an authored constant colour, a linear colour control, paired
native and linear lights, and a clock embedded in the native light. All exit
through the normal interface. An earlier capture fails because its status
filename collides with screenshot metadata; that evidence and its normal exit
are retained too.

The constant-colour prediction fails against the unchanged source, then passes
all 960 controlled updates. Its maximum absolute channel error is below seven
ten-millionths, at the fixed absolute-plus-relative tolerance. The player light
and bonfire position remain unchanged. This causally validates the selected
effect and the colour conversion for this action.

The library's interpretation of the changing colour groups the values
incorrectly. Reading the raw value block as contiguous colour tuples followed
by two auxiliary planes agrees with the control and the unchanged light's
extrema. Treating each key as an interleaved triple does not. The auxiliary
planes' general meaning remains unresolved; this experiment is evidence about
the inspected effect, not a complete reader for every curve variant.

The paired control preserves the native light branch and adds a distinguishable
linear sibling. Their positions differ by a small constant amount, rejecting
the initial assumption that identical branches imply identical attachment.
The native position itself stays unchanged. This could arise from emitter or
attachment state; it is not yet classified as random placement.

A further control changes only the native light's radius to an authored linear
curve. Colour and attachment remain unchanged. The radius supplies a clock on
the same emitter, avoiding an assumption about sibling scheduling. Its 960
updates agree with the earlier paired colour-versus-phase observations to
within seven ten-millionths, below the fixed two-millionths limit.

Linear, smoothstep and cosine predictions do not reproduce the changing source
colour. Empirical cubic and parametric fits also leave measurable residuals.
None is accepted as a translation. Related public tooling describes its own
curve evaluation as approximate, so it cannot supply an exact oracle either.
The next step is to classify the source interpolation and auxiliary fields,
then derive attachment independently. Wall-clock period measured in this
background setup is not yet an established simulation-time rule.

Failed controls remain preserved: the initial capture collision, the sibling
position assumption, a confinement check that compared relocated serialization
identities, and a clock selector that accidentally included reciprocal radius
in its position comparison. The corrected checks compare graph references by
identity and position coordinates separately. No numerical acceptance limit was
relaxed to pass them.

Sources consulted:

- Souls Modding's [FFX explanation](https://soulsmodding.com/doku.php?id=tutorial:ffx-files-for-sfx-explained)
  and [FXR explanation](https://soulsmodding.com/doku.php?id=tutorial:fxr-files-for-sfx-explained).
  The current wiki declares CC BY-SA 4.0. Used for effect organization and
  terminology; no implementation copied.
- JeNoVaViRuS's [main-action reference](https://docs.google.com/spreadsheets/d/1lt5l3NwPsL_HEHjd2WMCwvKejVABpuhWqOW9lyU-p44/edit)
  and [secondary-action reference](https://docs.google.com/spreadsheets/d/1OxvRUzu4eDoMzD6G26OtFnIyicYXyeJKE6BDi7hwj5Y/edit).
  No separate licence found. The main-action reference supplies an illumination
  classification; its numerical interpretation is checked against the install.
- The [FXMLR release](https://github.com/JeNoVaViRuS/FXMLR_upload/releases/tag/FXMLR).
  No licence found in the inspected release payload. Only its template was
  inspected privately; no executable was run and no implementation copied.
- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT), GPL-3.0,
  already used by the external research tools. Its reader/writer is linked
  privately; the value-layout finding above limits what its XML proves.
- [EvenTorset's FXR library](https://github.com/EvenTorset/fxr), Unlicense.
  Its related-game curve evaluator explicitly uses an approximation. Inspected
  privately as research; no implementation copied or executed.
- The existing private [MinHook](https://github.com/TsudaKageyu/minhook) build,
  BSD 2-Clause. Used to install the authored observation hooks.

All controls, extracted data, third-party material and captures remain outside
the repository. No host build or ordinary-play profile changes in this
checkpoint. This research makes no production language or dependency decision.
