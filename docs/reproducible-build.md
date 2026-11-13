# Reproducible build — work in progress

The accepted private playtest remains the comparison reference. The maintained builder is
being assembled from readers that use the two installs directly. Restoring the private kit
does not count as a fresh build, and the new preparation command does not yet produce a
playable mod.

## Checkpoint — 14 November 2026

`build-firelink` now writes a loadable build from the two installs in one command. It needs the
pinned DXC, the assembler helper and LLVM-MinGW (`AOG_DXC`, `AOG_DXC_ASSEMBLER`, `AOG_LLVM_MINGW`).
The tool is one Windows program; on Linux it runs under Wine with Windows DXC, because the host's
compression library is a Windows library.

```sh
AgeOfGold.exe build-firelink --source SOURCE_INSTALL --host HOST_INSTALL\Game --output FRESH_DIRECTORY
```

The output holds `mod/`, a mod loader profile that keeps the game offline, and `manifest.json`.
What the build contains so far:

- The host map with every installed placement and map effect disabled, the player kept.
- Terrain collision for the 39 ordinary solid placements, always visible as in the accepted playtest.
- Scenery for 307 of the 330 source surfaces: opaque one- and two-layer surfaces, cutouts
  (alpha-tested and blended surfaces, cut at half coverage), foliage and ivy without sway, and
  the sky dome. All share the host's two-layer family; each layer keeps its own coordinates and
  frame. Colour-only surfaces use authored neutral normal and packed textures.
- A shader family and material per source lighting row, fog row, scattering row and diffuse
  multiplier. The G-buffer pixel program is the installed host program, rewritten by resource
  and signature meaning: it blends the layers by vertex alpha, emits the source's environment
  and hemisphere light, and applies the source's fog and light scattering (the published
  Hoffman–Preetham model with constants from the source's scattering table). Vertex variants
  of a family place their added outputs on the same registers and forward the camera vector
  the installed programs already compute.
- The host map's graphics parameters with vignette, chromatic aberration and volumetric fog
  off, environment lighting regions moved to the scenery, and the lens far focus range from
  the source depth-of-field row selected by the collision under the spawn.

`firelink-build-installs FRESH` builds twice, compares every file, reads the build back and
rechecks the consumed install inputs. `pixel-shader` checks the pixel rewrite numerically on the
CPU against independent arithmetic, using an authored evaluator for scalar shader programs.
A freshly built candidate loads in the host and renders the chapel and cliff views with source
lighting. That is one private background capture, not a play proof.

Not built yet: 23 surfaces in 8 families (cloud, mist, tree planes, light shafts, water,
lightmapped), the source display stage (exposure, bloom, tone correction), source local lights
and the light composition stage, the drifting host leaves, effects, the bonfire, characters,
text, events and tables. Scene baking still differs in the last float digit between native
Linux and Wine, because rotation uses the platform's sine and cosine.

## Checkpoint — 9 November 2026

Implementation is paused at Riley's request to conserve the weekly usage allowance. The
latest implementation is `6f54f5f` on `feat/reproducible-firelink`. The accepted playtest and
its saves are unchanged. There is no complete maintained install-to-playable-build command.

The authored suite passes. Source shader specialization also passes 26 GPU comparisons
covering 6,656 floating-point results, including eight cases with zero constant buffers
supplied. Repeated specialization produces identical bytes and retains no constant-buffer
loads. Native Windows specialization remains unverified. See the
[shader binding evidence](research/2026-11-09-shader-bindings.md) and
[renderer settings evidence](research/2026-11-09-renderer-settings.md).

The next recommended milestone is a freshly built Firelink subset with geometry, one ground
material and lighting, loaded in the host and compared with the accepted playtest. This
should establish the missing pixel-program integration before expanding material coverage.
Construction must use the installs; the accepted build remains comparison evidence only.
Success requires correct rendering and a repeated deterministic build, not compilation alone.

After that milestone, extend material/model coverage, integrate atmosphere and the lighting
runtime, connect map/event/text/table stages, and exercise the complete build and platform
checks before promotion. The detailed remaining stages below still apply.

Archive access, shader conversion, texture handling and comparison tools are broadly reusable.
Current material, collision and map adapters cover checked cases, including Firelink-specific
selection and placement assumptions. Other areas still need classification of new cases;
successful shader conversion does not establish that every material renders correctly.

