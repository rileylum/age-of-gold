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

The environment reader now follows relative code references to named settings. It recognizes
the ordinary environment path's material-mode/flag gate and traces scalar loads and lane-unpack
operations into the vector passed to the renderer. The mode and gain are read from that path,
without fixed addresses or an instruction-byte signature. Analysis accepts one straight-line
pack and rejects unclassified operations, invalid branches, argument clobbers, ambiguous paths
and invalid gains. It reuses the already-approved native disassembler.

Authored instruction fixtures vary the mode, gain, image base and unrelated argument loads.
Invalid control flow and duplicate paths fail. The installed result repeats exactly, agrees
with the source capture and is identical under the Linux and Windows disassembler distributions.
The complete authored suite and the shared host-binding checks pass. This establishes parameter
derivation, not a complete pixel translation: material features, directional and local lighting,
fog and host shader integration remain unfinished.

Sources and licences: the player's source executable, parameter definitions, lighting tables
and shaders are proprietary local research inputs and are not redistributed. The source GPU
capture is private comparison evidence, never a construction input. The approved LLVM-MinGW
disassembler was used only for inspection; its existing external-tool notices apply. No
executable instructions, addresses, scalar values, shader fragments or captured output are
included in this record. No additional third-party dependency was introduced.
