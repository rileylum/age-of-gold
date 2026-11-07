# Firelink resting-point spike — 8 November 2026

The private build now supports activation, resting, reload and death/respawn at the
source bonfire's placement. This is a functional checkpoint, not a finished bonfire.

## Registration and respawn

A host resting point has a visible marker, an interaction controller and a separate
respawn region. Event registration connects these through related identities. Moving
the first two without the region allowed resting but returned the player outside the
translated area after death. The private death test exposed that failure.

`FirelinkBonfire` now places all three together. Its caller supplies a template read
from the host install, the translated source placement and reserved host identities.
The module preserves the template's relative identity relationships, rotates spatial
offsets and facing with the new placement, registers the activation flag, and checks
the serialized result. Unrelated map and event content must restore exactly after
the additions are removed. Existing identities and activation flags are rejected.

The authored fixture failed before implementation because all three placements were
missing. Separate checks exposed loss of the registration's layer selection and reuse
of an activation flag before those were corrected. The fixture checks a quarter-turn
using hand-calculated positions, registration arguments, unrelated bytes and repeatability.
The installed host uses a longer registration record than the first authored fixture;
the install check rejected it, and the fixture and explicit layout check were corrected.

Run the authored check after publishing the existing probe:

```sh
WINEPREFIX=/tmp/aog-scale-wine WINEDEBUG=-all \
  wine spikes/er-collision/probe/bin/publish/Probe.exe unused firelink-bonfirecheck
```

The external reader supplies the real template and source placement. Two applications
of the module produce identical map and event bytes. Its private output also passed
the in-game rest-menu check and exited normally. The earlier corrected private build
passed death, return to solid ground, currency recovery and resting after respawn.
That death run's capture loop hit temporary-storage quota; the same running session
was recovered and checked afterward. It was not an uninterrupted capture-suite pass.

## Appearance and remaining work

The source sword and ash geometry are visible. The source ash perimeter uses vertex
opacity and needs a blended host material; preserving opacity with a cutout material
alone left a hard polygon boundary. The private blended-material trial removes that
boundary in two close views.

The source fire effect is not translated. The host's golden resting effect and host
interaction wording remain. The private resting table exposes only the translated
resting point; full menu behavior and travel still need checks. The module deliberately
does not choose game bindings, read message text, translate effects or edit resting tables.
The accepted user playtest profile and its save have not changed.

## Dependencies

The existing external SoulsFormatsNEXT checkout is GPL-3.0. Private parameter and event
inspection also used Soulstruct's GPL-3.0-or-later metadata. These remain spike tools;
no community implementation, game data, bindings, text or captures enter this repository.
No new production dependency or licensing decision is made by this checkpoint.
