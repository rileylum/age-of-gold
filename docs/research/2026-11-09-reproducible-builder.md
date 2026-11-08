# Maintained builder dependency checkpoint — 9 November 2026

Riley selected C#/.NET, SoulsFormatsNEXT, HKLib and GPL-3.0 for the maintained builder, then
approved external DXC as a separately installed build tool. These decisions permit their
use under their licences; they do not permit shipping game data or frozen research outputs.

The active SoulsFormatsNEXT revision is `e25d1a5c3cda83b801fdefe00f4d2903892cc526`.
Its licence is [GPL version 3](https://github.com/soulsmods/SoulsFormatsNEXT/blob/e25d1a5c3cda83b801fdefe00f4d2903892cc526/LICENSE).
The maintained project builds an explicit subset for binders, models, maps, material
definitions and compression. It does not compile the bundled regulation/save key helpers,
load game-specific resource files or use console texture conversion. The ordinary PC texture
container reader is authored here; its DDS contents are already usable without deswizzling.

The original research checkout included an Oodle adapter modification: the older compression
library supplied by the host lacks an optional logging hook. The bootstrap now makes that
adjustment explicitly after retrieving the pinned source. The source tree and its complete
post-adjustment fingerprint manifest remain external.

The subset's remaining NuGet dependency is ZstdNet 1.4.5. Its package contains BSD three-clause
notices for ZstdNet and the included Zstandard library; the lock file records its package
digest. No compression library from either game is included. Sources:
[ZstdNet](https://github.com/skbkontur/ZstdNet),
[Zstandard licence](https://github.com/facebook/zstd/blob/v1.4.5/LICENSE).

HKLib is [MIT-licensed](https://github.com/The12thAvenger/HKLib/blob/ac40bc9915d8caff0df154c2b9a79656dfca1d77/LICENSE).
Its existing default serializer loads a bundled reflection registry. The maintained dependency
adapter instead starts with an empty registry and builds descriptions and hashes from each
input. Its projects exclude all bundled resources. Authored layouts with deliberately changed
offsets prove that the input controls field placement. Both installed host collision templates
read and round-trip, and the existing synthetic-mesh checks pass with this metadata, including
large section trees. The generated objects still use the library's format classes.

The source collision adapter reads the older tagfile container and item references itself and
reuses HKLib's type-description parser. Older type-table section names map to the newer names;
the data and offsets remain unchanged. This aliasing also appears in the public
[Soulstruct-Havok reader](https://github.com/Grimrukh/soulstruct-havok/blob/bf2d41fc83de4a43bd3ed3df8605741f143cad2c/src/soulstruct/havok/tagfile/unpacker.py)
(GPL-3.0-or-later); no code was copied from it. The maintained reader uses no generated 2015
classes or Python runtime. Every source collision part matches the preceding private reader,
including mesh arrays, materials, body/submesh transforms, extrusion and winding flags.

DXC release `v1.9.2609` reports compiler version `1.9.2609.5` locally. The previous research
verified the downloaded Linux archive against its published digest. Its source licence and
component-specific distribution terms are described in the earlier
[shader checkpoint](2026-11-08-surface-tangents.md). Neither the compiler nor its SDK is
vendored or included in our releases. The authored helper uses its public assembly,
container and validation interfaces. Its fixture establishes deterministic compilation and
reassembly, retaining root-signature, input/output-signature and pipeline-validation parts.
Debug resource labels need not survive reassembly; the checked GPU binding contract does.

Riley subsequently approved executable-derived regulation-key discovery. A private scan proved
that read-only PE data contains a candidate that opens the entire installed archive. The
maintained adapter validates the first decrypted block, then the complete binder, and requires
one distinct valid key. Its current ceiling is a 16-byte-aligned 256-bit key blob; another
executable layout must fail and be investigated. No key or address is embedded. Authored
fixtures cover missing keys, excluded code sections, duplicate locations and ambiguous
validation. Real-install checks prove independent deterministic encryption and readback with
all table contents retained. This resolves key recovery, not the separate host-schema question.

Review of the prepared outputs exposed an archive-reader error: an omitted unpadded length was
treated as an empty file. The stored extent is still valid in that case. An authored fixture
now covers it, and a separate install check parses the prepared host map, all material
definitions and model geometry. Repeatability and unchanged-input hashes alone had missed the
error; the earlier reports are insufficient evidence for extraction.

Comparison with the accepted playtest found two changed gameplay tables: bonfire destinations
and interaction prompts. A private experiment can preserve both tables using opaque row data
and reproduce the accepted edits. It obtains byte differences from the accepted output, so it
proves container preservation only, not install-derived semantic translation. The proposed
maintained approach is to author descriptions of only the fields that need editing, preserve
all other row bytes, and reject unsupported layouts. Riley approved that approach for these
two tables. The implemented adapter has authored preservation, revision, ambiguity and identity
checks. Real-install checks edit both tables independently twice, retain a source bonfire's
translated position through encrypted readback, and preserve the other tables and install inputs.

A separate private comparison uses external field descriptions from the existing Soulstruct
research checkout (package licence GPL-3.0-or-later) as an oracle. Both edits match its output
byte for byte. It reads both installs directly and does not use the accepted build. Neither
those descriptions nor Soulstruct is a maintained build dependency. The format library's row
clone shares array cell values; the adapter explicitly duplicates the opaque row payload before
editing so the template cannot be changed accidentally.

Riley also approved MinHook for the maintained native lighting runtime. The pinned revision
is the one used by the accepted private playtest; it reports version 1.3.3. A clean source
bootstrap and authored native probe demonstrate hook installation, calling the target's prior
implementation, disabling and removal. Two fresh builds match byte for byte under the existing
private Windows cross compiler. No game was launched. The generated check directory includes
the complete upstream notices. MinHook and its included HDE components use the two-clause BSD
terms in the [pinned upstream licence](https://raw.githubusercontent.com/TsudaKageyu/minhook/05c06c5bbca226b72ffb40fc0caaef33bcaf6f74/LICENSE.txt).
The private runtime still contains captured shader/root data and executable binding details;
adopting the hook library does not make that runtime suitable for the maintained source tree.

Riley selected the existing external LLVM-MinGW 20261104 toolchain for maintained native
builds. Its Clang version and source revision are checked at configuration, and the upstream
Linux/Windows package checksums are recorded. The retained Linux archive matches the upstream
digest. An authored unsupported compiler is rejected; two fresh accepted builds and their
licence staging pass. The distributed LLVM licence is Apache-2.0 with LLVM exceptions;
mingw-w64 carries ZPL-2.1 and component-specific terms recorded in its bundled notices. Those
notices are retained beside generated binaries; the compiler itself is not redistributed.
The [pinned release](https://github.com/mstorsjo/llvm-mingw/releases/tag/20261104) and
[upstream build documentation](https://github.com/mstorsjo/llvm-mingw/blob/20261104/README.md)
describe the Linux cross compiler and Windows-native packages. Native Windows execution has
not been verified in this session.

Primary DXC sources: [release](https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.9.2609),
[licences](https://github.com/microsoft/DirectXShaderCompiler/blob/v1.9.2609/LICENSE.TXT),
[public interfaces](https://github.com/microsoft/DirectXShaderCompiler/blob/v1.9.2609/include/dxc/dxcapi.h).
