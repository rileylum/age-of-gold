# Semantic vertex shader rewriting — 9 November 2026

The maintained ground-family shader adapter now derives a second tangent transform from the
installed vertex program's input/output semantics and scalar instruction dependencies. It
shares independent matrix and resource reads, duplicates only the required arithmetic, and
retains the program's control flow. An input-dependent branch cannot be shared between two
different tangent values, so that case fails. Unsupported operations and signature encodings
also fail. The adapter contains no captured instructions or fixed instruction/metadata IDs.

An authored conditional transform first failed at the missing implementation. It subsequently
caught a metadata allocator collision with a distinct control-flow node. The corrected adapter
passes both branches on the GPU against an independently compiled shader and CPU arithmetic:
256 floating-point values per branch, zero difference. The pixel fixture must retain the
vertex output layout for stage linkage; the first mismatched fixture returned zeros and was
corrected before accepting numerical evidence. Metadata renumbering leaves identical output;
missing, duplicate and unsupported semantics and tangent-dependent branches are rejected.

Fresh reads of both installs identify 85 source ground surfaces and two relevant host vertex
programs. Both host programs are rewritten twice with exact byte equality, DXC validation and
signature/file readback. All consumed installed inputs remain unchanged. These checks prove
second-tangent transport, not the complete material or pixel shader translation. No game was
launched. Floating-point bitangents and subsequent colour/normal translations remain pending.

Sources: our own authored fixtures and read-only local install inspection, plus Microsoft's
[DXIL specification](https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/DXIL.rst)
for signature metadata and scalar operations. DirectXShaderCompiler records the University of
Illinois/NCSA licence for its LLVM portions and MIT for Microsoft's contributions, with further
third-party notices in the upstream licence files.
No upstream implementation was copied. Private program text and generated binaries remain
outside the repository and releases.
