# Area titles and retained native effects — 7 November 2026

This snapshot concerns the local Firelink spike, not a production format decision.

## Area title

The host still announced its native area after translated geometry had replaced it.
The spike reads the source layout's area-name reference and resolves its text from the
source install. It reads the host map's explicit announcement reference and replaces
that entry in the effective host message table. The event bytes remain unchanged, so
other users of the shared reference resolve to the same translated name.

The installed host edition carries the main place-name table again inside its latest
expansion message bundle. Overriding only the older main bundle did not change the
runtime title. Replacing the selected entry in the effective bundle did: a private
background capture showed the source area name on entry. This observation establishes
the title for the tested English install, not localization or other host editions.

The authored seam fixture first failed when a new text reference was allocated instead
of preserving the shared reference. It passes after the correction. Install readback
checks the resolved source text, every unrelated message member and the remaining
build files. No game text, identifiers, bindings or captures are committed here.

## Native effects remain unresolved

Disabling retained native placements removes their geometry, but does not establish
that all native world systems are isolated. Removing the native map's effect and wind
effect regions still left gold particles in runtime captures. Separately, removing
weather-table effect references did not remove them, and a delayed forced-wind reset
also failed. Those broad diagnostic edits are not accepted fixes. The next investigation
must identify the effect actually being rendered before choosing a lasting change.

## Dependencies and limits

The external inspector uses SoulsFormatsNEXT (GPL-3.0); the repository module uses its
existing spike reference. Private weather diagnosis also used Soulstruct's external
Paramdex metadata (GPL-3.0-or-later). Neither dependency nor game-specific metadata is
shipped or committed. Format-library and production licensing decisions remain open.
The only build carrying these experiments is private; the user's accepted playtest
profile and save remain unchanged.
