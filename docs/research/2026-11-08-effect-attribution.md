# Retained world effect attribution — 8 November 2026

This is a local spike result. It does not settle the production format library or licence.

The drifting gold leaves persisted after disabling native map placements and map effect
regions. Weather-table, wind-reset and broad map-effect diagnostics also failed. A private
GPU capture identified the texture sampled by the visible leaf draw. Its compressed mip
matched a texture read from the installed host's common effect bundle. Candidate searches
then reduced the investigation to four effect definitions that referenced that texture.

Disabling those four definitions removed the particles. Separate single-definition tests
rejected the first candidate and confirmed the second in two matched camera views, each
captured twice. The other definitions can remain intact. This establishes the cause of the
observed leaves; it does not establish complete isolation of every native world system.

`FirelinkEffects.Suppress` accepts serialized bundle bytes and an explicit selection. It
removes emission from the selected definitions while retaining their identities and state.
The definition remains loadable, so callers can still request it. Other effect definitions,
textures and bundle members remain unchanged. The selection is private diagnostic metadata,
not a hardcoded game identifier in repository code.

The authored serialized fixture failed before suppression was implemented, then passed.
A second failing check required absent selections to reject the build. Readback verifies
empty emission, preserved state and references, exact unrelated bundle bytes and repeatability.
The private install adapter calls the repository module; two complete builds match all 351
files. The module retains the root container's identity, unlike the earlier attribution trial,
so its runtime output was checked independently. Eight compass views were captured twice;
the inspected chapel and cliff views show no drifting leaves. The game exited normally.
A temporary-storage quota interrupted the first capture run. Older captures were preserved
with verified hashes, then a complete fresh capture set was collected from the same live
session after rechecking the player anchor, camera, time and weather.

The checked seam can be run after publishing the existing probe with
`Probe.exe <host-install> firelink-effectscheck`. The fixture is authored from scratch and
does not read either install. Actual build and play checks remain private and manual.

Sources and licences: [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT), GPL-3.0,
is the existing external format-library dependency. [RenderDoc](https://github.com/baldurk/renderdoc),
MIT, was used privately for GPU attribution. No implementation from either tool, game
identifiers, shader disassembly, generated bundles or captures are included here. The source
and host installs remained read-only; game tests ran offline with isolated saves.
