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

Primary DXC sources: [release](https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.9.2609),
[licences](https://github.com/microsoft/DirectXShaderCompiler/blob/v1.9.2609/LICENSE.TXT),
[public interfaces](https://github.com/microsoft/DirectXShaderCompiler/blob/v1.9.2609/include/dxc/dxcapi.h).
