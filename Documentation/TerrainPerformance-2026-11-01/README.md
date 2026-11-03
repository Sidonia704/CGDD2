# Terrain performance — SCOPED VALIDATED (CPU only)

**Status: SCOPED VALIDATED (CPU only).** All required inputs are present and passing: five compatible matched VerifyAssets runs, at least one real EditMode test report with Total>0 and no failures/skips/inconclusives, the schema-validated terrain-performance-2 profiler report, the 10-row prewarm audit and the unchanged 48-file hash snapshot. This label covers only that Editor CPU evidence: it is not FPS, Player-build, real-time, gameplay or map-assembly approval.

## Scope and preserved evidence

Optimize terrain before any more map assembly. This report does not assemble a map or modify source art, saved assets, ship/AI/flight behavior, gameplay tuning or project history.

The original v04 [README](<Documentation/MiningStation-Round2-2026-11-01/v04/README.md>) and evidence remain preserved; this is a separate fresh baseline, not a replacement for those earlier measurements.

Machine-readable results: [summary](<Documentation/TerrainPerformance-2026-11-01/baseline-summary.json>) · [source/asset hash audit](<Documentation/TerrainPerformance-2026-11-01/source-hash-audit.json>). Analyzer: [Summarize.py](<tools/TerrainPerformance/Summarize.py>).

## Optimized-runtime design under measurement

The baseline tables below are the unchanged v04 probe. These are the optimized-runtime design points the matched runs are meant to measure, not proof that a benefit already exists:

- **Chunk rendering is lazy.** An intact asset keeps its single root renderer and aggregate mesh; exact-4-cube (`exact4cube`) chunk render meshes activate only on the **first lethal hit** that actually destroys or detaches geometry, not on load, validation or warm initialization. The first hit's `flushMs`/`damageMs` therefore carries renderer activation and draw-call setup, kept in the hit total instead of being moved into setup or hidden in `applyDamageMs`.
- **Collision work is subtracted, reused and bounded.** Collision shapes are subtracted from and reused for the affected cells rather than rebuilding every collider; recompaction of the remaining cells is bounded per hit so one hit cannot trigger an unbounded whole-asset rebuild.
- **Connectivity is certified locally, with a full fallback.** A local connectivity certificate decides island/fragment status for the damaged neighborhood; boundary or ambiguous cases fall back to the full connectivity oracle instead of guessing, so bodies and fragments are neither fabricated nor missed.
- **HP expiry is sparse and quiet.** Damaged cells whose HP lapses expire sparsely without respawning geometry or rebuilding chunk meshes; expiry is separate from lethal-hit destruction and does not reset the asset.
- **Tiny-fragment rendering is OFF by default.** The optional render-only policy (`maxCells = 0`) applies only to **newly detached** fragments whose **occupied-cell count ≤ 4 AND full occupied world-bounds maximum dimension ≤ 2 world units**. Both conditions are required (not an OR, not a chunk-size approximation); intact or pre-existing geometry is unaffected and there is no age- or distance-based removal.

## Measurement groups kept separate

The analyzer keeps the optimized-runtime and evidence inputs separate and never pools them:

| Group | Default path | What it is |
|---|---|---|
| Matched VerifyAssets | `optimized-matched/run-0..4.json` | Five same-probe warm-cache runs of the same [VerifyAssets](<tools/MiningStationRound2/Verification.cs>) call as the baseline, written by [OptimizedMatch.cs](<tools/TerrainPerformance/OptimizedMatch.cs>). The only group compared with the baseline. |
| Complementary profiler | `optimized/report.json` | terrain-performance-2 measured-stage rows plus row/collection checks and the synthetic prewarm probe; legacy terrain-performance-1 is recognized but not gate-validated. Not a repetition of VerifyAssets. |
| Prewarm audit | `prewarm-audit.json` | Private-cache template/chunk/collision construction with no body instance; not a startup or FPS probe. |
| Allocation counter audit | `allocation-counter-audit.json` | Positive-control proof that the Editor allocation counter is unavailable; not a performance metric and never a CPU-scope blocker. |
| Matched warm-cache lease audit | `matched-cache-lease-audit.json` | Ten explicit source-template leases and restored user ownership; preparation/cleanup timing context, not a performance feature. |
| Functional tests | `tests/*.json` | Final accepted raw Unity CLI `run_tests` Editor responses; only the nested Summary counts and nested success are gated. |