Required temporary development material has moved to the HDD. Recovery instructions,
verified relocation inventories and the environment setup are under
`/mnt/storage/age-of-gold-private/2026-11-09-checkpoint-control/`. The private recovery README
explains how to restore temporary aliases after reboot and select the HDD-backed TMPDIR.
The repository handoff and an HDD snapshot identify the exact next steps and evidence paths.

## Implemented

- A C# command reads requested files from both installs, derives host archive keys from the
  host executable, decrypts requested entries and records the bytes it read. Conflicting
  archive entries fail; identical duplicates are checked rather than selected by accident.
- Regulation-key discovery searches the host executable's read-only data and validates the
  complete archive. Keys stay in memory. Missing or ambiguous discovery fails. Re-encryption
  is deterministic.
- Minimal authored layouts edit bonfire destinations and interaction prompts. Other bytes
  survive unchanged, and unsupported encodings or conflicting identities fail. This writer
  is checked separately; the complete map, event, text and table bindings still need integration.
- Firelink preparation reads placed source models and material definitions, including patch
  precedence, rigid transforms, triangle winding, vertex colours, tangent frames and all UV
  channels. It extracts the explicitly referenced DDS textures without altering their bytes.
  The same checked lookup handles loose area packages, including the diffuse environment
  textures referenced by the source lighting rows; ambiguous names fail.
- Preparation also reads the source renderer's named hemisphere and normal-strength settings
  from its executable, with version, type and pointer checks. No address or captured value is
  embedded. Ambient parameter translation combines colour and intensity before gamma conversion,
  then applies hemisphere strength separately. Host pixel-program integration remains unfinished.
- Environment sampling gain and its material mode come from bounded analysis of the source
  renderer's parameter construction. Named setting references locate the code; its branch and
  scalar-vector operations determine the values. Preparation records them in `environment.json`.
  Missing, ambiguous or unsupported paths fail instead of using a captured constant.
- Source collision reading uses serialized type descriptions, validates references and extracts
  both sets of triangle meshes, their materials, transforms, placements and roles. It does not
  need the earlier Python reader or bundled game type descriptions.
- Host collision serialization reads its metadata and type hashes from the installed
  compendium. It runs with HKLib's resource directory excluded. Both template round trips and
  generated meshes pass readback, including section and vertex-slot boundaries.
- Terrain assembly connects that reader and writer to host map placements. It rebuilds the
  accepted ordinary-solid subset in both detail sets, retains source visibility groups and
  places the player above supported ground. Special and disabled placements are reported
  separately. Existing collision physics and unrelated map fields are preserved.
- PC surface texture packaging preserves compressed mip payloads and derives container
  settings from a host template. Sampling is explicitly linear or sRGB; BC5 rejects sRGB.
  BC1, BC5 and BC7 are checked for surfaces. BC6H unsigned HDR cubemaps retain six complete
  face/mip chains and derive the host container format from an installed cube template.
  Other formats, incomplete cubes, arrays and volumes currently fail.
- Two-layer ground geometry retains both texture mappings, signed tangent frames, derived
  bitangents and vertex colour/alpha through subdivision and host model readback. Secondary
  coordinates and colour use floating-point storage; primary coordinates retain the host's
  packed format. Visibility bounds cover every written vertex.
- Ground material binding retains both colour, normal and packed specular inputs, with a
  diffuse environment texture. Host sampler semantics and coordinate groups determine their
  slots. Material definitions, model references and shader metadata remain consistent; added
  pixel texture declarations preserve existing constants, vertex and tessellation bindings.
  Pixel programs and lighting arithmetic remain unfinished; these checks do not produce a
  playable model.
- Output is written in a fresh staging directory and published only after success. Existing
  output and paths overlapping either install are refused, including symlink aliases.
- Dependencies use a pinned source revision, NuGet lock files and an explicit Oodle adapter
  adjustment. External checkouts and their resources are not vendored into this repository.
- The DXC adapter compiles authored shader source and reassembles intermediate programs.
  A small authored C++ helper accesses DXC's assembly and validation APIs while preserving
  the template root signature. DXC itself remains separately installed.
