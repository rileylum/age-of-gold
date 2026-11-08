# Additional bonfire flames and glow — 9 November 2026

The private candidate now includes narrow upright flames and smaller camera-facing flames
alongside the two previously verified appearances. This is a build checkpoint, not a live
verification or an ordinary playtest update.

The reader derives their dimensions, lifetime, emission settings, movement, rotation,
colour curves and atlas animation from the source definition. A first candidate omits a
nested vertical offset and fails the captured-particle bounds. Including that offset passes
without changing the tolerances. Source vertices also agree with reconstruction from the
captured particle inputs. These checks establish feasible ages and parameter bounds, not
exact emission cadence, movement integration or random distributions.

The writer preserves the two existing appearance subtrees. The camera-facing appearance
uses one random size sample for both dimensions, matching the source's square particles.
Independent readback verifies those properties, and two complete builds match byte for
byte. Only the selected effect payload and two scoped shader entries change. The new
upright appearance uses the previously checked rotation-order correction; the square
appearance retains native camera-facing geometry. Their compiled shaders repeat exactly.
GPU marker routing and the new native particles still need a fresh live capture.

The remaining soft glow has a distinct depth response. The source supplies camera-facing
corners together with a volume thickness. Across the three captured glow appearances,
thickness matches the mean of width and height. A width-only hypothesis fails on the
elongated appearance. The source projects the front of that volume for depth testing and
normalizes opacity by thickness. An independent calculation agrees with the captured
vertices, projected depth and opacity. The repeating colour curve also matches the mature
particle age stored in the input buffer. A small horizontal attachment discrepancy remains
explicit; these checks do not claim exact absolute placement.

A per-appearance GPU replay removes only the scene-depth fade. Targets preceding the
selected draw remain byte-identical. The resulting contribution is substantially stronger;
treating it as the source result fails clearly. Applying the independently read scene depth
and volume bounds reproduces all three source contributions within the packed target's
quantization bounds. This supports translating a depth volume, rather than assigning the
ordinary flame path to the glow. Startup, near-camera transitions and host implementation
remain unverified. The first input exporter used an unavailable replay API; its failure is
retained separately from the corrected export.

No game was launched for this checkpoint. Existing source captures supplied the evidence,
and all GPU replays closed. Persistent storage is nearly full, so another complete live
recording is pending more capacity. A compressed-bundle delta was too large; the candidate
bundle can instead be reconstructed exactly from the immutable preceding bundle, the
small native effect and the recorded writer. Preservation checks that reconstruction's
complete hash. The other new evidence is archived privately. Earlier checkpoints remain
unchanged, and the ordinary playtest still selects its existing build.

The private tools reuse the licences recorded in [the preceding research](2026-11-09-bonfire-layers.md):
SoulsFormatsNEXT is GPL-3.0, the FXR helper is Unlicense, RenderDoc is MIT, DXC carries
LLVM/NCSA and component notices, and Zstandard is BSD-3-Clause. No new production dependency
or licence decision is made. Game data, shader listings, binaries and captures remain
outside the repository.