Matched inputs may also be passed with `--matched`. A matched file also selected by `--optimized` is removed from that group and recorded in the summary, so no sample is counted twice. Complementary reports, when present, stay under `optimized.performanceProfiles`, `optimized.prewarmAudit` and `optimized.otherLayouts` in the summary and are never compared with the baseline.

## Evidence gate — final status

The final label is **SCOPED VALIDATED (CPU only)** only when every row below is met. It is never a map-assembly, gameplay, Player-build, real-time or FPS approval. Missing inputs stay absent rather than guessed.

| Gate | Input(s) | Observed | State |
|---|---|---|---|
| matched VerifyAssets | [run-0.json](<Documentation/TerrainPerformance-2026-11-01/optimized-matched/run-0.json>) · [run-1.json](<Documentation/TerrainPerformance-2026-11-01/optimized-matched/run-1.json>) … (6 files) | 5/5 present; passed=True; compatible=True; leases=10; ownershipRestored=True; rows=10 | met |
| functional tests | [voxel-editmode.json](<Documentation/TerrainPerformance-2026-11-01/tests/voxel-editmode.json>) | 1 report(s); passing=1; Total/Passed/Failed/Skipped/Inconclusive=93/93/0/0/0 | met |
| complementary profiler | [report.json](<Documentation/TerrainPerformance-2026-11-01/optimized/report.json>) | 1 profiler report(s); schema-validated=1 | met |
| prewarm audit | [prewarm-audit.json](<Documentation/TerrainPerformance-2026-11-01/prewarm-audit.json>) | 10 row(s) across 1 report(s) | met |
| protected hashes | [source-hash-audit.json](<Documentation/TerrainPerformance-2026-11-01/source-hash-audit.json>) | 48 snapshot files; comparison UNCHANGED (48 checked) | met |

**Scope: Editor only.** Every gate above is Unity Editor edit-mode evidence in CPU milliseconds; none of it is a Player build, frame time, FPS, speedup, gameplay or map-assembly approval.

## Functional tests — nested CLI gate

| Report | Mode | Total | Passed | Failed | Skipped | Inconclusive | Nested success | Gate | SHA-256 (first 12) |
|---|---:|---:|---:|---:|---:|---:|---|---|---|
| [voxel-editmode.json](<Documentation/TerrainPerformance-2026-11-01/tests/voxel-editmode.json>) | EditMode | 93 | 93 | 0 | 0 | 0 | true | pass | `A658E9B5D041` |

Test gate: **PASSED**; 1 of 1 report(s) pass. Totals across valid summaries: Total 93, Passed 93, Failed 0, Skipped 0, Inconclusive 0.

The outer `success` flag is recorded in the summary but is not sufficient: each report is gated on nested `data.result.Summary` counts, nested `data.result.success` and an EditMode run. Full SHA-256 values are retained in `tests.reports[].sha256`.

## Complementary profiler — terrain-performance-2 validation

| Report | Version | Passed | Verdict | Rows | Summaries | Cache probes | Prewarm rows | Probe cells | Alloc counter | Schema | SHA-256 (first 12) |
|---|---:|---|---:|---:|---:|---:|---:|---|---|---|---|
| [report.json](<Documentation/TerrainPerformance-2026-11-01/optimized/report.json>) | terrain-performance-2 | true | FUNCTIONAL CHECKS PASSED - operation CPU and … | 215 | 143 | 7 | 7 | 585 | false | validated | `B81A48FC9512` |

Profiler schema gate: **met**; 1 of 1 report(s) schema-validated. The check requires top-level passed/verdict/failures, contract/source/scene/physics/cache flags, non-empty rows/summaries/cache/prewarm, row quiet/conservation flags, cache activation flags, the synthetic prewarm probe, dataOnly, tiny and density. Legacy terrain-performance-1 is recognized and summarized but is not gate-validated.

Each measured operation stage keeps its own `stage` path in the summary; stages are never pooled into a frame budget or one silent total.

