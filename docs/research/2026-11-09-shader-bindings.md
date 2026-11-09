# Source shader resource placement — 9 November 2026

While tracing the ground pixel calculation, a conversion readback found that some resource
bindings moved. The pinned converter emits an explicit register at a gap, then omits registers
for following consecutive resources. DXC allocates those unbound declarations into earlier
free slots. Successful compilation therefore did not establish texture or sampler identity.

An authored GPU fixture reproduces the failure without game inputs. It declares three textures
and three samplers across nonconsecutive slots, with different texture values and wrap, clamp
and border addressing. The source program agrees with independent CPU expectations. Before
the fix, the converted output differs by approximately 0.8 in one channel. With every binding
made explicit from input reflection, all 256 GPU values agree exactly with the source program;
the maximum CPU-reference error is below two hundred-millionths. Existing arithmetic and loop
comparisons retain their previous bounds.

A second readback found that flattened global constant declarations also lost register gaps.
An authored constant-buffer fixture fails numerically before correction, with maximum error
2.5. A separate packed-vector fixture fails compilation because the converter addresses it
using register component positions rather than positions within the declared vector.

The adapter now rewrites only constant reflection into complete arrays of four-component
registers before invoking the converter. Buffer slots and sizes derive from the input;
executable instructions and all other program sections remain unchanged. This preserves
register and component addressing without reproducing variable-layout rules in generated HLSL.
The intermediate checksum is cleared; that intermediate is consumed only by the pinned parser,
and DXC constructs the final program. It is never loaded by a game. Unsupported primitive
types, invalid field extents, active overlaps and ambiguous buffer bindings fail.

Two installed effect programs retain an unused matrix name over the first element of a used
matrix array. The adapter permits only that prefix pattern: the same start and element shape,
an unused nonarray field, and a larger used array. An authored reflection fixture adds this
alias to a matrix program without changing its executable instructions. The converter rejects
it before the correction and its GPU result matches the unmodified reference afterwards.
The native fixture compiler itself rejects overlapping declarations, so the fixture constructs
only the reflection alias. This is not evidence that overlapping active declarations are safe.

GPU checks now cover gaps, packed components, dynamic matrix arrays, integer inputs, and
shader-model 4 and 5 constant-buffer metadata. All constant fixtures match their GPU and CPU
references exactly. The first matrix comparison fails on the reference path with root constants:
the index changes, but the sampled matrix does not. Using constant buffers fixes that reference
failure without changing its CPU expectation. The root-constant run is retained as a failed
harness control, not conversion evidence.

Normalization also exposed an integer-selector defect: the converter emits a switch over a
constant without a bit reinterpretation. A direct float-to-integer conversion selects a
different branch. An authored switch confirms this on the GPU, with a maximum error of 3
before correction. The adapter explicitly reinterprets normalized constant selectors as
unsigned bits. Three additional comparisons cover zero, a nonzero case and the default branch.
All match their independent reference exactly. Across ten comparisons, 2,560 values are checked.

The maintained adapter reads the source program's resource table and supplies explicit registers
before compilation. Missing or ambiguous declarations, conflicting claimed slots, unsupported
resource arrays, invalid name/range records and unsupported shader revisions fail. This uses
the existing pinned converter and DXC; it neither modifies their external checkouts nor adds a
dependency. Shader source, binaries, installed reflection records and numerical results remain
outside the repository. Fixtures are authored from scratch.

The expanded install check compiles every supported graphics program and compares its resulting
resource inventory and slots with the input reflection table, and every constant buffer's
byte extent. It validates all constant metadata before compilation, then repeats the deferred
programs and a representative ground program. These are conversion and binding checks, not
proof of full material translation or equivalent rendering in the host. The complete survey
passes for 2,848 graphics programs, 33,493 resource bindings and 4,341 constant buffers. Seven
selected programs repeat exactly, and all 16 compute programs are rejected. A second complete
run includes the integer-selector correction and passes with the same inventory. Targeted
Windows checks also pass; native Windows shader compilation remains a separate check.

A named parameter writer now uses the same validated reflection to place typed values in
constant buffers. It requires exactly the active fields, zeros padding, and converts logical
matrix rows into the declared storage order. An authored check rejects the initial empty
writer; a second rejects its initial scalar-only implementation. Eight GPU comparisons then
check generated buffers through both source-format and converted authored shaders. Packed
vectors, vector arrays, both matrix orders, booleans, signed values and unsigned bit operations
match the independent expectations exactly. The full numerical suite now checks 4,608 values
across eighteen comparisons. This does not yet establish the source material and lighting
values to supply to a playable build.

Sources and licences: read-only installed shader reflection and generated private comparisons
(proprietary game data, never redistributed); the already approved HlslDecompiler source
(MIT) and DXC compiler (University of Illinois/NCSA and Microsoft MIT notices). No source from
either external tool was copied into the maintained adapter. The existing tool pins remain.