- Vertex shader rewriting follows signature semantics and instruction dependencies to carry a
  second tangent and both floating-point bitangents through the installed transform. Matching
  scalar unpacking is derived from the program so bitangents avoid quantization. It preserves
  independent control flow and rejects unsupported transforms or tangent-dependent branches.
  Authored CPU/GPU comparisons and two freshly read ground-family vertex programs pass.
  Pixel-stage and final-colour translations remain unfinished.
- A pinned external HlslDecompiler converts source vertex/pixel programs. Bytecode and resource
  declarations are checked before conversion; compute stages, writable resources and unsupported
  instructions fail. The converter's complete external distribution is fingerprinted. It is
  built separately and never becomes a runtime or release dependency.
- Source parameters bind by reflected field name, type, position, matrix order and array
  stride. Optional complete specialization embeds exact register bits before compilation;
  missing or unsupported fields and malformed generated declarations fail. Deriving every
  material/runtime value and integrating the specialized result with host pixel programs
  remain unfinished.
- MinHook is pinned for the native lighting runtime. An authored native check proves hook,
  original-call, disable and removal behaviour, with deterministic builds and licence staging.
- Host runtime binding derives the world-manager slot from executable type metadata and bounded
  constructor-call analysis using the pinned compiler package's disassembler. A minimal player
  and physics layout supports executable version 2.7.2.0. The native reader verifies the generated
  configuration against the executable fingerprint, handles relocation and skips missing player
  state. Captured shader and illumination inputs still need replacing before runtime integration.

## Developer checks

Use the SDK version in `global.json`. Install-dependent checks require both environment
variables below, refuse CI, and write beneath the system temporary directory.

`source-renderer` and `source-lighting-parameters` check authored executable metadata and ambient
colour arithmetic. `source-renderer-installs OUTPUT` reads both installs, writes the renderer
settings and ambient coefficients for every source scenery and character lighting row, and
verifies unchanged inputs. Optional `AOG_SOURCE_LIGHTING_REFERENCE` supplies a private captured
reference for comparison only. Linux and Windows output equality is checked separately.
`source-environment-settings` checks authored instruction/control-flow variants;
`source-environment-settings-installs OUTPUT` additionally derives the installed environment gain
with `AOG_LLVM_MINGW`. Both platform distributions of the pinned LLVM-MinGW release are checked.

```sh
python eng/bootstrap.py /var/tmp/aog-libraries
export AOG_DEPENDENCIES=/var/tmp/aog-libraries
dotnet run --project tests/AgeOfGold.Checks

export AOG_DARK_SOULS_REMASTERED='/path/to/source/install'
export AOG_ELDEN_RING='/path/to/host/Game'
TMPDIR=/var/tmp python tests/check_installs.py /var/tmp/aog-input-check --firelink
```

The bootstrap's optional `--mirror PATH` uses a local Git repository at the pinned commit.
Use `--hk-mirror PATH` for an existing HKLib repository.
Use `--minhook-mirror PATH` for an existing MinHook repository.
Use `--shader-mirror PATH` for an existing HlslDecompiler repository.
It creates a fresh checkout and ignores that repository's working-tree modifications.
The external manifest records every checked-out file after the explicit adapter adjustment.

To produce one preparation directory:

```sh
export AOG_LLVM_MINGW='/path/to/pinned/llvm-mingw'
dotnet run --project src/AgeOfGold -c Release -- prepare-firelink \
  --source "$AOG_DARK_SOULS_REMASTERED" --host "$AOG_ELDEN_RING" \
  --output /var/tmp/aog-firelink-inputs
```

For a targeted extraction, `prepare` also takes `--request FILE`. Its JSON contains only
`source` and `host` arrays of relative file paths. This is a developer diagnostic, not a
way to bypass the required installs.

Shader checks require external DXC release `v1.9.2609`, compiler version `1.9.2609.5`, and
a native C++17 compiler plus CMake to build our small API helper:

```sh
export AOG_DXC_ROOT='/path/to/external/dxc'
cmake -S eng/DxcAssembler -B /var/tmp/aog-dxc-helper -DCMAKE_BUILD_TYPE=Release
cmake --build /var/tmp/aog-dxc-helper
export AOG_DXC="$AOG_DXC_ROOT/bin/dxc"
export AOG_DXC_ASSEMBLER=/var/tmp/aog-dxc-helper/aog-dxc-assemble
dotnet run --project tests/AgeOfGold.Checks -- shaders
```