Editor coverage note: the lifecycle `OnDestroy` callback is invoked reflectively in EditMode; this does not cover Play-mode automatic destruction dispatch.

## Matched optimized VerifyAssets — five same-probe runs

| Run | Passed | Report duration ms | SHA-256 (first 12) |
|---|---:|---:|---|
| [0](<Documentation/TerrainPerformance-2026-11-01/optimized-matched/run-0.json>) | true | 1,431.168 | `2E774F914334` |
| [1](<Documentation/TerrainPerformance-2026-11-01/optimized-matched/run-1.json>) | true | 1,399.156 | `081654B657AA` |
| [2](<Documentation/TerrainPerformance-2026-11-01/optimized-matched/run-2.json>) | true | 1,416.671 | `861B45690AC1` |
| [3](<Documentation/TerrainPerformance-2026-11-01/optimized-matched/run-3.json>) | true | 1,435.560 | `24716562A1B2` |
| [4](<Documentation/TerrainPerformance-2026-11-01/optimized-matched/run-4.json>) | true | 1,498.160 | `FE8D7D5ACD57` |

Group status: **READY_FOR_REVIEW** — All five matched reports are present and passed; the separate test and benefit gates decide the final label.

### Matched warm-cache lease audit

Explicit warm leases: **10**; preparation **3,119.727 ms**; ownership restored: **true**; rows: **10**.

| Asset | Users before | Users after | Ownership restored |
|---|---:|---:|---|
| Debris_AngledShard | 0 | 0 | true |
| Debris_FracturedChunk | 0 | 0 | true |
| Rock_Faceted | 0 | 0 | true |
| Rock_Notched | 0 | 0 | true |
| Rock_Ridged | 0 | 0 | true |
| Station_CapsuleTank | 0 | 0 | true |
| Station_EquipmentCage | 0 | 0 | true |
| Station_NotchedSlab | 0 | 0 | true |
| Station_OpenTruss | 0 | 0 | true |
| Station_SphericalTank | 0 | 0 | true |

Lease preparation is **outside** the five warm VerifyAssets phase timings and is **not subtracted or removed**. Prewarm construction is measured separately by the prewarm audit. Cleanup is also outside phased timings, so whole-report duration includes different cleanup work from the preserved baseline; its delta is suppressed (raw per-group durations remain descriptive).


### Initialization and integrity (warm templates)

Pairs are **baseline → matched (matched − baseline)**. Counts are medians; they are not required to stay equal because the optimized runtime may legitimately change mounted meshes or colliders.

| Asset | Cells | Vertices | Colliders | First instance ms | Repeat instance ms |
|---|---:|---:|---:|---:|---:|
| [Debris_AngledShard](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Debris_AngledShard.asset>) | 288 → 288 (0) | 73,836 → 73,836 (0) | 36 → 36 (0) | 0.764 → 1.179 (+0.415) | 0.500 → 1.079 (+0.579) |
| [Debris_FracturedChunk](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Debris_FracturedChunk.asset>) | 516 → 516 (0) | 132,456 → 132,456 (0) | 56 → 56 (0) | 0.716 → 1.679 (+0.963) | 0.810 → 1.655 (+0.845) |
| [Rock_Faceted](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Faceted.asset>) | 883 → 883 (0) | 188,712 → 188,712 (0) | 67 → 67 (0) | 0.897 → 2.240 (+1.342) | 0.981 → 2.111 (+1.130) |
| [Rock_Notched](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Notched.asset>) | 2,047 → 2,047 (0) | 424,428 → 424,428 (0) | 117 → 117 (0) | 1.984 → 4.031 (+2.047) | 2.018 → 3.992 (+1.973) |
| [Rock_Ridged](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Ridged.asset>) | 1,892 → 1,892 (0) | 415,692 → 415,692 (0) | 125 → 125 (0) | 2.501 → 4.755 (+2.254) | 2.209 → 4.442 (+2.233) |
| [Station_CapsuleTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_CapsuleTank.asset>) | 5,517 → 5,517 (0) | 1,197,756 → 1,197,756 (0) | 42 → 42 (0) | 2.768 → 4.681 (+1.912) | 2.739 → 4.363 (+1.624) |
| [Station_EquipmentCage](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_EquipmentCage.asset>) | 1,872 → 1,872 (0) | 502,548 → 502,548 (0) | 270 → 270 (0) | 3.989 → 12.932 (+8.944) | 3.988 → 9.969 (+5.981) |
| [Station_NotchedSlab](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_NotchedSlab.asset>) | 3,995 → 3,995 (0) | 710,880 → 710,880 (0) | 4 → 4 (0) | 1.798 → 2.905 (+1.107) | 1.896 → 3.314 (+1.418) |
| [Station_OpenTruss](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_OpenTruss.asset>) | 2,403 → 2,403 (0) | 580,452 → 580,452 (0) | 356 → 356 (0) | 6.152 → 15.062 (+8.909) | 6.790 → 18.944 (+12.154) |
| [Station_SphericalTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_SphericalTank.asset>) | 1,659 → 1,659 (0) | 327,012 → 327,012 (0) | 74 → 74 (0) | 1.489 → 2.702 (+1.213) | 1.375 → 2.801 (+1.426) |

