# Source renderer settings — 9 November 2026

The source lighting tables are not shader-ready colours. Read-only inspection of the installed
renderer and comparison with an earlier source GPU capture established that colour and intensity
are combined before a gamma power curve. Hemisphere light has a further multiplier applied after
that conversion. The environment coefficient does not use that hemisphere multiplier.

The executable includes named setting records with a padded UTF-16 identifier, scalar kind and
relocated pointer to initialized storage. This permits reading hemisphere and normal strengths
without an absolute address, executable signature or captured constant array. The maintained
reader supports the inspected executable revision and rejects missing or duplicate identifiers,
wrong types, malformed records, invalid storage, nonfinite values and negative strengths.

Authored executable checks change values, relocate the image and move the records. Authored
arithmetic checks use independent decimal expectations, including HDR intensities and disabled
hemisphere lighting. The installed settings and all 128 scenery/character ambient rows serialize
identically on Linux and Windows. Four named ambient coefficients and normal strength agree with
the private source capture; ambient comparisons allow relative floating-point error of one part
per million, with the same absolute allowance below one. Both installs remain unchanged.

This establishes parameter derivation, not a complete pixel translation. The environment sampling
gain, material features, directional and local lighting, fog and host shader integration still
need their own mapping and verification. Type metadata and instruction inspection also located
environment-parameter construction, but that exploration has not become a maintained reader.

Sources and licences: the player's source executable, parameter definitions, lighting tables
and shaders are proprietary local research inputs and are not redistributed. The source GPU
capture is private comparison evidence, never a construction input. The approved LLVM-MinGW
disassembler was used only for inspection; its existing external-tool notices apply. No
executable instructions, addresses, scalar values, shader fragments or captured output are
included in this record. No additional third-party dependency was introduced.
