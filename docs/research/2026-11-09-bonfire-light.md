# Bonfire-associated local illumination — 9 November 2026

The previous positive source sample receives its local illumination from the
bonfire-associated light. The player-associated light cannot reach that surface.
A private host control now adds both contributions to the classified scenery,
using the source response already checked independently. The bonfire placement is
read again from the source install. Its light attachment offset and intensity are
measured from a source capture. This reproduces one captured phase; it does not
yet derive the attachment or flicker from the source effect definition.

The added parameters extend our root constants without changing the native
descriptor tables. The old shader fails the buffer-size check; the new shader
passes. A separate deserializer verifies the new constant count and preservation
of every native table. DXC's root-signature compatibility check unexpectedly
accepts the shorter constant allocation, so that check alone is insufficient.
The failed preparation attempts remain preserved. Two builds match byte for byte;
only the composite shader archive and its own build metadata differ from the
preceding build.

One background host frame supplies the actual descriptors, light parameters,
camera, depth and material inputs. Exact GPU probes recover the sampled colour
and summed local response. Independent arithmetic agrees across 2,068,630
classified pixels, including 399,327 nonzero responses, at the previous tolerance.
The largest absolute difference is below two ten-millionths. This proves the
captured response, not matching surface sampling between the two games.

Removing all local illumination changes 256,043 composed pixels. Removing only
the bonfire contribution changes 226,291. Neither control changes pixels outside
the classified scenery. Independent depth reconstruction puts every pixel
changed by bonfire removal inside that light's radius. Neighboring draws retain
the host's descriptor heap and declared root layout after our draw completes.
These are same-frame controls, avoiding differences from animation or exposure.

The first runtime derives the physics translation by subtracting the player's
map position from its physics position. That fails during relocation: the two
positions can update at different times, moving an otherwise fixed light.
The trace retains forty-eight frames outside both the initial and rebased
coordinate origins. A later control reads the physics origin directly. Its first
interpretation uses the wrong sign and remains a failed diagnostic. Descriptor
lifetime checks continue to apply unchanged.

The corrected direct reader passes a fresh session: every active frame agrees
with either the initial or rebased origin, and every frame after the observed
world load keeps the bonfire fixed through relocation and player movement. The
camera stays fixed during the movement check, while the player light follows
the player. Shader bytes and root data are unchanged from the captured frame.
Sixteen further screenshots cover eight viewing directions, with no gross
scenery or binding regression against the preceding host control. All three
sessions exit through the normal interface and restore the private profile.

This does not establish arbitrary streaming or world-reload behavior. Source
flicker, the measured attachment offsets, other local lights and source/host
surface sampling remain open. The corrected adapter is a private development
checkpoint; its fixed trace destination must also change before ordinary play.

Sources consulted:

- Microsoft's [root constants documentation](https://learn.microsoft.com/en-us/windows/win32/direct3d12/using-constants-directly-in-the-root-signature)
  and [root-signature construction documentation](https://learn.microsoft.com/en-us/windows/win32/direct3d12/creating-a-root-signature).
  Documentation licence: CC BY 4.0, as declared by MicrosoftDocs/win32. No sample
  code was copied.
- A [Grand Archives discussion of physics origins](https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/issues/41)
  supplies a location to investigate, not proof of its current interpretation.
  The repository declares no licence through its repository metadata. No code
  was copied; the location is used only by a separately authored private reader.
- The [eldenring crate's character-physics documentation](https://docs.rs/eldenring/0.14.0/eldenring/cs/struct.CSChrPhysicsModule.html)
  was consulted for terminology. Version 0.14.0 declares MIT OR Apache-2.0.
  No implementation was copied or linked.

All runtime controls, captures, shaders, extracted data and third-party material
remain outside the repository. This is research, not a production dependency or
licence decision. The ordinary playtest remains on its existing build.