### First lethal hit (no sustained stream)

`damageMs = applyDamageMs + flushMs`; the total includes chunk renderer activation on that first hit. Do not replace it with `applyDamageMs` alone.

| Probe asset | Apply ms | Flush ms | Total damage ms | Colliders after | Fragments |
|---|---:|---:|---:|---:|---:|
| [Debris_FracturedChunk](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Debris_FracturedChunk.asset>) | 0.002 → 0.003 (+0.001) | 30.975 → 4.347 (-26.628) | 30.977 → 4.351 (-26.626) | 57 → 57 (0) | 0 → 0 (0) |
| [Rock_Notched](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Notched.asset>) | 0.004 → 0.009 (+0.005) | 84.017 → 8.454 (-75.564) | 84.021 → 8.463 (-75.558) | 116 → 118 (+2) | 0 → 0 (0) |
| [Station_CapsuleTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_CapsuleTank.asset>) | 0.004 → 0.010 (+0.005) | 262.905 → 24.891 (-238.014) | 262.909 → 24.899 (-238.009) | 43 → 43 (0) | 0 → 0 (0) |
| [Station_EquipmentCage](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_EquipmentCage.asset>) | 0.004 → 0.009 (+0.005) | 102.232 → 11.286 (-90.946) | 102.237 → 11.296 (-90.941) | 270 → 270 (0) | 0 → 0 (0) |
| [Station_NotchedSlab](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_NotchedSlab.asset>) | 0.004 → 0.008 (+0.005) | 142.528 → 17.181 (-125.347) | 142.531 → 17.190 (-125.341) | 5 → 5 (0) | 0 → 0 (0) |
| [Station_OpenTruss](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_OpenTruss.asset>) | 0.004 → 0.009 (+0.005) | 143.558 → 14.587 (-128.971) | 143.562 → 14.595 (-128.967) | 353 → 357 (+4) | 0 → 0 (0) |
| [Station_SphericalTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_SphericalTank.asset>) | 0.004 → 0.007 (+0.004) | 64.101 → 7.302 (-56.799) | 64.106 → 7.309 (-56.796) | 75 → 75 (0) | 0 → 0 (0) |

| Other matched phases | Baseline → matched (Δ) |
|---|---|
| Cross-section cut damage ms | 127.570 → 49.588 (-77.982) |
| Cross-section cut flush ms | 127.548 → 49.545 (-78.003) |
| Cross-section removed cells | 104 → 104 (0) |
| Density average init ms | 0.476 → 1.347 (+0.872) |
| Patch init total ms | 80.808 → 200.296 (+119.488) |
| Patch average step ms | 0.609 → 1.031 (+0.422) |
| Patch maximum step ms | 2.620 → 4.300 (+1.680) |

`n` per metric is retained in the JSON. Metrics measured by only one group appear under `metricCoverage` without a fabricated delta (for example new chunk-renderer instrumentation has no baseline counterpart).
Whole-report duration is **not differenced**: explicit warm-lease preparation/cleanup work differs from the preserved baseline. Raw per-group duration medians remain in the summary.

## Baseline provenance and limits