Build the source shader converter separately, outside the repository. The authored recipe
compiles only the pinned upstream command-line implementation under our .NET SDK, excluding
its tests, fixtures and unused NUnit dependency:

```sh
python eng/build_shader_tool.py /var/tmp/aog-shader-converter
export AOG_SHADER_CONVERTER=/var/tmp/aog-shader-converter
TMPDIR=/var/tmp python tests/check_shader_tool.py /var/tmp/aog-shader-tool-check
dotnet run --project tests/AgeOfGold.Checks -- source-shader-installs
```

`source-shader-installs` requires both install variables. It converts every source graphics
stage, rejects source compute stages, and compares the compiled resource names and slots with
the installed program's reflection records. It also checks every constant buffer's byte extent.
Deferred-lighting programs and a layered ground program also repeat exactly. The adapter makes
every texture and sampler slot explicit;
automatic allocation can otherwise fill a gap with a later resource. This establishes
conversion and binding preservation, not host renderer integration. Before conversion, constant
reflection is normalized to complete register arrays while retaining the executable instructions.
This preserves gaps, packed component offsets and dynamic indexing instead of asking the
converter to reconstruct variable packing. Integer switch selectors retain their register bits.
Unsupported field types and malformed extents fail. Reflection aliases are allowed only for an
unused prefix over the first element of a used array with the same element shape.
The external converter manifest records its source revision, SDK and all output
fingerprints. `build_shader_tool.py --runtime win-x64` builds its Windows distribution.

With `AOG_LLVM_MINGW` set as described below, the authored numerical check runs without opening
a game or window:

```sh
TMPDIR=/var/tmp python tests/check_shader_execution.py /var/tmp/aog-shader-gpu-check
```

It compiles our own source-format fixtures using the platform Direct3D compiler, translates
them through the maintained adapter, and compares floating-point GPU readback with both the
reference program and independent CPU expectations. It also rejects a compute program and a
pixel program with writable buffers, and checks altered converter files and revisions. On
Linux the native harness runs under Wine. These fixtures cover arithmetic, branching, loops,
sparse texture slots and distinct wrap, clamp and border samplers. They do not prove the entire
installed shader corpus equivalent in play. Constant fixtures also check gaps, packed components,
dynamic matrix-array indexing, integer switch cases and shader-model 4 and 5 inputs. The matrix fixture uses buffer
bindings: the development graphics backend failed its independent reference with root constants.
Resource arrays and unsupported reflection or
declaration shapes stop conversion rather than inheriting the compiler's guessed placement.

`source-parameters` checks binding values by reflected buffer and field names. Every active
field must be supplied with its declared scalar type and logical shape; unused aliases cannot
be assigned. Padding starts at zero, and matrix values arrive in logical row order regardless
of their stored order. The GPU check also reads these generated buffers through the authored
source and translated programs, covering packed vectors, vector arrays, both matrix orders,
boolean flags, signed values and unsigned bit operations. This establishes parameter transport;
selecting every material, camera and lighting value for the playable build remains work.

`dotnet run --project tests/AgeOfGold.Checks -- vertex-shader` checks semantic second-tangent
rewriting and rejected contracts. `TMPDIR=/var/tmp python tests/check_vertex_execution.py
/var/tmp/aog-vertex-gpu-check` compares authored GPU and CPU results on both conditional paths
without opening a window. It uses the same external DXC, assembler and native compiler.
The `vertex-frames` check and the GPU script's `--frames` option also exercise both floating-point
bitangents, including rejection of unequal and nonlinear input decoding.

For installed programs, run `vertex-shader-extract FRESH_DIRECTORY` with the Windows check
executable under Wine and both install variables. Then run `vertex-shader-installed DIRECTORY`
with the native .NET check executable and DXC. The extraction records both installs' consumed
fingerprints and the source surface inventory; the second step checks those extracted program
hashes, rewrites each program twice and validates the generated signatures. All paths remain
under system temp. This split permits using the installed Windows compression library and
Linux DXC without treating a historical playtest as a construction input.
`vertex-frames-installed DIRECTORY` checks the complete tangent/bitangent extension using
those same fresh inputs and writes a separate report.

