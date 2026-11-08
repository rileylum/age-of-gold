# Bonfire attachment controls — 9 November 2026

The source object's attachment point can now be derived from its installed model
and map placement. Five live source controls distinguish that attachment from the
effect's additional displacement. This is a source research checkpoint: the host
still uses the preceding measured light position, and the flame is not translated.

## Attachment and displacement

A private reader applies the attachment's parent-node scale and translation, then
the map placement's rotation and translation. It rejects transform variants that
these controls have not exercised. Two fresh reads produce identical output.

The controls alter one setting at a time, using redirected reads of private
bundles. Neither install is written to. The checks read every bundle back, compare
all other members, and check confinement within the changed effect or model.

- Removing the light's local displacement exposes the attachment point. The
  calculated position matches the running source game within one millionth of a
  world unit.
- Doubling that displacement doubles its world-space vector about the attachment.
- An authored change to the model's attachment point produces the predicted
  displacement after the bone and map transforms.
- Clearing a setting in the parent effect removes the sideways component, leaving
  the light directly above the attachment.
- Doubling that parent setting changes the direction while leaving the attachment
  itself unchanged.

The five sessions contain 1,200 observed light-buffer uploads. Positions remain
constant within each observation. The four quantitative placement checks pass at
the unchanged authored tolerance of one ten-thousandth of a world unit; the largest
error is below five millionths. Every source session exits through the normal UI.
The freshly read input fingerprints still match after the experiments.

The initial hypothesis—an attachment followed by a purely vertical offset—fails
against the unchanged source light. A diagnostic that doubles the observed polar
angle agrees with the doubled parent setting at the same tolerance. That diagnostic
still consumes a measured direction. It does not establish the parent effect's
sampling distribution, seed, or numerical implementation, and is not a replacement
translation. The small difference between previously observed sibling lights is
consistent with distinct emission directions, but this checkpoint does not prove
how those directions were chosen.

## Preserved limitations and failures

The first launcher retained an old Windows DLL path after its POSIX path had been
updated. The source loaded without the observer, and camera acknowledgement timed
out. It was closed normally; no experimental samples came from that run. The
corrected launchers use fresh destinations and validate both path forms.

A private static-inspection helper initially treated an entire executable section
as the exception directory, producing invalid function ranges. The failed report
and oversized disassemblies are retained, with the latter compressed. Reading the
actual directory corrects that scan. The resulting emitter-related reference has
not been tied to the tested parent action, so it supplies no implementation proof.
No new executable hook was used in these experiments.

The ordinary host preview, profile and save are unchanged. No host shader, runtime
or build was generated here. The next attachment step is to classify the parent
direction sampling without carrying a captured direction into the writer. The
verified object attachment also supplies the anchor needed for the flame work.
Firelink fidelity, world isolation, NPC fidelity and the enemy stretch remain open.

## Evidence and tools

Private evidence is retained under
`~/.local/share/age-of-gold/visual-compare/2026-11-09-bonfire-attachment`.
It includes the deterministic reader, bundle controls, observer source, traces,
screenshots, failed launch and inspection attempts, and the checkpoint report.
Game-derived data and executable inspection remain outside the repository.

The existing SoulsFormatsNEXT reader is GPL-3.0 and MinHook is BSD-2-Clause.
The existing LLVM compiler distribution has Apache-2.0 with LLVM exceptions and
its runtime component notices. These remain private development tools, as recorded
in the preceding effect and curve investigations. GNU objdump was used for private
read-only inspection; the installed binutils package records GPL-2.0-or-later,
GPL-3.0-or-later, LGPL variants, GFDL-1.3 and FSFAP component licences. No community
implementation or game-derived constant is added to the repository by this work.
Production language, library and project-licence decisions remain open.