- 5 fresh unchanged-runtime runs; original [VerifyAssets](<tools/MiningStationRound2/Verification.cs>) invoked by [TerrainBaseline](<tools/TerrainPerformance/Baseline.cs>) against the unchanged v04 assets.
- All reports passed with no failures/unsupported sections: **true**.
- Unity 6000.3.25f1, WindowsEditor, 13th Gen Intel(R) Core(TM) i7-13700K; GPU NVIDIA GeForce RTX 5080. Editor edit mode, not a Player build.
- All timings below are **CPU milliseconds**, shown as **median (minimum–maximum)** across five separate probe runs in the same Editor session. They are not rendered frame times or FPS. No outliers were discarded.
- Template caches were warm in these runs. `firstBuildMs` is the first instance in that probe, **not** cold initialization; per-asset cache flags are retained in the summary.
- Damage is one lethal surface-cell hit on each fresh representative instance plus a separate cut probe. This is not sustained fire or full-station combat.

Raw reports: [run-0.json](<Documentation/TerrainPerformance-2026-11-01/baseline/run-0.json>) · [run-1.json](<Documentation/TerrainPerformance-2026-11-01/baseline/run-1.json>) · [run-2.json](<Documentation/TerrainPerformance-2026-11-01/baseline/run-2.json>) · [run-3.json](<Documentation/TerrainPerformance-2026-11-01/baseline/run-3.json>) · [run-4.json](<Documentation/TerrainPerformance-2026-11-01/baseline/run-4.json>).

## Initialization — reported separately from damage

Cold template prewarm/build: **not measured by VerifyAssets, baseline or matched**. The separate prewarm audit measures a fresh private template cache; it is not a cold VerifyAssets baseline and no startup FPS follows from it.

| Asset | Cells | Vertices | Colliders | First instance ms | Repeat instance ms |
|---|---:|---:|---:|---:|---:|
| [Debris_AngledShard](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Debris_AngledShard.asset>) | 288 | 73,836 | 36 | 0.764 (0.642–1.131) | 0.500 (0.427–0.710) |
| [Debris_FracturedChunk](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Debris_FracturedChunk.asset>) | 516 | 132,456 | 56 | 0.716 (0.703–1.004) | 0.810 (0.630–1.000) |
| [Rock_Faceted](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Faceted.asset>) | 883 | 188,712 | 67 | 0.897 (0.870–1.348) | 0.981 (0.917–1.388) |
| [Rock_Notched](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Notched.asset>) | 2,047 | 424,428 | 117 | 1.984 (1.905–2.850) | 2.018 (1.950–2.982) |
| [Rock_Ridged](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Ridged.asset>) | 1,892 | 415,692 | 125 | 2.501 (1.819–2.977) | 2.209 (1.957–2.805) |
| [Station_CapsuleTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_CapsuleTank.asset>) | 5,517 | 1,197,756 | 42 | 2.768 (2.573–3.529) | 2.739 (2.729–3.716) |
| [Station_EquipmentCage](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_EquipmentCage.asset>) | 1,872 | 502,548 | 270 | 3.989 (3.942–6.209) | 3.988 (3.836–6.403) |
| [Station_NotchedSlab](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_NotchedSlab.asset>) | 3,995 | 710,880 | 4 | 1.798 (1.699–2.474) | 1.896 (1.669–2.242) |
| [Station_OpenTruss](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_OpenTruss.asset>) | 2,403 | 580,452 | 356 | 6.152 (5.861–6.582) | 6.790 (6.348–7.572) |
| [Station_SphericalTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_SphericalTank.asset>) | 1,659 | 327,012 | 74 | 1.489 (1.359–1.911) | 1.375 (1.245–1.825) |

## First lethal hit — no sustained stream

`damageMs = applyDamageMs + flushMs`. Flush includes destruction work; do not substitute only the tiny apply-call cost for the total hit cost.