These commands have been exercised on Linux. Windows remains a target; this checkpoint does
not claim that the complete builder or native helper has been verified on Windows. The host
collision check uses a self-contained Windows build under Wine so it can load the compression
DLL from the host install:

```sh
dotnet publish tests/AgeOfGold.Checks -c Release -r win-x64 --self-contained -o /var/tmp/aog-collision-check
# Under Wine, set the install environment variables to their Wine paths first.
wine /var/tmp/aog-collision-check/AgeOfGold.Checks.exe collision-installs
wine /var/tmp/aog-collision-check/AgeOfGold.Checks.exe prepared-host-installs 'Z:\var\tmp\aog-input-check\first'
```

The second check reads the prepared host map, every material definition and the model geometry
through their format parsers. Run it alongside the repeatability check: identical output alone
cannot establish that extraction recovered usable files. The preparation check also rejects
host templates without their expected compressed-container header.

The `source-collision-installs` check additionally takes `AOG_COLLISION_REFERENCE`, pointing to
a private export from the preceding reader. It verifies the reference's input fingerprints
before comparing all geometry and transform values. That export is comparison evidence only;
the maintained preparation command does not read it.

`layer-geometry` and `layer-model` exercise independent texture mappings, frame handedness,
colour/alpha interpolation, large-coordinate subdivision and host-format readback using authored
fixtures. They also run in the default authored suite. `layer-model-installs` reads both installs
and, under Wine on Linux, checks all 85 surfaces in the supported two-layer ground family.
It rebuilds each selected model twice, reads every attribute back and verifies unchanged inputs.
The host template supplies the layout and material state. `layer-material`, `material-writer`
and `shader-metadata` check texture roles and consistent bindings with authored fixtures, and
also run in the default suite. `layer-material-installs FRESH_DIRECTORY` extends the installed
shader declarations and rebuilds all 85 ground surfaces with 70 source textures. It checks
every face/mip payload, geometry attribute and model/material reference, exact repeat builds,
and unchanged installed inputs. It retains one model and environment texture with complete
reports, rather than a second texture library. `shader-metadata-installs FRESH_DIRECTORY`
runs the narrower declaration check. Both run under Wine on Linux. Other material families,
pixel-program translation and final shader/material identities remain unfinished.

`terrain-installs` runs with the same Windows check executable and install variables. It reads
both installs directly, assembles both collision sets and the map twice, checks every written
triangle and lookup tree, verifies source visibility and spawn support, and rechecks consumed
install bytes. The `terrain-geometry` and `map-terrain` authored checks cover transforms,
winding, excluded roles, visibility, identity conflicts and preservation of unrelated map
fields. These stages do not yet produce a complete playable build.

`texture-writer` checks authored packages and malformed input. `texture-installs FRESH_OUTPUT`
runs under Wine like the other host-format checks, with an output directory under its system
temporary directory. It reads all source surface textures, rebuilds supported sampling variants
twice, reads the DDS data back and records a manifest for independent comparison. Under Wine,
set `TEMP` and `TMP` inside a `cmd` script when directing output to a Linux disk-backed temporary
directory; Wine can replace values supplied only in the launching shell. The full external
format reader/writer also matches the generated packages exactly. That independent oracle is
private test tooling, not a builder dependency or construction input.

`environment-texture` exercises authored HDR cubes and rejection of incomplete faces, arrays,
inconsistent dimensions/headers and sRGB interpretation. It also runs in the authored suite.
`environment-texture-installs FRESH_OUTPUT` runs under Wine as above, reading the area's full
source environment library and a host cube template. It checks exact repeats and all face/mip
payloads while retaining only two representative packages and the complete report. Packaging
does not yet bind these textures to the rebuilt lighting programs.

Preparation also reads scenery, character and point-light settings using the source install's
own parameter definitions. `lighting.json` retains encoded colours separately from intensity;
it does not yet translate their colour space or reproduce the renderer. Scene placements retain
their lighting and player-light selections. The install check verifies that these references
resolve, and that both independent preparations retain identical lighting data.

