# Reproducible build — work in progress

The accepted private playtest remains the comparison reference. The maintained builder is
being assembled from readers that use the two installs directly. Restoring the private kit
does not count as a fresh build, and the new preparation command does not yet produce a
playable mod.

## Implemented

- A C# command reads requested files from both installs, derives host archive keys from the
  host executable, decrypts requested entries and records the bytes it read. Conflicting
  archive entries fail; identical duplicates are checked rather than selected by accident.
- Firelink preparation reads placed source models and material definitions, including patch
  precedence, rigid transforms, triangle winding, vertex colours, tangent frames and all UV
  channels. It extracts the explicitly referenced DDS textures without altering their bytes.
- Source collision reading uses serialized type descriptions, validates references and extracts
  both sets of triangle meshes, their materials, transforms, placements and roles. It does not
  need the earlier Python reader or bundled game type descriptions.
- Host collision serialization reads its metadata and type hashes from the installed
  compendium. It runs with HKLib's resource directory excluded. Both template round trips and
  generated meshes pass readback, including section and vertex-slot boundaries.
- Output is written in a fresh staging directory and published only after success. Existing
  output and paths overlapping either install are refused, including symlink aliases.
- Dependencies use a pinned source revision, NuGet lock files and an explicit Oodle adapter
  adjustment. External checkouts and their resources are not vendored into this repository.
- The DXC adapter compiles authored shader source and reassembles intermediate programs.
  A small authored C++ helper accesses DXC's assembly and validation APIs while preserving
  the template root signature. DXC itself remains separately installed.

## Developer checks

Use the SDK version in `global.json`. Install-dependent checks require both environment
variables below, refuse CI, and write beneath the system temporary directory.

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
It creates a fresh checkout and ignores that repository's working-tree modifications.
The external manifest records every checked-out file after the explicit adapter adjustment.

To produce one preparation directory:

```sh
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

These commands have been exercised on Linux. Windows remains a target; this checkpoint does
not claim that the complete builder or native helper has been verified on Windows. The host
collision check uses a self-contained Windows build under Wine so it can load the compression
DLL from the host install:

```sh
dotnet publish tests/AgeOfGold.Checks -c Release -r win-x64 --self-contained -o /var/tmp/aog-collision-check
# Under Wine, set the install environment variables to their Wine paths first.
wine /var/tmp/aog-collision-check/AgeOfGold.Checks.exe collision-installs
```

The `source-collision-installs` check additionally takes `AOG_COLLISION_REFERENCE`, pointing to
a private export from the preceding reader. It verifies the reference's input fingerprints
before comparing all geometry and transform values. That export is comparison evidence only;
the maintained preparation command does not read it.

## Evidence and remaining stages

The authored checks exercise the CLI and format readers using files created from scratch.
They cover rejected writes and malformed requests as well as geometry, materials, texture
extraction and shader binding preservation. The install check prepares twice, compares every
output and independently re-reads every recorded install input. Both checks pass locally.
The private comparison also finds exact agreement with the retained independent-layer
geometry and texture exports. Those exports are test references, never construction inputs.

The older walking baseline also rebuilt successfully from the installs. It uses the spike
toolchain and does not reproduce the accepted lighting, so it is separate evidence.

Work remains before a reproducible playtest can replace the accepted build:

1. The checked collision reader and writer still need integration into the complete build and
   its map placement stage. Preparation retains special collision roles; their gameplay
   behaviour must not silently become ordinary solid terrain.
2. Material, lighting, atmosphere and effect stages must replace historical exported inputs
   and captured constants with install-derived inputs. Shader assembly alone does not prove
   those translations or recover their source parameters.
3. Gameplay table edits need the unresolved regulation-key and schema work in `core.md`.
   The illumination runtime also needs an install-derived binding recipe.
4. A fresh full build must match the accepted build's behaviour, pass readback and background
   play checks, and be built again independently before promotion and historical cleanup.

Keep the accepted playtest, rollback, private kit, capture parents and historical stage inputs
until those replacements are proven. Newly generated identical preparation directories can
be removed after their hashes and comparison reports have been retained.