| Probe asset | Apply ms | Flush ms | Total damage ms |
|---|---:|---:|---:|
| [Debris_FracturedChunk](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Debris_FracturedChunk.asset>) | 0.002 (0.002–0.004) | 30.975 (25.782–158.217) | 30.977 (25.784–158.221) |
| [Rock_Notched](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Rock_Notched.asset>) | 0.004 (0.003–0.004) | 84.017 (82.108–279.277) | 84.021 (82.112–279.280) |
| [Station_CapsuleTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_CapsuleTank.asset>) | 0.004 (0.004–0.007) | 262.905 (257.039–302.711) | 262.909 (257.043–302.718) |
| [Station_EquipmentCage](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_EquipmentCage.asset>) | 0.004 (0.004–0.005) | 102.232 (101.431–324.570) | 102.237 (101.436–324.574) |
| [Station_NotchedSlab](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_NotchedSlab.asset>) | 0.004 (0.003–0.004) | 142.528 (137.483–158.329) | 142.531 (137.487–158.332) |
| [Station_OpenTruss](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_OpenTruss.asset>) | 0.004 (0.003–0.004) | 143.558 (133.531–151.072) | 143.562 (133.535–151.076) |
| [Station_SphericalTank](<Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets/Station_SphericalTank.asset>) | 0.004 (0.004–0.005) | 64.101 (62.328–291.190) | 64.106 (62.332–291.195) |

Separate OpenTruss cross-section cut: 104 removed cells; 2,299 surviving cells; 2 islands / 2 bodies / 1 newly created fragment. Total damage ms: **127.570 (118.182–137.952)**; flush ms: 127.548 (118.162–137.933).

**Sustained hits: not measured in the original baseline.** Repeating independent first-hit probes is not a sustained-hit benchmark.

## Warm density initialization and isolated physics

- Eight shared-template debris instances: average initialization ms **0.476 (0.426–0.644)** per instance.
- Whole local density patch: 56 bodies, 77,657 cells, 5,112 colliders, 5 shared meshes.
- Patch total warm initialization ms: **80.808 (77.808–113.281)**; first-copy-per-asset subtotal: 6.405 (5.945–7.077) (already warm templates); remaining-copy subtotal: 73.526 (71.686–106.962).
- 120 isolated PhysX steps per run at 1/60 s: per-run average CPU ms/step **0.609 (0.539–0.645)**; per-run maximum step CPU ms **2.620 (2.113–3.282)**. The latter is a distribution of five run maxima, not raw-step percentiles.
- NaN count: 0; out-of-bounds bodies: 0; new world-AABB overlap pairs: 0. Initial/end world-AABB overlap pairs: 2/2 (coarse AABB checks, not an exact no-contact proof).
- No rendering, AI, gameplay, continued fire or complete station is included in that physics probe.

## Prewarm memory and first-hit renderer cost — kept explicit

- **Prewarm extra native mesh memory.** The prewarm audit clones each source into a fresh private cache key. `aggregateUnityReportedBytes` (Unity-reported aggregate template mesh) and `chunkMeshStreamLowerBoundBytes` (estimated chunk mesh stream) are different native measurements: they are not an exact total, not additive with managed allocation bytes (withheld here when the counter is unsupported), and not part of any damage millisecond. The v2 profiler's prewarm rows likewise estimate `aggregateEstimatedBytes`, `chunkEstimatedBytes` and their `combinedEstimatedBytes`; those are mesh-stream estimates, not an exact native total. Prewarm creates no renderer, so it never measures draw calls.
- **First-hit renderer activation.** Creating chunk renderer objects, uploading chunk meshes and setting up draw calls on the first lethal hit is charged to that hit's `flushMs`/`damageMs`. A separate activation field, if a report adds one, is a breakdown of the same hit — not a cost that may be removed from the hit total.
- **Warm templates are not cold startup.** Every VerifyAssets run, baseline or matched, starts with a warm template cache; `firstBuildMs` is the first instance inside that probe. Cold prewarm exists only in the separate audit. When that audit is present, its values are in the summary under `optimized.prewarmAudit` and are never folded into a damage or FPS number.

## Allocation counter — unavailable, not zero

- Positive control allocated **1,048,576 bytes**; `GC.GetAllocatedBytesForCurrentThread()` observed **0** while Mono used heap changed by **1,052,672 bytes**. The counter is unsupported on this Editor.

