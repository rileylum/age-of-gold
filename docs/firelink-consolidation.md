# Firelink consolidation — 9 November 2026

Riley asked to stop accumulating experiments and consolidate a usable playtest, reusable
tools and findings for subsequent areas. This consolidation goal replaced the broad fidelity
goal; further fidelity work is deferred. This document records local development practice; it does not settle the
production language, libraries or licence decisions in [core.md](core.md).

## Deliverables and current state

| Deliverable | Current state | Remaining work |
| --- | --- | --- |
| Persistent ordinary playtest | Latest live-verified improvements promoted with the illumination runtime; preceding scenery build retained as rollback. Both have regular files without external file links. | Riley's traversal check. |
| Reusable comparison tools | Generic controllers remain in `spikes/visual-compare`. The private kit restores the background session, both adapters, isolated saves, observer and menu templates. Both games passed two views twice and normal exit after restoration. | Recalibrate bookmarks and bindings when changing area or install. |
| Reusable build tools and findings | Private runtime rebuild matches the promoted binary. Both private format tools rebuild; the inspector passes texture checks and reads a promoted model. The kit indexes stage recipes and provenance; findings are below. | Integrate private stages into the production install-to-build pipeline in a later goal. |
| Bounded reference set | One current build, one rollback, one recovery/tool kit, one restored workspace and selected evidence. Earlier deduplication recovered 53.78 GiB; verified motion-capture deltas recovered another 6.56 GiB, followed by removal of reconstruction scratch. | Restore compacted historical captures before replay; retain each delta's parent. |

The playtest includes the live-verified two-appearance flame candidate. The subsequent
four-flame, three-glow experiment remains excluded: it lacks live verification.

Launch with `python ~/.local/share/age-of-gold/playtests/firelink/launch.py`, then Continue.
Add `--rollback` to select the preceding scenery build. Both use the existing resumable
playtest save. Use this launcher: it also supplies the runtime's diagnostic destination.
The ordinary profile contains no camera adapter. Logs reset on launch and are bounded;
a separate small error record retains failures after the main trace reaches its limit.

The private kit is `~/.local/share/age-of-gold/development/firelink-kit-v1`; its `README.md`
documents restoration, background comparison, runtime/format-tool rebuilding and recovery.
`index.json` links verification and historical evidence. All game-specific contents remain
outside Git. Its build recovery reconstructs frozen snapshots, not a fresh conversion from
the installs. The earlier stage inputs remain necessary for historical research.

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

## Verification and handoff

The candidate passed cold startup, Continue, three checked views, ordinary movement and
normal exit using an isolated save. A separate session verified loading and rendering with
the runtime present and no camera DLL. The actual launcher was checked for offline arguments,
profile selection and the trace environment; normal saves retained their hashes.

The private kit restored into a fresh directory. Host and source each produced two checked
chapel views and two cliff views, then exited normally. The host's capture-only clock hold
and anchor correction are distinct from ordinary play. An initial capture rejected the
slightly displaced saved anchor; the existing correction then passed. The old third-view
label `stairs` was inaccurate; the kit calls that view `west-wall`.

Both complete build snapshots restored and matched every file hash. Eight historical motion
captures were independently reconstructed to disk and hashed before their redundant raw
files were removed. Four original parent captures remain. The private compaction manifest
and restoration command preserve the dependency information. Reconstruction scratch was
removed after its checks; historical build stages and capture parents were not blanket-deleted.

Riley's next step is a brief traversal using the launcher above. Aqueduct repairs, remaining
effect fidelity and production pipeline integration remain outside this completed pass.

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
