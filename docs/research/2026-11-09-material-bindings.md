# Ground material bindings — 9 November 2026

The maintained ground adapter now reads two independent sets of colour, normal and packed
specular texture roles. It checks their source coordinate channels and unit scales, and
rejects unsupported detail textures. Host sampler semantics and coordinate groups determine
which slots receive each layer. Missing, duplicate or conflicting declarations fail.

Shader material metadata needs extending alongside the material definition and model
references. The writer inserts donor texture records, relocates the format's absolute
references and appends their strings. Existing pixel passes that use textures gain the added
slots; vertex, tessellation and texture-free pass bindings remain unchanged. Existing constant
records and opaque header values are retained. The adapter handles the checked metadata
revision only and rejects out-of-range references, overlapping tables, unsupported texture
extensions and undeclared sampler-mask bits. It uses the pinned library's independent reader
for readback; that library currently provides no writer for this format.

Authored tests failed before each new capability, then passed with distinct layers, parameter
readback, malformed inputs and repeated builds. The installed check extends the relevant host
declarations, binds all 85 source ground surfaces to 70 installed textures, and checks every
geometry attribute and texture mip payload. Repeated models are identical and consumed install
inputs remain unchanged. Only representative output and full reports are retained.

The source lighting rows' diffuse environment references resolve into a loose area texture
package, distinct from the GI archive checked earlier. The freshly read diffuse cube matches
the earlier GPU-attributed source cube exactly; that private comparison is evidence only and
does not supply construction data. The ordinary texture reader now supports these loose
packages with the same ambiguity checks as its split archives.

This proves material input transport and host-format readback. The pixel programs still need
translation, source material constants still need their rendering interpretation, and final
material/shader identities need integration into the complete build. No game was launched and
the accepted playtest was not replaced.

Sources: read-only inspection of both local installs and our earlier private capture comparison;
the pinned SoulsFormatsNEXT material/metadata readers (GPL-3.0). The external reader is compiled
through the existing dependency subset. No community implementation or game payload was added
to the repository.