- `allocatedBytes`, `allocatedManagedBytes`, `medianAllocatedBytes`, idle-allocation fields and GC generation counts are suppressed unless the positive control proves the counter works. Sentinels (`-1`) are never reported as medians, and a raw `0` is not treated as a measurement while unavailable.
- Raw pre-fix prewarm zeros are preserved under Temp for audit only and are not evidence. The final guarded prewarm records `allocationCounterAvailable=false` with explicit `-1` row sentinels.
- Prewarm allocation state: **unavailable** (report.supported=false); profiler allocation state: **unavailable** (report.allocation.supported=false).
- Native/Unity mesh memory and chunk-stream estimates (`aggregateUnityReportedBytes`, `chunkMeshStreamLowerBoundBytes`, v2 `aggregateEstimatedBytes`, `chunkEstimatedBytes`) remain distinct reported measurements; they are not managed allocations and are never added to them.
- The CPU evidence gate does not depend on the allocation counter; unsupported allocation telemetry cannot block the Editor CPU label, and it is never replaced by zero.

## Hash protection

The initial snapshot was captured at 2026-11-01T10:17:28.926116+00:00 before this analyzer agent's first file edit. 10 original source files match the historical [source-hash manifest](<Temp/MiningRound2/source-hashes-before.json>); the immutable snapshot also covers 29 generated-v04 asset/layout/meta files and 9 preserved v04/baseline evidence files, 48 files in total.
Latest read-only comparison at 2026-11-01T11:50:07.445348+00:00: **UNCHANGED**, 48 checked files. This proves preservation at that check only, not a future state.

The immutable snapshot is not extended with the matched or test reports; their SHA-256 values are recorded in the summary (`matchedVerifyAssets.verify.reports` and `tests.reports`), so this 48-file snapshot still describes only the pre-fix state.

## Remaining gates and standing constraints

All evidence gates above are met. The SCOPED VALIDATED label still covers only the listed Editor CPU evidence, not map assembly or FPS.

- Record functional gates separately: HP/reset semantics, cell conservation, exact cuboid collision coverage, local connectivity certificate plus full-oracle fallback, and the tiny-fragment policy conditions.
- Confirm the lazy chunk-render activation, the retained single root renderer for intact assets, subtracted/reused collision shapes with bounded recompaction, and quiet sparse HP expiry without geometry respawn.
- Keep tiny-fragment rendering **OFF by default (`maxCells = 0`)**. If it is ever enabled, require occupied-cell count ≤ 4 **and** full occupied world-bounds maximum dimension ≤ 2 world units for **newly detached** fragments only; no age/distance removal.
- Keep prewarm native mesh memory and first-hit renderer activation/draw-call cost explicit; do not net them out of damage ms.
- Recheck protected hashes after implementation and measurements. No map assembly is authorized by this document.

## Reproduce analysis without Unity

Run from the project root (standard-library Python only):

```powershell
python tools/TerrainPerformance/Summarize.py --self-test
python tools/TerrainPerformance/Summarize.py
# The five matched runs and tests/*.json are discovered when present:
python tools/TerrainPerformance/Summarize.py --matched Documentation/TerrainPerformance-2026-11-01/optimized-matched
python tools/TerrainPerformance/Summarize.py --tests Documentation/TerrainPerformance-2026-11-01/tests
# Allocation-counter positive-control audit (also auto-discovered):
python tools/TerrainPerformance/Summarize.py --allocation-audit Documentation/TerrainPerformance-2026-11-01/allocation-counter-audit.json
# Matched warm-cache lease audit (also auto-discovered):
python tools/TerrainPerformance/Summarize.py --lease-audit Documentation/TerrainPerformance-2026-11-01/matched-cache-lease-audit.json
# Complementary profiler and prewarm inputs are separate groups; never mix them with matched:
python tools/TerrainPerformance/Summarize.py --optimized Documentation/TerrainPerformance-2026-11-01/optimized
python tools/TerrainPerformance/Summarize.py --optimized Documentation/TerrainPerformance-2026-11-01/prewarm-audit.json
# Recheck hashes without regenerating the summary or README.
python tools/TerrainPerformance/Summarize.py --audit-only
```

Running the analyzer refreshes only this README, the summary and the latest audit comparison; it never resets the initial hash snapshot. Missing metrics stay absent, not zero; no cold baseline, frame time or FPS is inferred. Final status becomes SCOPED VALIDATED (CPU only) only when every evidence gate is met.
