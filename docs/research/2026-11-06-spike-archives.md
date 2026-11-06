# Spike: opening Elden Ring's archives from the player's install alone

A snapshot as of 6 November 2026, from `spikes/er-collision/probe` run against Riley's install
(Steam build 25827938). Not a spec.

## Proven

- The five archive headers (`Data0`–`Data3`, `DLC`) are RSA-encrypted. The public keys that open
  them are in the player's `eldenring.exe` as five PEM blocks (PKCS#1 `RSA PUBLIC KEY`), in plain
  text, in archive order. The tool can find them by scanning for the PEM markers: no byte
  signatures and no keys in our code. Key N opens archive N.
- Decrypting is textbook RSA with the public key, block by block. Each 256-byte cipher block gives
  a 255-byte plain block.
- The decrypted header is BHD5 with 32-bit counts and offsets: bucket count at 0x10, bucket table
  offset at 0x14, then the salt. Each bucket is a file count and an offset. Each file entry is
  40 bytes: 64-bit name hash, padded size, unpadded size, data offset, SHA offset, AES key offset.
  Where present, the AES key is 16 bytes followed by a count of byte ranges to decrypt with
  AES-128-ECB.
- File names are a 64-bit polynomial hash, multiplier 0x85, of the lower-case path with a leading
  `/`. Paths our own code names are enough to find files; no dictionary is needed.
- The five archives hold about 120,000 files: 5,878, 39,419, 39,684, 1,672 and 33,755.
- A legacy dungeon's files live under `map/mXX/mXX_YY_ZZ_WW/`. For the Stranded Graveyard,
  `map/m18/m18_00_00_00/` holds the high- and low-detail collision as split binders
  (`h18_00_00_00.hkxbhd`/`.hkxbdt`, BHF4/BDF4, not compressed on the outside), the navmesh
  (`.nvmhktbnd.dcx`), `.nva.dcx`, map pieces (`_000000.mapbnd.dcx`) and lights (`_0000.btl.dcx`).
  Map layouts are at `map/mapstudio/mXX_YY_ZZ_WW.msb.dcx`.

## Not yet proven

- Decompressing DCX files. Elden Ring uses Oodle Kraken, and the player's install has only the
  Windows library, `oo2core_6_win64.dll`. A native Linux process cannot load it. Options: run the
  tool, or a helper process for Oodle, under Wine/Proton; or find a licensed Linux Oodle.
- Reading binders (BND4, BXF4) and the collision itself.
