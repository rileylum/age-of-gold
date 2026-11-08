# Firelink consolidation — 9 November 2026

Riley asked to stop accumulating experiments and consolidate a usable playtest, reusable
tools and findings for subsequent areas. The broad fidelity investigation is paused during
this work. This document records local development practice; it does not settle the
production language, libraries or licence decisions in [core.md](core.md).

## Deliverables and current state

| Deliverable | Current state | Remaining work |
| --- | --- | --- |
| Persistent ordinary playtest | The existing scenery preview has been materialized outside temporary storage. All build files match the preceding preview; the offline profile keeps the same resumable save. | Promote the accumulated, verified improvements after an ordinary-play smoke test. |
| Reusable comparison tools | Camera readback, scripted startup/normal exit, paired captures, ordinary-play position reports and capacity preflight are in `spikes/visual-compare`. | Consolidate the external background launcher and game adapters with a restoration check and a small private dependency manifest. |
| Reusable findings | The index below identifies the format and rendering conclusions separately from their private captures. | Turn accepted private stages into a reproducible build recipe; preserve their provenance and licence boundaries. |
| Bounded reference set | Identical files in frozen repeated builds and published tools have been consolidated without removing paths. Interrupted work has a verified private archive. | Flatten dependencies of retained builds, then retire redundant experiments and exports. |

The ordinary playtest is the existing accepted preview, not the latest research build.
The later two-appearance flame candidate passed live checks. The subsequent four-flame,
three-glow candidate passed build/readback checks but still lacks live verification.
Those states must remain distinct when choosing a build to promote.

## Retention workflow

Keep one current ordinary playtest and one preceding rollback after the next promotion.
Each must load from persistent storage without a chain of experimental build directories.
Keep its build recipe, tool/install fingerprints, offline profile and verification report.
Saves are independent of builds and are never discarded as experiment output.

Keep one active candidate and its necessary inputs. A repeat build exists to prove
determinism: after comparing every output, retain the comparison manifest and consolidate
the identical bytes. A repeat build must be generated independently before deduplication.
Hardlinks are limited to completed, immutable outputs; every new build uses a fresh
directory. Editing a shared file in place would change every linked checkpoint.

For each retained finding, keep a small report, its generator/check, the selected source
and host views, and the inputs needed to reproduce the result. Keep a GPU capture when the
finding depends on its draw state or replay; an ordinary screenshot cannot replace that.
Reuse source captures while their install and conditions remain applicable. Keep a failed
control when it distinguishes the accepted explanation, rather than every failed export.

Retire an old root only after a dependency audit shows that no retained build, script or
capture delta needs it. Materialize the retained dependency chain first, verify hashes and
exercise restoration. A delta and its parent form one retention unit. Generated exports
can then be removed if the retained capture and exporter reproduce them. Do not retain
both raw captures and independently restorable archived versions indefinitely.

During consolidation, do not grow the long-term research store with more experimental
checkpoints. The next capture work serves playtest promotion or restoration verification.
Batch budgets should leave at least 20 GiB free on each disk filesystem and 2 GiB on each
temporary-memory filesystem. Use the capacity command before each batch, with estimates
for capture, export, save staging and preservation. These are local operating limits;
they are not production requirements or a guarantee against other programs using space.

## Next promotion

1. Assemble a self-contained candidate from the latest live-verified scenery and lighting
   work. Preserve the accepted preview as rollback. Record exactly which runtime components
   it needs; diagnostic tracing must not create unbounded logs during ordinary play.
2. Verify the candidate against both installs and read back its complete build. Exercise
   cold startup, Continue with an isolated save, the dark chapel and open courtyard views,
   ordinary movement, and normal exit in the private background session.
3. Promote only after that check. Keep the user's resumable save, supply the same ordinary
   launcher, and request a brief traversal check. Aqueduct repairs remain outside this pass.
4. Consolidate the private tools and dependency manifest. Prove restoration in a fresh
   directory before pruning the experimental chain. Broader fidelity work follows this
   checkpoint rather than expanding the archive first.

## Findings to carry to another area

| Topic | Reusable conclusion | Evidence and limits |
| --- | --- | --- |
| Geometry and visibility | Collision, model bounds, placement and visibility selection need independent checks. Rendering a surface once does not establish stable visibility. | [Rendering](research/2026-11-07-spike-firelink-rendering.md), [visibility](research/2026-11-07-firelink-visibility.md). |
| Texture coordinates | A serialized vertex layout can still violate the shader's input contract. Checked packed coordinates and per-triangle tile shifts preserve repeating texture mappings. | [Packed coordinates and world isolation](research/2026-11-07-packed-uv-and-world-isolation.md). The private stage and older generic converter are not yet one pipeline. |
| Lighting and materials | Sun direction alone does not explain shaded-surface darkness. Classify source lighting selections, baked contributions, material inputs and later composition separately. | [Lighting](research/2026-11-07-spike-firelink-lighting.md), [surface and composition controls](research/2026-11-08-surface-tangents.md). Later sections supersede earlier hypotheses. |
| Automated comparison | Read back pose, FOV and streaming anchor; record conditions; use isolated saves and sequential offline sessions. A camera teleport does not load a distant area. | [Comparison tooling](research/2026-11-07-visual-comparison-tooling.md), [ordinary-play observation](research/2026-11-07-playtest-observation.md). Game adapters remain private. |
| Effects | Verify native input routing, transform order, sampling and the immediate target before each appearance. Build/readback success does not establish live rendering. | [Effects](research/2026-11-08-effects.md), [flame filtering and geometry](research/2026-11-09-bonfire-layers.md), [unverified glow candidate](research/2026-11-09-bonfire-glow-translation.md). Exact timing and random behaviour remain open. |

All real captures, game data, external tools and game-specific bindings stay outside Git
and releases. The linked research records third-party licences. The local cleanup uses
the installed util-linux `hardlink` command with content comparison and metadata checks;
it adds no runtime dependency to the mod or the build tool.
