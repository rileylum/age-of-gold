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

## Proven with SoulsFormatsNEXT and HKLib, under Wine

For speed, the spike reads binders, DCX and Havok with SoulsFormatsNEXT (GPL-3.0, commit
`e25d1a5`) and HKLib (MIT, commit `ac40bc9`), cloned outside the repository under
`~/dev/tmp/aog-spike-tools/`. It is built as a self-contained `win-x64` program and run with Wine
11.20. This settles nothing about decision 2.

- Wine's platform RSA import mangles the keys; parsing the PEM with .NET's managed ASN.1 reader
  works on both Linux and Wine.
- The player's `oo2core_6_win64.dll` loads under Wine and decompresses Kraken DCX. SoulsFormatsNEXT
  asks the library for `OodleCore_Plugins_SetPrintf`, which version 6 lacks; the local clone is
  patched to skip it.
- The Stranded Graveyard's high-detail collision binder holds 42 files: one
  `h18_00_00_00.compendium.dcx` (shared Havok type information, magic `TCM0`) and one
  `hkx.dcx` per collision part (Havok 2018 tagfiles, magic `TAG0`). The smallest part is 2.9 KB
  uncompressed and the largest about 3 MB.
- HKLib reads every part once the compendium is loaded. Each part is a physics scene:
  `hknpPhysicsSceneData` holding one `hknpPhysicsSystemData` with materials, motion properties and
  a body whose shape is FromSoftware's `fsnpCustomParamCompressedMeshShape`.

## What the tool must build

The shape is a Havok compressed mesh (`hknpCompressedMeshShape`) whose data,
`hknpCompressedMeshShapeData`, holds:

- a mesh tree: sections, each with a compressed bounding-box tree over its primitives
  (`Aabb5BytesCodec`), vertices quantised per section (`packedVertices`), vertices shared across
  sections (`sharedVertices`), and runs of primitive data;
- optionally a SIMD tree (`hkcdSimdTree`) over the sections;
- connectivity between triangles, and a bit field of interior triangles;

plus FromSoftware's `fsnpCustomMeshParameter`: per-triangle and per-primitive data, which
presumably carries the material (footstep sound, slipperiness) for each triangle.

Building these from a triangle mesh is the open question: a bounding-volume tree, quantisation and
FromSoftware's per-triangle data, all in shapes the game accepts. The smallest real part is the
reference to compare against, locally, without committing it.