`shader-archives` checks flat and nested shader access, replacement, compression preservation
and malformed-container rejection using authored fixtures. `shader-archive-installs` reads
programs from both installs and exercises replacement within an installed host shader family.
Run the latter under Wine with the Windows check executable and install paths, as above. Reads
extract only the requested entries from the large host shader library. This proves archive
access and preservation, not the shading translation or a complete shader-library rebuild.

With both install environment variables set, `dotnet run --project tests/AgeOfGold.Checks --
regulation-installs` checks key discovery, two independent rebuilds, readback and unchanged
install inputs. It writes only to a temporary directory and needs no schema export.

`dotnet run --project tests/AgeOfGold.Checks -- gameplay-installs` exercises the two table
writers against the installed regulation. It uses a source bonfire placement, rebuilds twice,
reads the encrypted result back, verifies the remaining tables and checks unchanged installs.
The authored `gameplay-tables` mode covers field preservation and rejected layouts/references.

Native checks use external LLVM-MinGW release 20261104, pinned in `eng/native-toolchain.json`.
Verify the downloaded archive against the checksum for your platform before unpacking it.
With `AOG_DEPENDENCIES` set, run:

```sh
export AOG_LLVM_MINGW='/path/to/llvm-mingw-20261104-ucrt'
TMPDIR=/var/tmp python tests/check_native.py /var/tmp/aog-native-check
```

The check configures `tests/NativeHooks` with `eng/llvm-mingw.cmake`, builds twice and runs the
authored executable directly on Windows or under Wine on Linux. It checks deterministic bytes,
hook removal, native player/physics sampling and staged licence notices. It also uses the .NET
check project to generate an authored runtime configuration. The native test binds it to its
own executable and checks changed fingerprints, malformed configurations and missing state.
On Linux it also checks refusal of an unsupported
compiler. CMake needs a generator that honours the selected compiler, such as Ninja or Unix
Makefiles. These checks have been exercised on Linux/Wine; native Windows remains unverified.
No game is launched. The compiler distribution is neither committed nor bundled.

With the same compiler environment, `dotnet run --project tests/AgeOfGold.Checks -- host-binding`
checks a wholly authored PE executable, relocation, zero-filled data, ambiguous metadata and
rejected control flow. `host-binding-installs FRESH_TEMP_DIRECTORY` additionally requires both
install variables and checks two independent discoveries against unchanged installed inputs.
Its output contains the generated runtime configuration and a check report. An optional
`AOG_RUNTIME_REFERENCE` names the private discovery oracle for independent comparison; it is
read only by this test and is never a builder input.

## Evidence and remaining stages

The authored checks exercise the CLI and format readers using files created from scratch.
They cover rejected writes and malformed requests as well as geometry, materials, texture
extraction and shader binding preservation. The install check prepares twice, compares every
output and independently re-reads every recorded install input. Both checks pass locally.
Host entries with an omitted unpadded length retain their stored extent. An earlier preparation
mistakenly emitted empty host templates; the authored regression and real format readback now
cover that case. Earlier repeatability reports alone do not prove host-template extraction.
The private comparison also finds exact agreement with the retained independent-layer
geometry and texture exports. Those exports are test references, never construction inputs.

The older walking baseline also rebuilt successfully from the installs. It uses the spike
toolchain and does not reproduce the accepted lighting, so it is separate evidence.

Work remains before a reproducible playtest can replace the accepted build:

1. Terrain collision and its map placement stage are checked together, but still need the
   complete build orchestration. Special collision roles remain explicitly outside the
   accepted ordinary-solid subset; they must not silently become solid terrain.
2. Material, lighting, atmosphere and effect stages must replace historical exported inputs
   and captured constants with install-derived inputs. Shader assembly alone does not prove
   those translations or recover their source parameters.
3. Gameplay tables now have checked writers; they must be connected to the map, event and
   text bindings. The checked install-derived runtime binding must join the illumination runtime.
4. A fresh full build must match the accepted build's behaviour, pass readback and background
   play checks, and be built again independently before promotion and historical cleanup.

Keep the accepted playtest, rollback, private kit, capture parents and historical stage inputs
until those replacements are proven. Newly generated identical preparation directories can
be removed after their hashes and comparison reports have been retained.
