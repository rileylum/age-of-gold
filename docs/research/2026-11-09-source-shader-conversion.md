# Source shader conversion experiment — 9 November 2026

The maintained builder still needs a translation from the source install's DXBC programs to
programs the host renderer can use. The inspected host programs already contain DXIL. The
accepted playtest's captured programs are comparison evidence, never construction input.

The approved DXC release compiles HLSL and handles DXIL, but its binary distribution does not
include the DXBC converter found in its source tree. The attempted LLVM-MinGW build of that
converter failed on Windows support dependencies. Inspection of the official DXC NuGet package
and Windows SDK packages also found no usable converter. Logs, package listings and revision
information remain outside Git; no SDK or game program is included here.

A separate private experiment evaluated
[HlslDecompiler at revision 14bb0af4f9c074138df5a40d8d0bee0f30da0e30](https://github.com/AndresTraks/HlslDecompiler/tree/14bb0af4f9c074138df5a40d8d0bee0f30da0e30).
It uses the [MIT licence](https://github.com/AndresTraks/HlslDecompiler/blob/14bb0af4f9c074138df5a40d8d0bee0f30da0e30/LICENSE).
The unmodified external project targets .NET 8 and references NUnit 4.3.2, including in its
command-line build. It was evaluated as a candidate before the decision recorded below. Its
source, binaries, generated HLSL and game inputs remain outside Git and releases.

Using its instruction-oriented writer and the approved DXC compiler, all 2,848 extracted
vertex and pixel programs compiled. HLSL 2018 compatibility was needed for vector conditional
expressions. Ten representative programs produced identical compiled bytes in separate runs
through the command-line and direct-library paths. These results establish coverage and
repeatability of translation and compilation, not rendering equivalence for those programs.
The unmodified Windows command-line build also ran under Wine. Its output compiled with the
same Linux DXC to identical bytes for those ten programs. That checks the converter across
the two .NET environments, not native Windows graphics execution or the Windows DXC binary.

An authored D3D12 experiment compared a source-format program with its translated program on
the GPU. The test uses signed inputs, a uniform-controlled loop, vector conditionals, dot
products, clamp and bit reinterpretation. The graphics version renders a fullscreen triangle
into a floating-point target without opening a game or window. Both tested uniform values
matched independently calculated results; translation changed values by at most about
0.000001. This small fixture does not establish texture sampling, derivatives, all instructions
or the complete source lighting model.

The equivalent compute experiment exposed an incorrect translation: scalar stores at distinct
component offsets in a structured buffer became whole-vector assignments. The final scalar
was broadcast across the output. Separately, four of sixteen installed compute programs failed
conversion on an unsupported instruction. Compiling successfully is therefore insufficient
evidence of correctness. Compute conversion is rejected for this candidate.

The proposed scope is a pinned external converter for vertex and pixel programs with no
writable-buffer resources, followed by the approved DXC compiler. A maintained adapter would
reject other stages and unsupported resources, record tool fingerprints, retain authored
numerical checks, and compare the resulting Firelink rendering before replacing the accepted
playtest. A new instruction or resource requirement must fail and be investigated. This
proposal required Riley's decision under core constraint 6 before implementation.

The generated graphics sources contained no writable-buffer declarations. This is survey
evidence only; the proposed maintained adapter must inspect the input declarations itself.
Redundant corpus output, superseded check binaries and the failed converter source build were
removed after retaining fingerprints, reports and small reproductions. The private source
archive and revision remain available if the failed DXC build needs further investigation.

All game-derived programs and test output remain private. No game was launched, neither
install was modified, and the accepted playtest and saves were left intact.

Riley subsequently approved that restricted converter. The maintained build recipe compiles
only upstream production source with the pinned .NET SDK; it needs neither upstream fixtures
nor NUnit. Two independent external distributions match, including licence staging. The adapter
checks tool fingerprints, container structure, stage/version, token lengths, resource
declarations and unsupported opcodes before conversion. Authored rejection cases were observed
failing before their implementation. The maintained GPU check retains the numerical result
above and rejects an actual authored pixel shader that writes to a structured buffer. The
install check accepts all 2,848 graphics programs, rejects all sixteen compute programs and
converts each of the six deferred stages twice with deterministic DXC readback. Host renderer
binding and visual equivalence remain separate work.
The maintained Windows distribution also runs under Wine and matches the Linux distribution
and the prior pinned tool for the ten representative programs, with Linux DXC used throughout.

Instruction metadata is described by Microsoft's
[public token-format header](https://github.com/microsoft/DirectXShaderCompiler/blob/v1.9.2609/include/dxc/Support/d3d12TokenizedProgramFormat.hpp)
under the DXC distribution's previously recorded licence terms. Our boundary reader was authored
from that format description; it contains no game program or captured instruction stream.
