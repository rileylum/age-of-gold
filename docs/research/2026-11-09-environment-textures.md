# Environment texture packaging — 9 November 2026

The maintained texture writer now handles a complete BC6H unsigned HDR cube. It retains the
compressed face/mip chains verbatim and derives the host format tag from an installed cube
template. The DDS describes one cube with six faces; incomplete or nonsquare cubes, inconsistent
headers, cube arrays, missing mip payloads and sRGB interpretation are rejected. Surface texture
formats remain separately checked. The writer does not decode or recompress pixels.

The authored workflow failed first on the unsupported cube dimensions, then passed complete
payload/readback, repeatability and malformed-input checks. A real-install check reads all
174 cubes in the source area's environment archive, builds every one twice and reads back all
faces and mip levels. All output bytes repeat and installed inputs remain unchanged. The host
format tag comes from a real host environment binder, not an arbitrary surface texture. Only
one diffuse and one specular representative are retained with the complete fingerprint report.
Both representatives also pass an independent full-library TPF read/write check with exact
output equality.

This establishes packaging. Selecting and binding environment textures to rebuilt shader
programs remains separate work. No game was launched or playtest replaced.

Sources: read-only local install inspection; SoulsFormatsNEXT's pinned TPF/DDS readers
(GPL-3.0); Microsoft's [DDS header documentation](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header-dxt10)
(Microsoft Learn documentation terms). The documentation describes cube arrays as six faces
per cube; no documentation text, community implementation or game payload was copied into
the repository.
