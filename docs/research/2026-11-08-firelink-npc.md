# Firelink NPC interaction spike — 8 November 2026

A private build places the Fire Keeper in her source cell and supplies a repeatable
initial response. This is a narrow functional checkpoint: services, quest state,
damage, voice and live animation are not implemented.

## Appearance

The private reader obtains the source character model, skeleton and a sampled idle
pose from the installed game. Weighted vertices are transformed from bind pose into
that pose and written as a static host map model. Character and shared body textures
come from the source install. Serialized checks cover geometry, opacity and texture
coordinates. Normal and specular maps are not translated; blended surfaces currently
use a cutout approximation.

The first source comparison showed an empty cell while the player remained upstairs.
Moving only the camera was insufficient. A bounded player relocation in the isolated
source test made the NPC visible, and position readback confirmed stable ground.
Matched cell views now confirm placement and the sampled seated pose. The host cell
is brighter and cooler than the source; this is not a fidelity pass.

## Interaction

The private build reads the source interaction placement, prompt and initial response
at build time. A separate invisible host controller uses a dedicated action row and
an authored response graph. Message entries are added to the effective host language
bundle. Removing the additions restores unrelated map, message and regulation data.

The graph first transitions into initialization, then waits for the action, opens
the response, waits for closure and returns to the action. In the tested setup,
commands attached directly to the entry state did not execute; a separate initialization
state did. An automatic response probe established script execution before testing the
ordinary action. Early trials also reused a controller instance identity and a binder
member index; both now follow unique/native conventions, but their individual runtime
effects were not isolated.

The fixed host debug camera consumes movement keys. Early facing tests therefore did
not turn the player, leaving the action unavailable. Normal controls established the
prompt. Later captures verified opening, closing, reopening, leaving interaction range
and returning. The bonfire retained its own prompt at its placement. Both host tests
and the source comparison exited through the normal UI. Two complete private builds
match byte for byte across 386 files.

## Tools and limits

The private prototype uses the existing SoulsFormatsNEXT checkout (GPL-3.0) and
Soulstruct/Havok tooling (GPL-3.0-or-later). Talk expression research uses
[ESDLang](https://github.com/thefifthmatt/ESDLang) at
`9fa7fa90849a662b36839f077e321a02f9e992f4`.
Its [licence file](https://github.com/thefifthmatt/ESDLang/blob/9fa7fa90849a662b36839f077e321a02f9e992f4/Script/LICENSE)
limits the Apache-2.0 declaration to the Script directory and identifies unlicensed
code in the separate ESDLang library. That library remains private research tooling
under Riley's authorization; no implementation or metadata is incorporated here.

The private adapters, bindings, poses, message text, generated files and captures stay
outside the repository. No production dependency or licence decision is made. The
ordinary playtest profile and the player's save are unchanged.
