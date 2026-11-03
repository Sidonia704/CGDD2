// Terrain-only, ephemeral Pipeline profiler. Bundle with Bundle.py --performance.
// This file is outside Assets: it neither imports scripts nor changes saved content.
// Complementary to VerifyAssets: it measures idle/nonlethal/expiry, the first lethal-hit
// chunk activation, warm local and sustained hits, the optional tiny policy, dataOnly previews
// and the actual prewarm resource overhead. It does not replace the original five baseline runs.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Flight;
using Starframe.Maps;
using Object = UnityEngine.Object;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        const BindingFlags PerfFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly string[] PerfMetricNames = { "ConnectivityMs", "GeometryMs", "ColliderMs", "MassMs", "GeometryBuildCount", "ConnectivityPassCount", "ColliderBuildCount" };
        static readonly Func<long> PerfAllocated = PerfAllocationReader();
        const string PerfModuleDatabase = "Assets/_Starframe/Settings/Modules/ModuleDatabase.asset";
        const string PerfDensityLayout = "Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Layouts/LocalDensityPatch.asset";
        // Saved empty additive fixture; an untitled TestRunner-like editor session cannot create one reliably.
        const string PerfStagingScene = "Assets/_Starframe/Tests/Construction/Fixtures/TerrainTestStaging.unity";
        static bool PerfAllocationCounterSupported;
        static bool PerfStagingPreexisting;

        [Serializable] public sealed class TerrainProfileReport
        {
            public string version = "terrain-performance-2", generatedUtc, verdict, outputDirectory;
            public string measurement = "Editor edit-mode CPU milliseconds only; not frames/FPS/GPU/audio/AI. Initialization, in-memory prewarm, first lethal hit, warm local hit and sustained sequential hits are separate stages. Non-preview instances keep one Rigidbody and enabled compound BoxColliders, but the profiler never calls Physics.Simulate: no automatic physics, contacts or gravity are measured. Managed allocatedBytes are reported only when a positive control verifies GC.GetAllocatedBytesForCurrentThread; otherwise every allocatedBytes field is the -1 sentinel and no allocation claim is made. Native mesh/PhysX/GPU memory is not measured and must not be added. First-hit flush totals include full first renderer activation (chunk GameObjects/MeshRenderers created inside the timed call) and are not split into hidden sub-phases.";
            public int measuredRuns, discardedWarmups, sustainedHits, warmTemplateLeases;
            public bool passed, sceneRestored, globalPhysicsUntouched, templateCachePreserved, templateCacheRestored;
            public TerrainAllocationProbe allocation;
            public TerrainRuntimeContract contract;
            public VerifyEnvironment environment;
            public VerifySourceIntegrity source;
            public List<string> failures = new List<string>(), notes = new List<string>(), unsupported = new List<string>();
            public List<TerrainFileAudit> files = new List<TerrainFileAudit>();
            public List<TerrainProfileRow> rows = new List<TerrainProfileRow>();
            public List<TerrainProfileSummary> summaries = new List<TerrainProfileSummary>();
            public List<TerrainCacheProbe> cache = new List<TerrainCacheProbe>();
            public List<TerrainPrewarmInfo> prewarm = new List<TerrainPrewarmInfo>();
            public TerrainPrewarmProbe prewarmProbe;
            public TerrainDataOnlyProbe dataOnly;
            public TerrainTinyProbe tiny;
            public TerrainDensityProbe density;
        }
        [Serializable] public sealed class TerrainAllocationProbe
        {
            public string method = "GC.GetAllocatedBytesForCurrentThread", note;
            public bool methodAvailable, supported;
            public int positiveControlBytes = 1024 * 1024, attempts;
            public long firstBefore = -1, firstAfter = -1, firstDelta = -1, maxDelta = -1, heapBefore = -1, heapAfter = -1;
        }
        [Serializable] public sealed class TerrainRuntimeContract
        {
            public int chunkEdge, minimumCells, defaultExecutionOrder, defaultTinyFragmentMaxCells, idleCellVisitsConstant;
            public float defaultTinyFragmentMaxWorldExtent;
            public bool matchesContract, flushBeforeShips;
        }
        [Serializable] public sealed class TerrainFileAudit { public string path, before, after; public bool unchanged; }
        [Serializable] public sealed class TerrainPrewarmInfo
        {
            public string asset, cacheState;
            public int cells, chunks, chunkVertices, aggregateVertices, intactMountedMeshes, chunkUsers, templateUsers;
            public long chunkEstimatedBytes, aggregateEstimatedBytes, combinedEstimatedBytes;
            public double prewarmMs, overheadX, intactInitializeMs, repeatInitializeMs;
            public bool captured, chunkSupported, templateExistedBeforeInitialize, retainedForRun;
        }
        [Serializable] public sealed class TerrainCacheProbe
        {
            public string asset, note, cacheState;
            public int cells, intactMeshesPerInstance, sharedMeshCount, childRenderersBeforeHit, mountedMeshesAfterHit, renderersBefore, renderersAfter;
            public bool chunkSupported, sharedTemplates, aggregateShared, intactSingleRootRenderer, chunksActivateOnFirstLethal, peerIntactAfterHit, templateRetainedForRun, firstLethalIncludesRendererActivation;
            public double firstInstanceInitMs, repeatInstanceInitMs, firstLethalTotalMs;
        }
        [Serializable] public sealed class TerrainPrewarmProbe
        {
            public string note = "A fresh synthetic hull asset is initialized once to time in-memory SharedTemplate construction and its persistent chunk resources. This is not a cold disk/asset-import measurement and it does not replace VerifyAssets. firstLethalFlushMs includes creating every first-time chunk renderer/binding inside the timed call; managed allocation bytes are not native/GPU memory.";
            public int cells, chunks;
            public double initializeMs, repeatInitializeMs, firstLethalApplyMs, firstLethalFlushMs, firstLethalTotalMs, warmLocalApplyMs, warmLocalFlushMs, warmLocalTotalMs;
            public TerrainPrewarmInfo first, second;
            public string firstTarget, secondTarget, firstHitChangedChunks, secondHitChangedChunks, secondHitInvalidatedChunks, peerHitChangedChunks;
            public int templateCacheRawBefore = -1, templateCacheLiveBefore = -1, templateCacheDeadBefore = -1;
            public int templateCacheRawAfter = -1, templateCacheLiveAfter = -1, templateCacheDeadAfter = -1;
            public int sharedChunkTemplatesRawBefore = -1, sharedChunkTemplatesRawAfter = -1;
            public bool syntheticTemplateAbsentAfterInstances, syntheticSharedChunkAbsentAfterInstances, syntheticTemplateUsersZero, syntheticSharedChunkUsersZero;
            public int syntheticTemplateUsersAfterInstances = -1, syntheticSharedChunkUsersAfterInstances = -1;
            public bool secondHitChangedWithinInvalidated;
            public int renderersBeforeHit, renderersAfterHit, renderersAfterSecondHit, mountedMeshesBeforeHit, mountedMeshesAfterHit, mountedVerticesAfterHit, mountedMeshesAfterSecondHit, mountedVerticesAfterSecondHit, peerMountedMeshesAfterHit;
            public int warmHitKeptMeshes, warmHitReplacedMeshes, sharedMountedMeshes, expectedSharedMountedMeshes;
            public bool reusedOnSecondInstance, intactSingleRootRenderer, noChunkObjectsBeforeHit, chunksActivateOnFirstLethal, chunkCountMatchesMountedMeshes, rootAggregateHidden, peerStayedIntact, prewarmedResourcesReused, cacheRestored;
        }
        [Serializable] public sealed class TerrainDataOnlyProbe
        {
            public string note = "dataOnly previews retain the hull material palette for editor drawing but must not acquire VoxelTemplateCache, shared chunks, mounted meshes, colliders or a Rigidbody.";
            public bool supported, dataOnly, renderCacheAbsent, renderMeshesEmpty, noTemplate, noChunks, noMountedMesh, rendererDisabled, paletteRetained, noColliders, noRigidbody, cacheStillAbsentAfterDamage;
            public int cacheEntriesBefore, cacheEntriesAfter, sharedChunkTemplatesBefore, sharedChunkTemplatesAfter, aliveBefore, aliveAfter, removed, paletteMaterials;
            public double initializeMs;
        }
        [Serializable] public sealed class TerrainTinyProbe
        {
            public string note = "Default tinyFragmentMaxCells is 0. When this runtime admits render-only fragments in EditMode, the exact <=4-cell AND full occupied world-bounds extent <=2 policy is measured; otherwise only the default-off/above-threshold physical behaviour is claimed and the eligible branch is reported as unsupported.";
            public bool apiPresent, defaultOff, defaultPhysical, optInStored, renderOnlyAdmitted, smallOptInVisualOnly, retainedLargePhysical, extentBoundaryVisualOnly, extentAboveBoundaryPhysical, conserved;
            public int cellsBefore, pieces, physicalAfter, visualAfter, visualManagers, collidersAfter, activeColliders, pooledColliders;
            public int smallOptInPieces, smallOptInVisualCells, smallOptInPhysicalCells, extentVisualCells, aboveExtentVisualCells;
            public int retainedPieces, retainedPhysicalCells, retainedVisualCells, retainedVisualFragments, aboveExtentPieces, aboveExtentPhysicalCells;
        }
        [Serializable] public sealed class TerrainDensityProbe
        {
            public string note = "Whole LocalDensityPatch instantiated in the isolated preview scene. Counts are actual mounted MeshFilters/renderers/vertices after activation-free construction; renderers are not measured GPU draw calls.";
            public int instances;
            public double initializeMs;
            public TerrainRenderMetrics render;
        }
        [Serializable] public sealed class TerrainProfileRow
        {
            public string asset, role, scenario, maintenancePath;
            public int run, initialCells, removedCells, expectedSurvivors, requestedHits, skippedVisualOnlyTargets;
            public bool chunkSupported, conserved, uniquePhysicalOwners, noRebuild, healthRestored, diagnosticsEnabled, physicsBodiesMatchRuntimes, intactSingleRootRenderer, runtimeEnabledAfter, firstHitIncludesRendererActivation;
            public double initializeMs, firstHitTotalMs;
            public int mountedMeshesAfter, mountedVerticesAfter, uniqueMountedMeshesAfter, chunkMeshesAfter, drawUpperBoundAfter, piecesAfter, warmHitMountedMeshes, warmHitKeptMeshes, renderersBefore, renderersAfter, activeCollidersAfter, pooledCollidersAfter;
            public List<string> targets = new List<string>();
            public List<TerrainOperation> operations = new List<TerrainOperation>();
            public TerrainPrewarmInfo prewarm;
            public TerrainRenderMetrics intact, activated, before, after;
            public List<TerrainMetric> diagnostics = new List<TerrainMetric>();
        }
        [Serializable] public sealed class TerrainOperation
        {
            public string stage;
            public int operations;
            public double cpuMs;
            public long allocatedBytes = -1;
            public int gen0, gen1, gen2;
        }
        [Serializable] public sealed class TerrainMetric { public string name; public bool available; public double delta; }
        [Serializable] public sealed class TerrainRenderMetrics
        {
            public int runtimes, bodies, enabledBoxes, disabledBoxes, colliders, activeColliderComponents, pooledColliderComponents, meshFilters, renderers, activeRenderers, uniqueMeshes, gameObjects;
            public int rootMeshFilters, chunkMeshFilters, mountedChunkMeshes, rootRenderers, activeRootRenderers, chunkRenderers, activeChunkRenderers, rendererDrawUpperBound;
            public long instancedVertices, uniqueMeshVertices, indices;
            public int physicalAliveCells, detachedVisualCells, detachedVisualFragments;
            public bool chunksActive, rootAggregateVisible, visualCountersAvailable;
            public int visualManagers, visualLiveCells, visualFragments, visualOwnedMeshes, visualOwnedMaterials, visualSubmittedDraws;
            public long visualMeshVertices = -1;
            public string visualMeshLimit = "Procedural manager draws have no MeshFilter. Owned mesh/material counts are reported once per manager; private mesh vertex data is not exposed and is NOT folded into mounted chunk vertices.";
            public string visualConservation = "Physical cells and per-runtime DetachedVisualCells are counted once per runtime. VoxelDetachedVisualFragments.CellCount/FragmentCount are manager-wide and counted once per manager, never multiplied by the runtime count.";
            public string physicsNote = "Non-preview EditMode instances own one Rigidbody and enabled active compound BoxColliders. VoxelTerrainColliders may retain unused pooled components disabled after compaction: activeColliderComponents is the runtime ColliderCount / active physical volume, pooledColliderComponents are disabled retained components and are NOT physical colliders. The profiler never calls Physics.Simulate, so this is not a physics, contact, gravity or gameplay measurement.";
            public string drawsNote = "rendererDrawUpperBound = sum of active MeshRenderer material slots, an upper bound on MeshRenderer draw calls. Real GPU draw calls/batching are not measurable in Editor edit mode. Visual-fragment draws are submitted by LateUpdate, which the Editor does not run here, so SubmittedDrawCount stays 0 and is not invented.";
            public string allocationNote = "allocatedBytes are current-thread managed allocations only and are valid only when report.allocation.supported is true; otherwise they are the -1 sentinel and must not be interpreted as zero. Native mesh/PhysX/GPU memory is not measured and is never added to managed bytes.";
            public string hitTotalNote = "First-hit and flush CPU totals include full first renderer activation: chunk GameObjects, MeshFilters, MeshRenderers, materials and bindings are created inside the timed call. They are not split into hidden sub-phases.";
        }
        [Serializable] public sealed class TerrainProfileSummary
        {
            public string asset, scenario, stage;
            public int samples, allocationSamples;
            public bool allocationAvailable;
            public double medianMs, minMs, maxMs, p95Ms, medianAllocatedBytes;
        }
        sealed class TerrainSignature
        {
            public int body, revision;
            public float mass;
            public List<int> meshes = new List<int>(), colliders = new List<int>();
        }
        sealed class TerrainSceneStamp { public int handle; public bool dirty; public string path; public List<int> roots; }

        public static string TerrainPerformance()
        {
            return TerrainPerformanceAt("Documentation/TerrainPerformance-2026-11-01/optimized", 5, 1, 16);
        }

        public static string TerrainPerformanceAt(string outputDirectory, int measuredRuns, int warmupRuns, int sustainedHits)
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("TerrainPerformance requires EditMode; PlayMode is refused.");
            if (measuredRuns < 1 || measuredRuns > 20 || warmupRuns < 0 || warmupRuns > 5 || sustainedHits < 1 || sustainedHits > 256)
                throw new ArgumentOutOfRangeException("Use 1..20 runs, 0..5 warmups and 1..256 sequential hits.");
            string output = VerifyFullPath(outputDirectory);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("No-overwrite: output already exists: " + output);
            Directory.CreateDirectory(output);
            var report = new TerrainProfileReport { generatedUtc = DateTime.UtcNow.ToString("o"), outputDirectory = output, measuredRuns = measuredRuns, discardedWarmups = warmupRuns, sustainedHits = sustainedHits, environment = VerifyCaptureEnvironment() };
            // Positive control before any measured operation: Unity/Mono can return a constant 0 from
            // GC.GetAllocatedBytesForCurrentThread, which must never be reported as "zero allocations".
            report.allocation = PerfAllocationProbe();
            PerfAllocationCounterSupported = report.allocation.supported;
            if (report.allocation.supported) report.notes.Add(report.allocation.note);
            else report.unsupported.Add(report.allocation.note);
            var originalActive = SceneManager.GetActiveScene();
            var scenesBefore = PerfSceneStamps();
            var gravityBefore = Physics.gravity;
            var modeBefore = Physics.simulationMode;
            float fixedBefore = Time.fixedDeltaTime;
            var assets = new List<VoxelAssetDefinition>(); var paths = new List<string>();
            var roots = new List<GameObject>(); var cached = new List<VoxelAssetDefinition>();
            var leases = new List<VoxelTemplateCache.Template>();
            bool cacheReadable = VerifyTryReadTemplateCache(cached);
            PerfStagingPreexisting = false;
            var sourceReport = new VerifyReport(); sourceReport.failures = report.failures;
            VerifySourceSnapshot sourceBefore = null; VerifyScene isolated = null;
            const string assetFolder = "Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets";
            try
            {
                VerifyLoadAssets(assetFolder, assets, paths, report.notes, report.failures);
                if (assets.Count == 0) throw new InvalidOperationException("No v04 assets found.");
                sourceBefore = VerifyCaptureSource(assets, paths, assetFolder);
                PerfCaptureFiles(report.files, paths);
                isolated = PerfCreateScene(report.unsupported, report.notes);
                // Never accept the additive fallback: it shares default PhysicsScene.
                if (isolated == null || !isolated.valid || !isolated.previewPhysics || !isolated.localPhysics || SceneManager.GetActiveScene() != isolated.staging)
                    throw new InvalidOperationException("Isolated preview PhysicsScene + active staging scene are required; default physics is never simulated.");
                report.notes.Add("Uses Verification loading/hash/cleanup helpers plus a profiler-specific isolated scene setup; it does NOT call VerifyAssets or reuse root-MeshFilter metrics. Physics is never stepped by this profiler.");
                report.notes.Add("Phase timings are cumulative runtime diagnostic deltas or direct measurement. Missing metrics stay unavailable; they are never inferred from total Flush time or replaced with zero.");
                report.notes.Add("Setup, target selection, diagnostics reflection, scene traversal, conservation and destruction are OUTSIDE timed windows. Managed allocatedBytes are measured only when report.allocation.supported is true; otherwise every allocatedBytes value is the -1 sentinel and no zero-allocation claim is made. No GC.Collect and no global template-cache clear.");
                report.notes.Add("Representative templates are kept warm between probes by explicit VoxelTemplateCache leases. Initialization rows are therefore warm-cache, matching the baseline's warm-template limitation; the separate prewarm probe reports the fresh construction cost.");
                report.notes.Add("Prewarm resource overhead is reported per asset: prewarmed chunk count, total chunk vertices, estimated generated mesh bytes and the intact aggregate mesh size. Chunk meshes coexist with the intact aggregate until the first lethal hit activates them.");
                report.notes.Add("Non-preview EditMode instances keep the original physical contract: one Rigidbody and enabled ACTIVE compound BoxColliders. VoxelTerrainColliders intentionally retains unused pooled components disabled after compaction; they are reported separately and are not part of the active collision volume. The profiler never calls Physics.Simulate and the Editor does not step physics here, so no contact, gravity or gameplay physics is measured. This is not a frame, FPS, GPU, audio or AI measurement.");
                report.notes.Add("Per-runtime DetachedVisualCells/DetachedVisualFragmentCount are counted once per runtime. VoxelDetachedVisualFragments.CellCount/FragmentCount are manager-wide and counted once per manager. Render-only admission requires a non-preview runtime, so only the tiny probe opts in.");
                report.notes.Add("Collider components are split into activeColliderComponents (runtime ColliderCount, the physical volume) and pooledColliderComponents (disabled retained pool entries after compaction, never physical). Total component count is not a physics budget.");
                report.notes.Add("IdleCellVisits is a contractual constant 0, not an observed per-cell visit counter. Idle/expiry no-rebuild evidence is runtime.enabled=false plus unchanged geometry/collider/connectivity build counters, mesh/collider identity and RenderRevision.");
                report.notes.Add("Stage CPU times are separate synchronous phases (apply, flush, maintenance, activation) and are not additive into a frame budget. When verified, managed allocation bytes exclude native memory; when unsupported they are the -1 sentinel.");
                report.notes.Add("This report is complementary to the preserved five baseline VerifyAssets runs. It does not compute speedups against them: only an identical VerifyAssets probe may be compared directly with that baseline; idle/nonlethal/prewarm/sustained rows here are different probes.");
                report.notes.Add("First-hit flush totals include full first renderer activation (chunk GameObjects/MeshRenderers/bindings created inside the timed call). allocatedBytes are managed allocations only when report.allocation.supported; native mesh/PhysX/GPU memory is not measured and the two must not be added.");
                report.contract = PerfContract(isolated, roots, report);
                var indices = new List<int>(); var roles = new List<string>();
                VerifyPickRepresentatives(assets, indices, roles, report.notes);
                for (int n = 0; n < indices.Count; n++) PerfCacheCheck(isolated, roots, assets[indices[n]], report, leases);
                report.warmTemplateLeases = leases.Count;
                for (int run = -warmupRuns; run < measuredRuns; run++)
                {
                    var measured = new List<TerrainProfileRow>();
                    for (int n = 0; n < indices.Count; n++)
                    {
                        var asset = assets[indices[n]];
                        foreach (string scenario in new[] { "idle-1024", "nonlethal-expiry", "single-lethal", "second-local-lethal", "batch-lethal", "sequential-lethal" })
                        {
                            var row = PerfProbe(isolated, roots, asset, roles[n], scenario, run, sustainedHits, report);
                            if (run >= 0) { report.rows.Add(row); measured.Add(row); }
                        }
                    }
                    string selection;
                    var cutAsset = VerifyPickCutAsset(assets, out selection);
                    if (cutAsset)
                    {
                        var row = PerfProbe(isolated, roots, cutAsset, "real-truss-cut: " + selection, "cross-section-cut", run, sustainedHits, report);
                        if (run >= 0) { report.rows.Add(row); measured.Add(row); }
                    }
                    if (run >= 0) PerfWriteNew(Path.Combine(output, "run-" + run + ".json"), Json(measured, true));
                }
                report.prewarmProbe = PerfPrewarmProbe(isolated, roots, report);
                report.dataOnly = PerfDataOnly(isolated, roots, report);
                report.tiny = PerfTinyCheck(isolated, roots, report);
                report.density = PerfDensity(isolated, roots, assets, report);
            }
            catch (Exception e) { report.failures.Add(e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
            finally
            {
                try { PerfDestroy(roots); } catch (Exception e) { report.failures.Add("Root cleanup: " + e.Message); }
                PerfReleaseLeases(leases, report);
                // If the staging fixture was already open before the profiler, leave it open: only close what we opened.
                if (PerfStagingPreexisting && isolated != null)
                {
                    isolated.staging = default(Scene);
                    if (!isolated.previewPhysics) isolated.scene = default(Scene);
                }
                VerifyCloseScene(isolated, report.notes);
                VerifyRestoreScene(originalActive);
                if (sourceBefore != null)
                {
                    try { VerifyCompareSource(sourceReport, sourceBefore, assets, paths, assetFolder, cached, cacheReadable); report.source = sourceReport.source; }
                    catch (Exception e) { report.failures.Add("Source audit failed: " + e.Message); }
                }
                foreach (var file in report.files) { file.after = VerifyFileHash(file.path); file.unchanged = file.before == file.after; if (!file.unchanged) report.failures.Add("Source file changed: " + file.path); }
                var afterCache = new List<VoxelAssetDefinition>();
                bool afterReadable = cacheReadable && VerifyTryReadTemplateCache(afterCache);
                var afterSet = new HashSet<VoxelAssetDefinition>(afterCache);
                var cachedSet = new HashSet<VoxelAssetDefinition>(cached);
                report.templateCachePreserved = afterReadable && cached.All(a => afterSet.Contains(a));
                report.templateCacheRestored = afterReadable && afterSet.SetEquals(cachedSet);
                if (cacheReadable && !report.templateCachePreserved) report.failures.Add("An existing template cache entry disappeared.");
                if (cacheReadable && !report.templateCacheRestored) report.failures.Add("Template cache key set was not restored after the profiler released its leases.");
                if (!cacheReadable) report.unsupported.Add("Template cache preservation could not be inspected.");
                report.sceneRestored = SceneManager.GetActiveScene().handle == originalActive.handle && PerfSameScenes(scenesBefore, PerfSceneStamps());
                report.globalPhysicsUntouched = Physics.simulationMode == modeBefore && Physics.gravity == gravityBefore && Time.fixedDeltaTime == fixedBefore;
                if (!report.sceneRestored) report.failures.Add("Open scene roots/dirty flags/active scene were not preserved.");
                if (!report.globalPhysicsUntouched) report.failures.Add("Global physics settings changed (profiler does not restore or write them).");
            }
            PerfSummarize(report);
            report.passed = report.failures.Count == 0 && report.rows.Count > 0 && report.source != null && report.source.integrityPassed
                && report.contract != null && report.contract.matchesContract && report.prewarmProbe != null && report.dataOnly != null && report.tiny != null;
            report.verdict = (report.passed ? "FUNCTIONAL CHECKS PASSED" : "FAILED") +
                (report.allocation != null && report.allocation.supported
                    ? " - operation CPU/allocations and mounted render-resource counts only; realtime render performance stays unmeasured."
                    : " - operation CPU and mounted render-resource counts only; managed allocation counter unavailable (allocatedBytes=-1 sentinel), realtime render performance stays unmeasured.");
            PerfWriteNew(Path.Combine(output, "report.json"), Json(report, true));
            return Path.Combine(output, "report.json");
        }

        static VerifyScene PerfCreateScene(List<string> unsupported, List<string> notes)
        {
            // A saved empty scene can always be opened additively, while EditorSceneManager.NewScene(Additive)
            // can fail in a TestRunner-like untitled session. The fixture is required for this profiler.
            if (!File.Exists(VerifyFullPath(PerfStagingScene)))
                throw new InvalidOperationException("Missing saved empty staging scene: " + PerfStagingScene);
            PerfStagingPreexisting = false;
            var result = new VerifyScene();
            result.previousActive = SceneManager.GetActiveScene();
            try
            {
                var preview = EditorSceneManager.NewPreviewScene();
                if (preview.IsValid() && preview.isLoaded)
                {
                    result.scene = preview; result.valid = true; result.previewPhysics = true;
                    result.physics = preview.GetPhysicsScene(); result.localPhysics = result.physics.IsValid();
                    if (!result.localPhysics) unsupported.Add("The Editor preview scene reported an invalid PhysicsScene; manual stepping was skipped.");
                }
            }
            catch (Exception e) { unsupported.Add("EditorSceneManager.NewPreviewScene failed: " + e.GetType().Name + ": " + e.Message); }
            try
            {
                var loaded = SceneManager.GetSceneByPath(PerfStagingScene);
                PerfStagingPreexisting = loaded.IsValid() && loaded.isLoaded;
                result.staging = PerfStagingPreexisting ? loaded : EditorSceneManager.OpenScene(PerfStagingScene, OpenSceneMode.Additive);
                if (result.staging.IsValid() && result.staging.isLoaded)
                    notes.Add("Working scenes use the saved empty staging fixture " + PerfStagingScene + ", opened additively; no user scene is saved, replaced or resaved.");
            }
            catch (Exception e) { unsupported.Add("Saved staging fixture open failed: " + e.GetType().Name + ": " + e.Message); }
            if (!result.staging.IsValid())
            {
                unsupported.Add("No staging scene could be loaded. Runtime instance/damage checks were skipped so nothing is ever created inside the user's open scene.");
                if (result.scene.IsValid()) { try { EditorSceneManager.ClosePreviewScene(result.scene); } catch { } }
                result.scene = default(Scene); result.valid = false; result.localPhysics = false; result.previewPhysics = false;
                return result;
            }
            if (!result.valid)
            {
                result.scene = result.staging; result.valid = true; result.localPhysics = false; result.previewPhysics = false;
                notes.Add("No preview scene was available, so runtime checks use only the saved staging scene and physics stays unsupported.");
            }
            try { SceneManager.SetActiveScene(result.staging); }
            catch (Exception e) { unsupported.Add("SceneManager.SetActiveScene(staging) failed: " + e.GetType().Name + ": " + e.Message); }
            return result;
        }

        static void PerfReleaseLeases(List<VoxelTemplateCache.Template> leases, TerrainProfileReport report)
        {
            foreach (var lease in leases)
            {
                try { VoxelTemplateCache.Release(lease); }
                catch (Exception e) { report.failures.Add("Template lease release: " + e.GetType().Name + ": " + e.Message); }
            }
            leases.Clear();
        }
        // EditMode DestroyImmediate does not dispatch MonoBehaviour.OnDestroy automatically. Dispose the
        // runtime/template and visual-manager resources explicitly before destroying the fixture GameObject.
        static void PerfDestroy(GameObject root)
        {
            if (!root) return;
            foreach (var visuals in root.GetComponentsInChildren<VoxelDetachedVisualFragments>(true))
                if (visuals) { try { visuals.Clear(); } catch { } }
            foreach (var runtime in root.GetComponentsInChildren<VoxelStructureRuntime>(true))
                if (runtime) { try { runtime.DisposeResources(); } catch { } }
            VerifyDestroy(root);
        }
        static void PerfDestroy(List<GameObject> roots)
        {
            for (int i = 0; i < roots.Count; i++) PerfDestroy(roots[i]);
            roots.Clear();
        }

        static TerrainRuntimeContract PerfContract(VerifyScene scene, List<GameObject> roots, TerrainProfileReport report)
        {
            var result = new TerrainRuntimeContract
            {
                chunkEdge = VoxelTerrainRenderChunks.ChunkEdge,
                minimumCells = VoxelTerrainRenderChunks.MinimumCells
            };
            var attribute = Attribute.GetCustomAttribute(typeof(VoxelStructureRuntime), typeof(DefaultExecutionOrder)) as DefaultExecutionOrder;
            result.defaultExecutionOrder = attribute == null ? int.MinValue : attribute.order;
            var container = VerifyContainer(scene, "Terrain runtime contract", roots);
            try
            {
                var go = new GameObject("Contract defaults");
                go.transform.SetParent(container.transform, false);
                if (scene != null && scene.valid && scene.scene.IsValid() && go.scene != scene.scene) SceneManager.MoveGameObjectToScene(go, scene.scene);
                var runtime = go.AddComponent<VoxelStructureRuntime>();
                result.defaultTinyFragmentMaxCells = runtime.tinyFragmentMaxCells;
                result.defaultTinyFragmentMaxWorldExtent = runtime.tinyFragmentMaxWorldExtent;
                result.idleCellVisitsConstant = runtime.IdleCellVisits;
            }
            finally { PerfDestroy(container); roots.Remove(container); }
            result.matchesContract = result.chunkEdge == 4 && result.minimumCells == 512 && result.defaultExecutionOrder == -100 &&
                result.defaultTinyFragmentMaxCells == 0 && Math.Abs(result.defaultTinyFragmentMaxWorldExtent - 2f) < .0001f && result.idleCellVisitsConstant == 0;
            result.flushBeforeShips = result.defaultExecutionOrder <= -100;
            if (!result.matchesContract) report.failures.Add("Terrain runtime contract changed (4-cell chunk edge, 512-cell minimum, -100 execution order, tiny off by default, 2-unit extent and constant IdleCellVisits=0 are required).");
            return result;
        }

        static TerrainProfileRow PerfProbe(VerifyScene scene, List<GameObject> roots, VoxelAssetDefinition asset, string role, string scenario, int run, int sustainedHits, TerrainProfileReport report)
        {
            var row = new TerrainProfileRow { asset = asset.name, role = role, scenario = scenario, run = run, chunkSupported = VoxelTerrainRenderChunks.IsSupported(asset) };
            var container = VerifyContainer(scene, "Terrain profile " + scenario + " " + asset.name, roots);
            try
            {
                bool templateExisted = PerfTemplateExists(asset);
                VoxelStructureRuntime runtime = null;
                var init = PerfMeasure("initialize", 1, () => runtime = VerifyInstance(container, scene, asset, Vector3.zero, 1, 1, Vector3.zero, Vector3.zero));
                row.initializeMs = init.cpuMs; // Initialization is reported separately from warmed attacks.
                row.prewarm = PerfPrewarmInfo(runtime, templateExisted, init.cpuMs, -1);
                row.operations.Add(init);
                row.initialCells = runtime.AliveCount;
                row.diagnosticsEnabled = PerfSet(runtime, "CollectDiagnostics", true);
                row.intact = PerfRenderMetrics(container);
                row.before = row.intact;
                row.renderersBefore = row.intact.activeRenderers;
                row.firstHitIncludesRendererActivation = false;
                row.intactSingleRootRenderer = row.intact.meshFilters == 1 && row.intact.chunkMeshFilters == 0 && !row.intact.chunksActive && row.intact.rootAggregateVisible;
                if (row.chunkSupported && !row.intactSingleRootRenderer)
                    PerfFail(report, row, "A large hull must keep exactly one intact root renderer before its first lethal hit.");
                row.physicsBodiesMatchRuntimes = row.intact.bodies == row.intact.runtimes && row.intact.runtimes > 0;
                var signature = PerfSignature(runtime);
                var beforeDiagnostics = PerfDiagnostics(container);
                var tick = PerfMaintenance(runtime, out row.maintenancePath);
                var destroyed = new HashSet<Vector3Int>();
                if (scenario == "idle-1024")
                {
                    tick(); // JIT/delegate/empty-list path warmup outside measurement.
                    row.operations.Add(PerfMeasure("maintenance", 1024, () => { for (int i = 0; i < 1024; i++) tick(); }));
                    row.noRebuild = PerfSameSignature(signature, PerfSignature(runtime));
                    if (!row.noRebuild) PerfFail(report, row, "Idle maintenance rebuilt geometry/collision or changed mass/body.");
                    if (row.intact.chunksActive) PerfFail(report, row, "Idle maintenance activated chunk rendering.");
                    row.runtimeEnabledAfter = runtime.enabled;
                    if (runtime.enabled) PerfFail(report, row, "Idle maintenance left the runtime enabled.");
                    if (runtime.IdleCellVisits != 0) PerfFail(report, row, "IdleCellVisits is no longer the contractual constant 0.");
                }
                else if (scenario == "nonlethal-expiry")
                {
                    var targets = PerfTargets(runtime, Math.Min(3, runtime.AliveCount));
                    row.targets.AddRange(targets.Select(PerfCoordinate));
                    row.requestedHits = targets.Count;
                    float hit = asset.blockHealth * .25f;
                    row.operations.Add(PerfMeasure("nonlethal-apply", targets.Count, () => { foreach (var p in targets) runtime.ApplyDamage(p, hit); }));
                    row.operations.Add(PerfMeasure("nonlethal-flush", 1, runtime.FlushMeshChanges));
                    foreach (var p in targets) if (!Mathf.Approximately(runtime.GetHealth(p), asset.blockHealth - hit)) PerfFail(report, row, "Nonlethal HP differs.");
                    runtime.AdvanceDamageTime(asset.damageResetSeconds + 1d);
                    row.operations.Add(PerfMeasure("expiry-maintenance", 1, tick));
                    row.healthRestored = true;
                    foreach (var p in targets) if (!Mathf.Approximately(runtime.GetHealth(p), asset.blockHealth)) row.healthRestored = false;
                    row.healthRestored &= runtime.RecentDamageCount == 0;
                    row.noRebuild = PerfSameSignature(signature, PerfSignature(runtime));
                    if (!row.noRebuild || !row.healthRestored) PerfFail(report, row, "Quiet expiry rebuilt geometry or did not fully restore sparse HP.");
                    if (row.intact.chunksActive) PerfFail(report, row, "Nonlethal damage/expiry activated chunk rendering.");
                    row.runtimeEnabledAfter = runtime.enabled;
                    if (runtime.enabled) PerfFail(report, row, "Quiet expiry left the runtime enabled.");
                }
                else
                {
                    List<Vector3Int> targets;
                    float lethal = asset.blockHealth * 2;
                    if (scenario == "single-lethal")
                    {
                        int islandsAfter, faces;
                        var p = VerifyPickSurfaceCell(runtime, VerifyCountIslands(new HashSet<Vector3Int>(runtime.Cells.Where(pair => pair.Value.Alive).Select(pair => pair.Key))), out islandsAfter, out faces);
                        targets = new List<Vector3Int> { p };
                        row.firstHitIncludesRendererActivation = row.chunkSupported;
                    }
                    else if (scenario == "second-local-lethal")
                    {
                        var first = PerfInteriorTarget(runtime, null);
                        var second = PerfInteriorTarget(runtime, first);
                        targets = new List<Vector3Int> { first, second };
                        row.firstHitIncludesRendererActivation = row.chunkSupported;
                        var dealtFirst = 0f;
                        row.operations.Add(PerfMeasure("activation-apply", 1, () => dealtFirst = runtime.ApplyDamage(first, lethal)));
                        row.operations.Add(PerfMeasure("activation-flush", 1, runtime.FlushMeshChanges));
                        if (dealtFirst > 0) { destroyed.Add(first); row.piecesAfter = container.GetComponentsInChildren<VoxelStructureRuntime>(true).Length; }
                        row.activated = PerfRenderMetrics(container);
                        if (row.chunkSupported && !(row.activated.chunksActive && row.activated.chunkMeshFilters > 1))
                            PerfFail(report, row, "The first lethal hit did not activate the prewarmed chunk meshes.");
                        if (!row.chunkSupported && row.activated.meshFilters != 1)
                            PerfFail(report, row, "A non-chunk asset must keep one rebuilt root mesh after damage.");
                        var beforeSecond = runtime.RenderMeshes.Select(m => m.GetInstanceID()).ToList();
                        var dealt = 0f;
                        row.operations.Add(PerfMeasure("apply", 1, () => dealt = runtime.ApplyDamage(second, lethal)));
                        if (dealt > 0) destroyed.Add(second);
                        row.operations.Add(PerfMeasure("flush", 1, runtime.FlushMeshChanges));
                        row.warmHitMountedMeshes = runtime.RenderMeshCount;
                        row.warmHitKeptMeshes = beforeSecond.Intersect(runtime.RenderMeshes.Select(m => m.GetInstanceID())).Count();
                        row.piecesAfter = container.GetComponentsInChildren<VoxelStructureRuntime>(true).Length;
                        if (row.chunkSupported && row.piecesAfter == 1 && row.warmHitKeptMeshes == 0)
                            PerfFail(report, row, "A warm local hit replaced every mounted chunk mesh instead of rebuilding only dirty chunks.");
                    }
                    else if (scenario == "cross-section-cut") targets = PerfCutTargets(runtime, asset);
                    else targets = PerfTargets(runtime, scenario == "batch-lethal" ? 8 : sustainedHits);
                    row.targets.AddRange(targets.Select(PerfCoordinate)); row.requestedHits = targets.Count;
                    if (scenario == "sequential-lethal")
                    {
                        foreach (var p in targets)
                        {
                            var owner = PerfFindOwner(container, p); // host lookup is not charged to runtime operation CPU.
                            if (!owner) { row.skippedVisualOnlyTargets++; continue; }
                            PerfSet(owner, "CollectDiagnostics", true);
                            float dealt = 0;
                            row.operations.Add(PerfMeasure("apply", 1, () => dealt = owner.ApplyDamage(p, lethal)));
                            if (dealt > 0) destroyed.Add(p);
                            row.operations.Add(PerfMeasure("flush", 1, owner.FlushMeshChanges));
                        }
                    }
                    else if (scenario != "second-local-lethal")
                    {
                        var dealt = new float[targets.Count];
                        row.operations.Add(PerfMeasure("apply", targets.Count, () => { for (int i = 0; i < targets.Count; i++) dealt[i] = runtime.ApplyDamage(targets[i], lethal); }));
                        for (int i = 0; i < targets.Count; i++) if (dealt[i] > 0) destroyed.Add(targets[i]);
                        row.operations.Add(PerfMeasure("flush", 1, runtime.FlushMeshChanges));
                    }
                    // Idempotence is checked outside timed work, after all intended deaths.
                    var post = PerfSignature(runtime); runtime.FlushMeshChanges();
                    if (!PerfSameSignature(post, PerfSignature(runtime))) PerfFail(report, row, "A second clean Flush rebuilt the retained runtime.");
                }
                row.removedCells = destroyed.Count;
                row.expectedSurvivors = row.initialCells - row.removedCells;
                bool lethalScenario = scenario != "idle-1024" && scenario != "nonlethal-expiry";
                if (lethalScenario)
                {
                    int expectedRemovals = row.requestedHits - row.skippedVisualOnlyTargets;
                    if (row.removedCells == 0) PerfFail(report, row, "No requested lethal target was actually destroyed (dealt=0 for every selected cell).");
                    if (row.removedCells != expectedRemovals) PerfFail(report, row, "Lethal removals " + row.removedCells + " differ from requested living targets " + expectedRemovals + ".");
                    if (scenario == "second-local-lethal" && row.piecesAfter != 1) PerfFail(report, row, "The second local hit changed the runtime count (" + row.piecesAfter + " pieces); it must stay one runtime.");
                }
                if (scenario == "single-lethal")
                    row.firstHitTotalMs = row.operations.Where(op => op.stage == "apply" || op.stage == "flush").Sum(op => op.cpuMs);
                else if (scenario == "second-local-lethal")
                    row.firstHitTotalMs = row.operations.Where(op => op.stage == "activation-apply" || op.stage == "activation-flush").Sum(op => op.cpuMs);
                row.after = PerfRenderMetrics(container);
                row.mountedMeshesAfter = row.after.activeRenderers;
                row.mountedVerticesAfter = (int)Math.Min(int.MaxValue, row.after.instancedVertices);
                row.uniqueMountedMeshesAfter = row.after.uniqueMeshes;
                row.chunkMeshesAfter = row.after.chunkMeshFilters;
                row.drawUpperBoundAfter = row.after.rendererDrawUpperBound;
                row.activeCollidersAfter = row.after.activeColliderComponents;
                row.pooledCollidersAfter = row.after.pooledColliderComponents;
                row.physicsBodiesMatchRuntimes &= row.after.bodies == row.after.runtimes && row.after.runtimes > 0;
                if (!row.physicsBodiesMatchRuntimes) PerfFail(report, row, "Every non-preview runtime must keep exactly one physical Rigidbody (the profiler never steps physics).");
                // Only collision.Active compounds are physical. Pooled components stay disabled and are not part of the volume.
                if (row.after.enabledBoxes != row.after.activeColliderComponents)
                    PerfFail(report, row, "Active compound collider components disagree with enabled BoxColliders (pooled disabled components must stay out).");
                if (row.after.colliders != row.after.enabledBoxes + row.after.disabledBoxes || row.after.pooledColliderComponents != row.after.disabledBoxes)
                    PerfFail(report, row, "Active vs retained disabled collider component accounting is inconsistent.");
                row.renderersAfter = row.after.activeRenderers;
                row.runtimeEnabledAfter = runtime.enabled;
                if (runtime.enabled) PerfFail(report, row, "The measured cycle left automatic maintenance enabled (expected a quiet runtime).");
                if (runtime.IdleCellVisits != 0) PerfFail(report, row, "IdleCellVisits is no longer the contractual constant 0.");
                // Physical cells are attributed per runtime. Manager-wide visual counters are deliberately NOT added per runtime.
                row.conserved = row.after.physicalAliveCells + row.after.detachedVisualCells == row.expectedSurvivors;
                row.uniquePhysicalOwners = PerfUniqueOwners(container, destroyed);
                if (!row.conserved || !row.uniquePhysicalOwners) PerfFail(report, row, "Survivor count/unique physical ownership failed (per-runtime visual cells included separately).");
                row.diagnostics = PerfDiagnosticDelta(beforeDiagnostics, PerfDiagnostics(container));
                if (scenario == "idle-1024" || scenario == "nonlethal-expiry")
                    foreach (var metric in row.diagnostics)
                        if (metric.available && (metric.name == "GeometryBuildCount" || metric.name == "ColliderBuildCount" || metric.name == "ConnectivityPassCount") && metric.delta != 0)
                            PerfFail(report, row, "No-rebuild scenario changed diagnostic " + metric.name + " by " + metric.delta + ".");
            }
            catch (Exception e) { PerfFail(report, row, e.GetType().Name + ": " + e.Message); }
            finally { PerfDestroy(container); roots.Remove(container); }
            return row;
        }

        static TerrainOperation PerfMeasure(string stage, int count, Action action)
        {
            // Recorder objects and profiler bookkeeping are allocated BEFORE the byte/time window.
            var result = new TerrainOperation { stage = stage, operations = count };
            var watch = new Stopwatch();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            bool allocationSupported = PerfAllocationCounterSupported && PerfAllocated != null;
            long bytes = allocationSupported ? PerfAllocated() : -1;
            watch.Start(); action(); watch.Stop();
            long after = allocationSupported ? PerfAllocated() : -1;
            result.cpuMs = watch.Elapsed.TotalMilliseconds;
            // -1 is the explicit sentinel for an unavailable counter; a measured 0 is only valid after the positive control.
            result.allocatedBytes = allocationSupported ? after - bytes : -1;
            result.gen0 = GC.CollectionCount(0) - g0; result.gen1 = GC.CollectionCount(1) - g1; result.gen2 = GC.CollectionCount(2) - g2;
            return result;
        }
        static TerrainAllocationProbe PerfAllocationProbe()
        {
            var result = new TerrainAllocationProbe();
            var read = PerfAllocated;
            result.methodAvailable = read != null;
            if (!result.methodAvailable)
            {
                result.note = "GC.GetAllocatedBytesForCurrentThread could not be bound; every allocatedBytes field is the -1 sentinel and no managed-allocation claim is made. CPU timings and functional checks are unaffected.";
                return result;
            }
            try
            {
                result.heapBefore = GC.GetTotalMemory(false);
                // A 1 MiB positive control must move the counter. Unity/Mono builds are known to keep it at 0.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    result.attempts = attempt + 1;
                    long before = read();
                    var control = new byte[result.positiveControlBytes];
                    long after = read();
                    GC.KeepAlive(control);
                    long delta = after - before;
                    if (attempt == 0) { result.firstBefore = before; result.firstAfter = after; result.firstDelta = delta; }
                    if (delta > result.maxDelta) result.maxDelta = delta;
                    if (delta >= result.positiveControlBytes / 2) { result.supported = true; break; }
                }
                result.heapAfter = GC.GetTotalMemory(false);
            }
            catch (Exception e)
            {
                result.supported = false;
                result.note = "Managed allocation counter positive control threw " + e.GetType().Name + ": " + e.Message + ". Every allocatedBytes field is the -1 sentinel and no allocation claim is made.";
                return result;
            }
            result.supported = result.methodAvailable && result.supported;
            result.note = result.supported
                ? "Positive control allocated " + result.maxDelta + " measured bytes for a requested " + result.positiveControlBytes + " byte array; GC.GetAllocatedBytesForCurrentThread is verified for this run and allocatedBytes may be interpreted."
                : "Positive control did not move GC.GetAllocatedBytesForCurrentThread (max delta " + result.maxDelta + " for a requested " + result.positiveControlBytes + " byte array; first delta " + result.firstDelta + "). Unity/Mono reports this counter as unsupported here, so every allocatedBytes field is the -1 sentinel and NO zero-allocation claim is made. CPU timings and functional checks are unaffected. Managed-heap diagnostic (not used for per-operation claims): " + result.heapBefore + " -> " + result.heapAfter + ".";
            return result;
        }
        static Func<long> PerfAllocationReader()
        {
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
        }
        static Action PerfMaintenance(VoxelStructureRuntime runtime, out string path)
        {
            var method = runtime.GetType().GetMethod("LateUpdate", PerfFlags, null, Type.EmptyTypes, null);
            if (method != null)
            {
                try { var action = (Action)Delegate.CreateDelegate(typeof(Action), runtime, method); path = "bound LateUpdate (no reflection inside measured loop)"; return action; } catch { }
            }
            path = "FlushMeshChanges only; automatic expiry maintenance unavailable";
            return runtime.FlushMeshChanges;
        }
        static object PerfMember(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var p = type.GetProperty(name, PerfFlags);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
            var f = type.GetField(name, PerfFlags);
            return f == null ? null : f.GetValue(target);
        }
        static bool PerfSet(object target, string name, object value)
        {
            if (target == null) return false;
            var type = target.GetType(); var f = type.GetField(name, PerfFlags);
            if (f != null) { f.SetValue(target, Convert.ChangeType(value, f.FieldType)); return true; }
            var p = type.GetProperty(name, PerfFlags);
            if (p != null && p.CanWrite) { p.SetValue(target, Convert.ChangeType(value, p.PropertyType), null); return true; }
            return false;
        }
        static int PerfInt(object target, string name, int fallback = 0) { object value = PerfMember(target, name); return value == null ? fallback : Convert.ToInt32(value); }
        static long PerfLong(object target, string name, long fallback = -1) { object value = PerfMember(target, name); return value == null ? fallback : Convert.ToInt64(value); }
        static bool PerfBool(object target, string name) { object value = PerfMember(target, name); return value is bool flag && flag; }
        static Dictionary<string, double> PerfDiagnostics(GameObject container)
        {
            var totals = new Dictionary<string, double>();
            foreach (var runtime in container.GetComponentsInChildren<VoxelStructureRuntime>(true))
            {
                var diagnostics = PerfMember(runtime, "PerformanceDiagnostics") ?? PerfMember(runtime, "Diagnostics") ?? runtime;
                foreach (string name in PerfMetricNames)
                {
                    object value = PerfMember(diagnostics, name) ?? PerfMember(runtime, name);
                    if (value == null) continue;
                    double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (!double.IsNaN(number) && !double.IsInfinity(number)) totals[name] = (totals.ContainsKey(name) ? totals[name] : 0) + number;
                }
            }
            return totals;
        }
        static List<TerrainMetric> PerfDiagnosticDelta(Dictionary<string, double> before, Dictionary<string, double> after)
        {
            var result = new List<TerrainMetric>();
            foreach (string name in PerfMetricNames)
            {
                double start, end; bool available = before.TryGetValue(name, out start) && after.TryGetValue(name, out end);
                result.Add(new TerrainMetric { name = name, available = available, delta = available ? after[name] - before[name] : 0 });
            }
            return result;
        }
        static List<Mesh> PerfMeshes(VoxelStructureRuntime runtime)
        {
            return runtime.RenderMeshes.Where(mesh => mesh).ToList();
        }
        static TerrainSignature PerfSignature(VoxelStructureRuntime runtime)
        {
            var result = new TerrainSignature { body = runtime.Body ? runtime.Body.GetInstanceID() : 0, mass = runtime.Body ? runtime.Body.mass : 0, revision = runtime.RenderRevision };
            foreach (var mesh in PerfMeshes(runtime)) result.meshes.Add(mesh.GetInstanceID());
            // Include every collider component: an enabled-state change or a recycled/disabled component
            // is still rebuild evidence, while enabled-only identity would hide it.
            foreach (var collider in runtime.GetComponentsInChildren<BoxCollider>(true))
                if (collider.GetComponentInParent<VoxelStructureRuntime>() == runtime) result.colliders.Add(collider.GetInstanceID());
            result.meshes.Sort(); result.colliders.Sort(); return result;
        }
        static bool PerfSameSignature(TerrainSignature a, TerrainSignature b)
        {
            return a.body == b.body && a.mass == b.mass && a.revision == b.revision && a.meshes.SequenceEqual(b.meshes) && a.colliders.SequenceEqual(b.colliders);
        }
        static TerrainRenderMetrics PerfRenderMetrics(GameObject container)
        {
            var result = new TerrainRenderMetrics(); var meshes = new Dictionary<int, Mesh>();
            var runtimes = container.GetComponentsInChildren<VoxelStructureRuntime>(true); result.runtimes = runtimes.Length;
            foreach (var runtime in runtimes)
            {
                result.physicalAliveCells += runtime.AliveCount;      // per-runtime
                result.detachedVisualCells += runtime.DetachedVisualCells; // per-runtime
                result.detachedVisualFragments += runtime.DetachedVisualFragmentCount;
                result.activeColliderComponents += runtime.ColliderCount; // active compound colliders only
            }
            result.visualCountersAvailable = runtimes.Length > 0;
            result.bodies = container.GetComponentsInChildren<Rigidbody>(true).Length;
            result.gameObjects = container.GetComponentsInChildren<Transform>(true).Length;
            foreach (var collider in container.GetComponentsInChildren<BoxCollider>(true))
            {
                result.colliders++;
                if (collider.enabled && collider.gameObject.activeInHierarchy) result.enabledBoxes++; else result.disabledBoxes++;
            }
            // Total components minus the runtime's active compounds are deliberately retained disabled pool entries.
            result.pooledColliderComponents = Math.Max(0, result.colliders - result.activeColliderComponents);
            var filters = container.GetComponentsInChildren<MeshFilter>(true); result.meshFilters = filters.Length;
            foreach (var filter in filters)
            {
                bool root = filter.GetComponent<VoxelStructureRuntime>() != null;
                if (root) result.rootMeshFilters++; else result.chunkMeshFilters++;
                var mesh = filter.sharedMesh; if (!mesh) continue;
                if (!root) result.mountedChunkMeshes++;
                result.instancedVertices += mesh.vertexCount; meshes[mesh.GetInstanceID()] = mesh;
                for (int i = 0; i < mesh.subMeshCount; i++) result.indices += (long)mesh.GetIndexCount(i);
            }
            foreach (var renderer in container.GetComponentsInChildren<MeshRenderer>(true))
            {
                result.renderers++;
                bool active = renderer.enabled && renderer.gameObject.activeInHierarchy;
                bool root = renderer.GetComponent<VoxelStructureRuntime>() != null;
                if (root) { result.rootRenderers++; if (active) result.activeRootRenderers++; }
                else { result.chunkRenderers++; if (active) result.activeChunkRenderers++; }
                if (active) { result.activeRenderers++; result.rendererDrawUpperBound += Math.Max(1, renderer.sharedMaterials.Length); }
            }
            result.chunksActive = result.activeChunkRenderers > 0;
            result.rootAggregateVisible = result.activeRootRenderers > 0;
            result.uniqueMeshes = meshes.Count; foreach (var mesh in meshes.Values) result.uniqueMeshVertices += mesh.vertexCount;
            // Count each shared manager once. Never attribute manager-wide counters to each runtime.
            foreach (var component in container.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component || component.GetType().Name != "VoxelDetachedVisualFragments") continue;
                result.visualManagers++;
                result.visualLiveCells += PerfInt(component, "CellCount");
                result.visualFragments += PerfInt(component, "FragmentCount");
                result.visualOwnedMeshes += PerfInt(component, "OwnedMeshCount");
                result.visualOwnedMaterials += PerfInt(component, "OwnedMaterialCount");
                result.visualSubmittedDraws += PerfInt(component, "SubmittedDrawCount");
            }
            return result;
        }
        static long PerfMeshBytes(Mesh mesh)
        {
            if (!mesh) return 0;
            long bytes = (long)mesh.vertexCount * 48; // position+normal+uv+tangent lower bound
            for (int i = 0; i < mesh.subMeshCount; i++) bytes += (long)mesh.GetIndexCount(i) * (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
            return bytes;
        }
        static IDictionary PerfTemplateCache()
        {
            var field = typeof(VoxelTemplateCache).GetField("cache", BindingFlags.Static | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(null) as IDictionary;
        }
        static int PerfTemplateCacheCount() { var cache = PerfTemplateCache(); return cache == null ? -1 : cache.Count; }
        static bool PerfTemplateExists(VoxelAssetDefinition asset)
        {
            var cache = PerfTemplateCache();
            return cache != null && asset != null && cache.Contains(asset);
        }
        static int PerfSharedChunkTemplateCount()
        {
            var field = typeof(VoxelTerrainRenderChunks).GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
            var shared = field == null ? null : field.GetValue(null) as IDictionary;
            return shared == null ? -1 : shared.Count;
        }
        // Raw/live/dead audit mirrors VoxelTemplateCache ownership, including destroyed keys.
        static void PerfTemplateCacheAudit(out int raw, out int live, out int dead)
        {
            raw = live = dead = 0;
            var cache = PerfTemplateCache();
            if (cache == null) { raw = live = dead = -1; return; }
            foreach (DictionaryEntry entry in cache)
            {
                raw++;
                if (entry.Key is VoxelAssetDefinition asset && asset) live++; else dead++;
            }
        }
        static void PerfTemplateCacheEntryAudit(VoxelAssetDefinition asset, out bool present, out int users)
        {
            present = false; users = -1;
            var cache = PerfTemplateCache();
            if (asset == null || cache == null) return;
            foreach (DictionaryEntry entry in cache)
                if (ReferenceEquals(entry.Key, asset)) { present = true; users = PerfInt(entry.Value, "users", -1); return; }
        }
        static void PerfSharedChunkTemplateAudit(VoxelAssetDefinition asset, out int raw, out bool present, out int users)
        {
            raw = 0; present = false; users = -1;
            var field = typeof(VoxelTerrainRenderChunks).GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
            var shared = field == null ? null : field.GetValue(null) as IDictionary;
            if (shared == null) { raw = -1; return; }
            foreach (DictionaryEntry entry in shared)
            {
                raw++;
                if (asset != null && ReferenceEquals(entry.Key, asset)) { present = true; users = PerfInt(entry.Value, "users", -1); }
            }
        }
        static Dictionary<Vector3Int, int> PerfTemplateChunkMeshes(VoxelStructureRuntime runtime)
        {
            var result = new Dictionary<Vector3Int, int>();
            var template = PerfChunksTemplate(runtime);
            var coordinates = PerfChunkCoordinateList(template);
            var chunks = template == null ? null : PerfMember(template, "chunks") as IEnumerable;
            if (chunks == null) return result;
            int index = 0;
            foreach (var chunk in chunks)
            {
                if (index < coordinates.Count)
                {
                    var intact = PerfMember(chunk, "intact");
                    var mesh = intact == null ? null : PerfMember(intact, "mesh") as Mesh;
                    if (mesh) result[coordinates[index]] = mesh.GetInstanceID();
                }
                index++;
            }
            return result;
        }
        static HashSet<Vector3Int> PerfChangedChunks(Dictionary<Vector3Int, int> before, Dictionary<Vector3Int, int> after)
        {
            var result = new HashSet<Vector3Int>();
            if (before == null || after == null) return result;
            foreach (var pair in before)
                if (!after.TryGetValue(pair.Key, out int id) || id != pair.Value) result.Add(pair.Key);
            foreach (var pair in after)
                if (!before.ContainsKey(pair.Key)) result.Add(pair.Key);
            return result;
        }
        static bool PerfChunksActive(VoxelStructureRuntime runtime)
        {
            var chunks = PerfMember(runtime, "chunks");
            return chunks != null && PerfBool(chunks, "HasChunks");
        }
        static TerrainPrewarmInfo PerfPrewarmInfo(VoxelStructureRuntime runtime, bool templateExistedBefore, double initializeMs, double repeatInitializeMs)
        {
            var info = new TerrainPrewarmInfo
            {
                asset = runtime && runtime.asset ? runtime.asset.name : string.Empty,
                cells = runtime && runtime.asset && runtime.asset.cells != null ? runtime.asset.cells.Count : 0,
                templateExistedBeforeInitialize = templateExistedBefore,
                intactInitializeMs = initializeMs,
                repeatInitializeMs = repeatInitializeMs,
                intactMountedMeshes = runtime ? runtime.RenderMeshCount : 0
            };
            var template = PerfMember(runtime, "template") as VoxelTemplateCache.Template;
            if (template == null) { info.cacheState = "no template (dataOnly or unsupported)"; return info; }
            info.captured = true; info.templateUsers = template.users;
            info.aggregateVertices = template.mesh ? template.mesh.vertexCount : 0;
            info.aggregateEstimatedBytes = PerfMeshBytes(template.mesh);
            var chunks = template.renderChunks;
            if (chunks != null)
            {
                info.chunkSupported = true;
                info.chunks = chunks.ChunkCount;
                info.chunkVertices = chunks.TotalVertices;
                info.chunkEstimatedBytes = chunks.EstimatedMeshBytes;
                info.prewarmMs = chunks.WarmupMilliseconds;
                info.chunkUsers = chunks.Users;
            }
            info.combinedEstimatedBytes = info.aggregateEstimatedBytes + info.chunkEstimatedBytes;
            info.overheadX = info.aggregateEstimatedBytes > 0 ? (double)info.chunkEstimatedBytes / info.aggregateEstimatedBytes : 0;
            info.cacheState = templateExistedBefore
                ? "warm template in this Editor session (kept by an explicit profiler lease); construction time is the original prewarm, not a cold cache"
                : "template cache miss before this sample; an earlier template may have been built and released in this Editor session; not a cold disk/asset-import measurement";
            return info;
        }
        static void PerfFail(TerrainProfileReport report, TerrainProfileRow row, string message) { report.failures.Add("run=" + row.run + " " + row.asset + "/" + row.scenario + ": " + message); }

        static string PerfCoordinate(Vector3Int p) { return p.x + "," + p.y + "," + p.z; }

        static void PerfCacheCheck(VerifyScene scene, List<GameObject> roots, VoxelAssetDefinition asset, TerrainProfileReport report, List<VoxelTemplateCache.Template> leases)
        {
            var container = VerifyContainer(scene, "Terrain cache " + asset.name, roots);
            var row = new TerrainCacheProbe { asset = asset.name, cells = asset.cells != null ? asset.cells.Count : 0, chunkSupported = VoxelTerrainRenderChunks.IsSupported(asset) };
            try
            {
                bool existed = PerfTemplateExists(asset);
                VoxelStructureRuntime first = null, second = null;
                var firstInit = PerfMeasure("intact-initialize", 1, () => first = VerifyInstance(container, scene, asset, Vector3.zero, 1, 1, Vector3.zero, Vector3.zero));
                var secondInit = PerfMeasure("intact-initialize-repeat", 1, () => second = VerifyInstance(container, scene, asset, Vector3.right * 10000, 1, 1, Vector3.zero, Vector3.zero));
                row.firstInstanceInitMs = firstInit.cpuMs; row.repeatInstanceInitMs = secondInit.cpuMs;
                var a = PerfSignature(first); var b = PerfSignature(second);
                var firstRoot = first.GetComponent<MeshFilter>();
                var secondRoot = second.GetComponent<MeshFilter>();
                int firstChildRenderers = first.GetComponentsInChildren<MeshRenderer>(true).Count(renderer => renderer.gameObject != first.gameObject);
                int secondChildRenderers = second.GetComponentsInChildren<MeshRenderer>(true).Count(renderer => renderer.gameObject != second.gameObject);
                row.sharedMeshCount = a.meshes.Intersect(b.meshes).Count();
                row.sharedTemplates = a.meshes.Count > 0 && a.meshes.SequenceEqual(b.meshes);
                row.aggregateShared = first.RenderMeshCount == 1 && second.RenderMeshCount == 1 && firstRoot.sharedMesh && secondRoot.sharedMesh &&
                    firstRoot.sharedMesh.GetInstanceID() == secondRoot.sharedMesh.GetInstanceID();
                row.intactSingleRootRenderer = first.RenderMeshCount == 1 && second.RenderMeshCount == 1 && !PerfChunksActive(first) && !PerfChunksActive(second) &&
                    firstChildRenderers == 0 && secondChildRenderers == 0 && row.aggregateShared;
                row.childRenderersBeforeHit = firstChildRenderers;
                row.intactMeshesPerInstance = a.meshes.Count;
                row.renderersBefore = first.GetComponentsInChildren<MeshRenderer>(true).Length;
                row.firstLethalIncludesRendererActivation = row.chunkSupported;
                row.templateRetainedForRun = false;
                var info = PerfPrewarmInfo(first, existed, firstInit.cpuMs, secondInit.cpuMs);
                row.cacheState = info.cacheState;
                report.prewarm.Add(info);
                var template = PerfMember(first, "template") as VoxelTemplateCache.Template;
                if (template != null && !leases.Any(lease => ReferenceEquals(lease, template)))
                {
                    VoxelTemplateCache.Retain(template); // Keeps this asset's template warm between probes; released in the profiler finally.
                    leases.Add(template);
                    row.templateRetainedForRun = true;
                    info.retainedForRun = true;
                }
                var intactMesh = firstRoot.sharedMesh;
                int islandsAfter, faces;
                var p = VerifyPickSurfaceCell(first, VerifyCountIslands(new HashSet<Vector3Int>(first.Cells.Where(pair => pair.Value.Alive).Select(pair => pair.Key))), out islandsAfter, out faces);
                var total = PerfMeasure("first-lethal-total", 1, () => { first.ApplyDamage(p, asset.blockHealth * 2); first.FlushMeshChanges(); });
                row.firstLethalTotalMs = total.cpuMs;
                row.renderersAfter = first.GetComponentsInChildren<MeshRenderer>(true).Length;
                row.mountedMeshesAfterHit = first.RenderMeshCount;
                row.chunksActivateOnFirstLethal = row.chunkSupported
                    ? PerfChunksActive(first) && first.RenderMeshCount > 1 && first.GetComponentsInChildren<MeshRenderer>(true).Count(renderer => renderer.gameObject != first.gameObject) >= first.RenderMeshCount
                    : !PerfChunksActive(first) && first.RenderMeshCount == 1 && firstRoot.sharedMesh;
                row.peerIntactAfterHit = second.AliveCount == asset.cells.Count && !PerfChunksActive(second) && second.RenderMeshCount == 1 &&
                    secondRoot.sharedMesh && secondRoot.sharedMesh.GetInstanceID() == intactMesh.GetInstanceID();
                row.note = (row.chunkSupported
                    ? "Prewarmed 4-cube chunks exist in the shared template; the intact runtime keeps one shared root renderer and only the damaged instance activates chunks."
                    : "Below the 512-cell minimum or not a hull asset: no chunk template; damage rebuilds one private root mesh.")
                    + " firstLethalTotalMs includes full first renderer activation (chunk GameObjects/MeshRenderers/bindings), not only mesh triangles.";
                if (!row.sharedTemplates || !row.aggregateShared || !row.intactSingleRootRenderer) report.failures.Add("Template sharing/intact root renderer failed: " + asset.name);
                if (!row.chunksActivateOnFirstLethal) report.failures.Add("First lethal hit did not activate the expected renderer path: " + asset.name);
                if (!row.peerIntactAfterHit) report.failures.Add("An intact peer changed when another instance was damaged: " + asset.name);
            }
            catch (Exception e) { report.failures.Add("Cache probe " + asset.name + ": " + e.GetType().Name + ": " + e.Message); }
            finally { report.cache.Add(row); PerfDestroy(container); roots.Remove(container); }
        }

        static VoxelAssetDefinition PerfSyntheticLargeHull()
        {
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            source.name = "Ephemeral prewarm hull fixture";
            source.damageResetSeconds = 10;
            source.hullModules = AssetDatabase.LoadAssetAtPath<ModuleDatabase>(PerfModuleDatabase);
            if (!source.hullModules) throw new InvalidOperationException("Missing module database for the synthetic hull fixture.");
            for (int x = -32; x <= 32; x++) for (int y = 0; y < 3; y++) for (int z = 0; z < 3; z++)
                source.cells.Add(new VoxelCell { position = new Vector3Int(x, y, z) });
            source.NotifyChanged();
            if (!source.Validate(out var error)) throw new InvalidOperationException("Synthetic hull fixture invalid: " + error);
            return source;
        }
        static TerrainPrewarmProbe PerfPrewarmProbe(VerifyScene scene, List<GameObject> roots, TerrainProfileReport report)
        {
            var result = new TerrainPrewarmProbe();
            var source = PerfSyntheticLargeHull();
            var container = VerifyContainer(scene, "Terrain prewarm hull", roots);
            PerfTemplateCacheAudit(out int rawBefore, out int liveBefore, out int deadBefore);
            PerfSharedChunkTemplateAudit(null, out int sharedRawBefore, out _, out _);
            result.templateCacheRawBefore = rawBefore; result.templateCacheLiveBefore = liveBefore; result.templateCacheDeadBefore = deadBefore;
            result.sharedChunkTemplatesRawBefore = sharedRawBefore;
            try
            {
                result.cells = source.cells.Count;
                bool existed = PerfTemplateExists(source);
                VoxelStructureRuntime first = null;
                var init = PerfMeasure("prewarm-initialize", 1, () => first = VerifyInstance(container, scene, source, Vector3.zero, 1, 1, Vector3.zero, Vector3.zero));
                result.initializeMs = init.cpuMs;
                result.first = PerfPrewarmInfo(first, existed, init.cpuMs, -1);
                result.chunks = result.first.chunks;
                result.renderersBeforeHit = first.GetComponentsInChildren<MeshRenderer>(true).Length;
                result.mountedMeshesBeforeHit = first.RenderMeshCount;
                result.intactSingleRootRenderer = first.RenderMeshCount == 1 && first.GetComponent<MeshFilter>().sharedMesh == first.RenderMeshes[0] && result.renderersBeforeHit == 1;
                result.noChunkObjectsBeforeHit = first.GetComponentsInChildren<MeshFilter>(true).Length == 1 && result.renderersBeforeHit == 1;
                if (!result.first.captured || !result.first.chunkSupported) report.failures.Add("Prewarm probe: the 585-cell hull did not produce a shared chunk template.");
                VoxelStructureRuntime peer = null;
                bool peerExisted = PerfTemplateExists(source);
                var repeat = PerfMeasure("prewarm-initialize-repeat", 1, () => peer = VerifyInstance(container, scene, source, Vector3.right * 20000, 1, 1, Vector3.zero, Vector3.zero));
                result.repeatInitializeMs = repeat.cpuMs;
                result.second = PerfPrewarmInfo(peer, peerExisted, repeat.cpuMs, -1);
                result.reusedOnSecondInstance = result.second.templateExistedBeforeInitialize && !existed &&
                    result.second.prewarmMs == result.first.prewarmMs && result.second.chunks == result.first.chunks &&
                    first.RenderMeshCount == 1 && peer.RenderMeshCount == 1 && first.GetComponent<MeshFilter>().sharedMesh == peer.GetComponent<MeshFilter>().sharedMesh;
                var peerMeshBefore = peer.GetComponent<MeshFilter>().sharedMesh;
                var firstTarget = PerfEndTarget(first, false);
                var apply = PerfMeasure("firsthit-apply", 1, () => first.ApplyDamage(firstTarget, source.blockHealth * 2));
                var flush = PerfMeasure("firsthit-flush", 1, first.FlushMeshChanges);
                result.firstLethalApplyMs = apply.cpuMs; result.firstLethalFlushMs = flush.cpuMs;
                result.firstLethalTotalMs = result.firstLethalApplyMs + result.firstLethalFlushMs;
                result.chunksActivateOnFirstLethal = PerfChunksActive(first);
                result.mountedMeshesAfterHit = first.RenderMeshCount;
                result.mountedVerticesAfterHit = first.RenderMeshes.Sum(mesh => mesh.vertexCount);
                result.renderersAfterHit = first.GetComponentsInChildren<MeshRenderer>(true).Length;
                result.chunkCountMatchesMountedMeshes = result.chunks > 0 && result.mountedMeshesAfterHit == result.chunks;
                result.rootAggregateHidden = !first.GetComponent<MeshRenderer>().enabled && first.GetComponent<MeshFilter>().sharedMesh == null;
                result.peerStayedIntact = peer.RenderMeshCount == 1 && peer.AliveCount == source.cells.Count && !PerfChunksActive(peer) &&
                    peer.GetComponent<MeshFilter>().sharedMesh == peerMeshBefore;
                var templateChunkMeshes = PerfTemplateChunkMeshes(first);
                var firstChunkMeshesAfterFirst = PerfMountedChunkMeshes(first);
                var firstChangedChunks = PerfChangedChunks(templateChunkMeshes, firstChunkMeshesAfterFirst);
                result.firstHitChangedChunks = PerfChunkList(firstChangedChunks);
                var firstChunkMeshesBeforeSecond = firstChunkMeshesAfterFirst;
                var secondTarget = PerfEndTarget(first, true);
                result.firstTarget = PerfCoordinate(firstTarget);
                result.secondTarget = PerfCoordinate(secondTarget);
                var apply2 = PerfMeasure("warmhit-apply", 1, () => first.ApplyDamage(secondTarget, source.blockHealth * 2));
                var flush2 = PerfMeasure("warmhit-flush", 1, first.FlushMeshChanges);
                result.warmLocalApplyMs = apply2.cpuMs; result.warmLocalFlushMs = flush2.cpuMs;
                result.warmLocalTotalMs = result.warmLocalApplyMs + result.warmLocalFlushMs;
                var afterSecondMeshes = first.RenderMeshes.Select(mesh => mesh.GetInstanceID()).ToList();
                var firstChunkMeshesAfterSecond = PerfMountedChunkMeshes(first);
                var secondChangedChunks = PerfChangedChunks(firstChunkMeshesBeforeSecond, firstChunkMeshesAfterSecond);
                var secondInvalidatedChunks = PerfAffectedChunks(first, secondTarget);
                result.secondHitChangedChunks = PerfChunkList(secondChangedChunks);
                result.secondHitInvalidatedChunks = PerfChunkList(secondInvalidatedChunks);
                result.secondHitChangedWithinInvalidated = secondChangedChunks.All(secondInvalidatedChunks.Contains);
                result.mountedMeshesAfterSecondHit = afterSecondMeshes.Count;
                result.mountedVerticesAfterSecondHit = first.RenderMeshes.Sum(mesh => mesh.vertexCount);
                result.renderersAfterSecondHit = first.GetComponentsInChildren<MeshRenderer>(true).Length;
                result.warmHitReplacedMeshes = secondChangedChunks.Count;
                result.warmHitKeptMeshes = Math.Max(0, firstChunkMeshesAfterSecond.Count - result.warmHitReplacedMeshes);
                if (!result.noChunkObjectsBeforeHit || !result.intactSingleRootRenderer || !result.reusedOnSecondInstance)
                    report.failures.Add("Prewarm probe: intact instances did not share one root aggregate with detached chunks.");
                if (!result.chunksActivateOnFirstLethal || !result.rootAggregateHidden || !result.chunkCountMatchesMountedMeshes)
                    report.failures.Add("Prewarm probe: the first lethal hit did not mount every prewarmed chunk and hide the aggregate.");
                if (!result.peerStayedIntact) report.failures.Add("Prewarm probe: the intact peer changed when its sibling activated chunks.");
                if (result.chunks > 0 && result.warmHitKeptMeshes == 0)
                    report.failures.Add("Prewarm probe: a warm local hit replaced every chunk mesh.");
                // Damaging the peer at the SAME cell proves mounted meshes still reuse shared template resources.
                peer.ApplyDamage(firstTarget, source.blockHealth * 2); peer.FlushMeshChanges();
                result.peerMountedMeshesAfterHit = peer.RenderMeshCount;
                var peerChunkMeshesAfter = PerfMountedChunkMeshes(peer);
                var peerChangedChunks = PerfChangedChunks(templateChunkMeshes, peerChunkMeshesAfter);
                result.peerHitChangedChunks = PerfChunkList(peerChangedChunks);
                var firstIds = new HashSet<int>(first.RenderMeshes.Select(mesh => mesh.GetInstanceID()));
                var peerIds = new HashSet<int>(peer.RenderMeshes.Select(mesh => mesh.GetInstanceID()));
                result.sharedMountedMeshes = firstIds.Intersect(peerIds).Count();
                // Only meshes whose identity actually changed are excluded from reuse. A marked neighbour
                // whose bevel geometry/signature is unchanged legitimately keeps its template mesh.
                var actuallyChanged = new HashSet<Vector3Int>(firstChangedChunks);
                actuallyChanged.UnionWith(secondChangedChunks);
                actuallyChanged.UnionWith(peerChangedChunks);
                int mountedChunkCount = firstChunkMeshesAfterSecond.Count;
                result.expectedSharedMountedMeshes = Math.Max(0, mountedChunkCount - actuallyChanged.Count);
                result.prewarmedResourcesReused = mountedChunkCount > 0 && result.sharedMountedMeshes == result.expectedSharedMountedMeshes && result.secondHitChangedWithinInvalidated;
                if (!result.secondHitChangedWithinInvalidated)
                    report.failures.Add("Prewarm probe: the second hit replaced a chunk outside its invalidated neighbourhood [" + result.secondHitInvalidatedChunks + "] (changed [" + result.secondHitChangedChunks + "]).");
                if (!result.prewarmedResourcesReused)
                    report.failures.Add("Prewarm probe: mounted chunk meshes were not reused from the shared template (shared " + result.sharedMountedMeshes + " of expected " + result.expectedSharedMountedMeshes + "; first changed [" + result.firstHitChangedChunks + "], second changed [" + result.secondHitChangedChunks + "], peer changed [" + result.peerHitChangedChunks + "]).");
            }
            catch (Exception e) { report.failures.Add("Prewarm probe: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
            finally
            {
                PerfDestroy(container); roots.Remove(container);
                // Audit ownership while the synthetic asset is still a live dictionary key.
                PerfTemplateCacheEntryAudit(source, out bool templatePresent, out int templateUsers);
                PerfSharedChunkTemplateAudit(source, out _, out bool sharedPresent, out int sharedUsers);
                result.syntheticTemplateAbsentAfterInstances = !templatePresent;
                result.syntheticTemplateUsersAfterInstances = templateUsers;
                result.syntheticTemplateUsersZero = !templatePresent || templateUsers == 0;
                result.syntheticSharedChunkAbsentAfterInstances = !sharedPresent;
                result.syntheticSharedChunkUsersAfterInstances = sharedUsers;
                result.syntheticSharedChunkUsersZero = !sharedPresent || sharedUsers == 0;
                Object.DestroyImmediate(source);
                PerfTemplateCacheAudit(out int rawAfter, out int liveAfter, out int deadAfter);
                PerfSharedChunkTemplateAudit(null, out int sharedRawAfter, out _, out _);
                result.templateCacheRawAfter = rawAfter; result.templateCacheLiveAfter = liveAfter; result.templateCacheDeadAfter = deadAfter;
                result.sharedChunkTemplatesRawAfter = sharedRawAfter;
                result.cacheRestored = rawAfter == rawBefore && liveAfter == liveBefore && deadAfter == deadBefore &&
                    sharedRawAfter == sharedRawBefore && !templatePresent && !sharedPresent;
                if (!result.cacheRestored)
                    report.failures.Add("Prewarm probe cache ownership failed (template raw/live/dead " + rawBefore + "/" + liveBefore + "/" + deadBefore + " -> " + rawAfter + "/" + liveAfter + "/" + deadAfter +
                        ", shared raw " + sharedRawBefore + " -> " + sharedRawAfter + ", synthetic template present=" + templatePresent + " users=" + templateUsers +
                        ", synthetic shared present=" + sharedPresent + " users=" + sharedUsers + ").");
            }
            return result;
        }

        static TerrainDataOnlyProbe PerfDataOnly(VerifyScene scene, List<GameObject> roots, TerrainProfileReport report)
        {
            var result = new TerrainDataOnlyProbe();
            var source = PerfSyntheticLargeHull();
            var container = VerifyContainer(scene, "Terrain data-only preview", roots);
            try
            {
                int cacheBefore = PerfTemplateCacheCount(); int chunksBefore = PerfSharedChunkTemplateCount();
                result.cacheEntriesBefore = cacheBefore; result.sharedChunkTemplatesBefore = chunksBefore;
                var go = new GameObject("Terrain data-only preview instance");
                go.transform.SetParent(container.transform, false);
                if (scene != null && scene.valid && scene.scene.IsValid() && go.scene != scene.scene) SceneManager.MoveGameObjectToScene(go, scene.scene);
                var runtime = go.AddComponent<VoxelStructureRuntime>();
                var init = PerfMeasure("dataonly-initialize", 1, () => runtime.Initialize(source, true, false));
                result.initializeMs = init.cpuMs;
                result.dataOnly = runtime.DataOnlyPreview;
                result.supported = runtime.DataOnlyPreview;
                result.aliveBefore = runtime.AliveCount;
                result.renderMeshesEmpty = runtime.RenderMeshCount == 0 && runtime.TotalVertices == 0;
                result.noTemplate = PerfMember(runtime, "template") == null;
                result.noChunks = PerfMember(runtime, "chunks") == null;
                result.noMountedMesh = go.GetComponent<MeshFilter>() != null && go.GetComponent<MeshFilter>().sharedMesh == null;
                result.rendererDisabled = go.GetComponent<MeshRenderer>() != null && !go.GetComponent<MeshRenderer>().enabled;
                var palette = go.GetComponent<MeshRenderer>() != null ? go.GetComponent<MeshRenderer>().sharedMaterials : Array.Empty<Material>();
                result.paletteMaterials = palette.Length;
                result.paletteRetained = palette.Length == source.SurfaceCount * VoxelHullAppearance.ShapeCount && palette.All(material => material);
                result.noColliders = runtime.ColliderCount == 0 && go.GetComponentsInChildren<BoxCollider>(true).Length == 0;
                result.noRigidbody = go.GetComponent<Rigidbody>() == null;
                result.cacheEntriesAfter = PerfTemplateCacheCount(); result.sharedChunkTemplatesAfter = PerfSharedChunkTemplateCount();
                result.renderCacheAbsent = result.dataOnly && result.noTemplate && result.noChunks && result.renderMeshesEmpty &&
                    result.cacheEntriesAfter == cacheBefore && result.sharedChunkTemplatesAfter == chunksBefore;
                var removed = source.cells.Select(cell => cell.position).OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).First();
                float dealt = runtime.ApplyDamage(removed, source.blockHealth * 2); runtime.FlushMeshChanges();
                result.removed = dealt > 0 ? 1 : 0; result.aliveAfter = runtime.AliveCount;
                result.cacheStillAbsentAfterDamage = runtime.RenderMeshCount == 0 && runtime.ColliderCount == 0 &&
                    go.GetComponentsInChildren<BoxCollider>(true).Length == 0 && PerfTemplateCacheCount() == cacheBefore && PerfSharedChunkTemplateCount() == chunksBefore;
                if (!result.renderCacheAbsent) report.failures.Add("dataOnly preview acquired a template/chunk/mesh cache entry.");
                if (!result.supported || !result.dataOnly) report.failures.Add("dataOnly Initialize did not enter the dataOnly preview mode.");
                if (!result.paletteRetained) report.failures.Add("dataOnly preview did not retain the expected hull material palette (" + result.paletteMaterials + " materials).");
                if (!result.noMountedMesh || !result.rendererDisabled || !result.noColliders || !result.noRigidbody)
                    report.failures.Add("dataOnly preview mounted render objects, colliders or a Rigidbody.");
                if (result.removed != 1 || !result.cacheStillAbsentAfterDamage)
                    report.failures.Add("dataOnly damage created render/collision/cache work or failed to remove its cell.");
            }
            catch (Exception e) { report.failures.Add("dataOnly probe: " + e.GetType().Name + ": " + e.Message); }
            finally { PerfDestroy(container); roots.Remove(container); Object.DestroyImmediate(source); }
            return result;
        }

        static TerrainTinyProbe PerfTinyCheck(VerifyScene scene, List<GameObject> roots, TerrainProfileReport report)
        {
            var result = new TerrainTinyProbe();
            var five = PerfTinySource("Ephemeral tiny-policy five-cell fixture", 5);
            var three = PerfTinySource("Ephemeral tiny-policy three-cell fixture", 3);
            var four = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); four.name = "Ephemeral tiny-policy extent fixture"; four.damageResetSeconds = 10;
            for (int x = 0; x < 4; x++) four.cells.Add(new VoxelCell { position = new Vector3Int(x, 0, 0) });
            four.NotifyChanged();
            var container = VerifyContainer(scene, "Terrain tiny policy", roots);
            try
            {
                // 1. Default policy is off: a five-cell detached island stays a physical runtime.
                var defaultRuntime = PerfInstanceWithPolicy(scene, container, five, 0);
                result.apiPresent = PerfMember(defaultRuntime, "DetachedVisualCells") != null;
                result.defaultOff = defaultRuntime.tinyFragmentMaxCells == 0 && Math.Abs(defaultRuntime.tinyFragmentMaxWorldExtent - 2f) < .0001f;
                result.cellsBefore = defaultRuntime.AliveCount;
                defaultRuntime.ApplyDamage(new Vector3Int(3, 0, 0), 1000); defaultRuntime.FlushMeshChanges();
                var metrics = PerfRenderMetrics(container);
                result.pieces = metrics.runtimes; result.physicalAfter = metrics.physicalAliveCells;
                result.visualAfter = metrics.detachedVisualCells; result.visualManagers = metrics.visualManagers; result.collidersAfter = metrics.colliders;
                result.activeColliders = metrics.activeColliderComponents; result.pooledColliders = metrics.pooledColliderComponents;
                result.defaultPhysical = result.pieces == 2 && result.visualAfter == 0 && result.physicalAfter == result.cellsBefore - 1 && result.collidersAfter > 0;
                PerfClearTiny(container);
                // 2. Opt-in with three detached cells: exact render-only policy when the runtime admits it in EditMode.
                var small = PerfInstanceWithPolicy(scene, container, three, 4);
                result.optInStored = small.tinyFragmentMaxCells == 4 && Math.Abs(small.tinyFragmentMaxWorldExtent - 2f) < .0001f;
                small.ApplyDamage(new Vector3Int(3, 0, 0), 1000); small.FlushMeshChanges();
                var smallMetrics = PerfRenderMetrics(container);
                result.smallOptInPieces = smallMetrics.runtimes;
                result.smallOptInVisualCells = smallMetrics.detachedVisualCells;
                result.smallOptInPhysicalCells = smallMetrics.physicalAliveCells;
                result.renderOnlyAdmitted = smallMetrics.visualManagers > 0;
                if (result.renderOnlyAdmitted)
                {
                    var manager = PerfFindVisualManager(container);
                    result.smallOptInVisualOnly = smallMetrics.runtimes == 1 && smallMetrics.detachedVisualCells == 3 &&
                        smallMetrics.physicalAliveCells == 27 && manager != null && PerfInt(manager, "CellCount") == 3 && PerfInt(manager, "FragmentCount") == 1;
                    result.conserved = smallMetrics.physicalAliveCells + smallMetrics.detachedVisualCells == three.cells.Count - 1;
                }
                else
                {
                    result.smallOptInVisualOnly = smallMetrics.runtimes == 2 && smallMetrics.detachedVisualCells == 0 && smallMetrics.physicalAliveCells == three.cells.Count - 1;
                    result.conserved = result.smallOptInVisualOnly;
                }
                PerfClearTiny(container);
                // 3. Five detached cells are above the <=4 limit in every mode.
                var large = PerfInstanceWithPolicy(scene, container, five, 4);
                large.ApplyDamage(new Vector3Int(3, 0, 0), 1000); large.FlushMeshChanges();
                var largeMetrics = PerfRenderMetrics(container);
                result.retainedPieces = largeMetrics.runtimes; result.retainedPhysicalCells = largeMetrics.physicalAliveCells;
                result.retainedVisualCells = largeMetrics.visualLiveCells; result.retainedVisualFragments = largeMetrics.visualFragments;
                // An empty visual manager component may remain on the container after Clear; only live visual cells matter.
                result.retainedLargePhysical = largeMetrics.runtimes == 2 && largeMetrics.detachedVisualCells == 0 &&
                    largeMetrics.visualLiveCells == 0 && largeMetrics.visualFragments == 0 &&
                    largeMetrics.physicalAliveCells == five.cells.Count - 1;
                PerfClearTiny(container);
                // 4. Full world-bounds extent: 1 cell * scale 2 is eligible, 2.01 is not.
                var boundary = PerfInstanceWithPolicy(scene, container, four, 4, 2f);
                boundary.ApplyDamage(Vector3Int.right * 2, 1000); boundary.FlushMeshChanges();
                var boundaryMetrics = PerfRenderMetrics(container);
                result.extentVisualCells = boundaryMetrics.detachedVisualCells;
                // A render-only boundary keeps 2 physical cells + 1 visual cell; a physical split keeps 3 physical cells.
                result.extentBoundaryVisualOnly = result.renderOnlyAdmitted
                    ? boundaryMetrics.runtimes == 1 && boundaryMetrics.detachedVisualCells == 1 && boundaryMetrics.physicalAliveCells == 2
                    : boundaryMetrics.runtimes == 2 && boundaryMetrics.detachedVisualCells == 0 && boundaryMetrics.physicalAliveCells == 3;
                PerfClearTiny(container);
                var above = PerfInstanceWithPolicy(scene, container, four, 4, 2.01f);
                above.ApplyDamage(Vector3Int.right * 2, 1000); above.FlushMeshChanges();
                var aboveMetrics = PerfRenderMetrics(container);
                result.aboveExtentVisualCells = aboveMetrics.detachedVisualCells;
                result.aboveExtentPieces = aboveMetrics.runtimes; result.aboveExtentPhysicalCells = aboveMetrics.physicalAliveCells;
                // Physical: retained source 2 cells + physical fragment 1 cell = 3 physical cells.
                result.extentAboveBoundaryPhysical = aboveMetrics.runtimes == 2 && aboveMetrics.detachedVisualCells == 0 && aboveMetrics.physicalAliveCells == 3;
                if (!result.apiPresent) { report.unsupported.Add("Detached visual cells API is unavailable; the tiny policy was not evaluated."); return result; }
                if (!result.renderOnlyAdmitted)
                    report.unsupported.Add("This Editor/runtime does not admit render-only fragments for the non-preview EditMode fixture; the exact <=4-cell AND <=2-world-extent branch was not measured here. " + result.note);
                else report.notes.Add("Tiny opt-in probe admitted render-only fragments in EditMode and measured the exact <=4-cell / <=2-world-extent policy.");
                if (!result.defaultOff || !result.defaultPhysical || !result.optInStored || !result.retainedLargePhysical || !result.extentAboveBoundaryPhysical || !result.conserved)
                    report.failures.Add("Tiny policy probe failed: default off, opt-in storage, above-threshold/extent physical behaviour or conservation did not hold.");
                if (result.renderOnlyAdmitted && (!result.smallOptInVisualOnly || !result.extentBoundaryVisualOnly))
                    report.failures.Add("Tiny policy probe failed: an eligible detached island was not render-only or its manager accounting differs.");
            }
            catch (Exception e) { report.failures.Add("Tiny policy probe: " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                PerfDestroy(container); roots.Remove(container);
                Object.DestroyImmediate(five); Object.DestroyImmediate(three); Object.DestroyImmediate(four);
            }
            return result;
        }

        static VoxelAssetDefinition PerfTinySource(string name, int detached)
        {
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); source.name = name; source.damageResetSeconds = 10;
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) for (int z = 0; z < 3; z++) source.cells.Add(new VoxelCell { position = new Vector3Int(x, y, z) });
            source.cells.Add(new VoxelCell { position = new Vector3Int(3, 0, 0) });
            var tendril = new[] { new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0), new Vector3Int(5, 1, 0), new Vector3Int(4, 0, 1) };
            for (int i = 0; i < detached; i++) source.cells.Add(new VoxelCell { position = tendril[i] });
            source.NotifyChanged(); return source;
        }
        static void PerfClearTiny(GameObject container)
        {
            foreach (var piece in container.GetComponentsInChildren<VoxelStructureRuntime>(true)) PerfDestroy(piece.gameObject);
            foreach (var visuals in container.GetComponentsInChildren<VoxelDetachedVisualFragments>(true)) visuals.Clear();
        }
        static object PerfFindVisualManager(GameObject container)
        {
            foreach (var component in container.GetComponentsInChildren<MonoBehaviour>(true))
                if (component && component.GetType().Name == "VoxelDetachedVisualFragments") return component;
            return null;
        }

        static VoxelStructureRuntime PerfInstanceWithPolicy(VerifyScene scene, GameObject container, VoxelAssetDefinition source, int tiny, float scale = 1f)
        {
            var go = new GameObject(source.name + " policy instance");
            go.transform.SetParent(container.transform, false);
            go.transform.localScale = Vector3.one * scale;
            if (scene != null && scene.valid && scene.scene.IsValid() && go.scene != scene.scene) SceneManager.MoveGameObjectToScene(go, scene.scene);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            runtime.tinyFragmentMaxCells = tiny; runtime.tinyFragmentMaxWorldExtent = 2f; runtime.CollectDiagnostics = true;
            runtime.Initialize(source);
            return runtime;
        }

        static TerrainDensityProbe PerfDensity(VerifyScene scene, List<GameObject> roots, List<VoxelAssetDefinition> assets, TerrainProfileReport report)
        {
            var result = new TerrainDensityProbe();
            var container = VerifyContainer(scene, "Terrain actual density patch", roots);
            try
            {
                var layout = new List<VerifyLayoutInstance>();
                VerifyInspectLayout(PerfDensityLayout, layout, assets, report.unsupported, report.notes, report.failures);
                result.instances = layout.Count(item => item != null && item.asset);
                var init = PerfMeasure("density-initialize", Math.Max(1, result.instances), () =>
                {
                    foreach (var item in layout) if (item != null && item.asset) VerifyMapInstance(scene, container.transform, item);
                });
                result.initializeMs = init.cpuMs;
                result.render = PerfRenderMetrics(container);
                report.notes.Add("Density patch reports actual distinct mounted chunk mesh IDs and all instanced vertices/render objects. It does NOT assert the old monolithic 5-unique-mesh count.");
                return result;
            }
            finally { PerfDestroy(container); roots.Remove(container); }
        }

        static List<Vector3Int> PerfTargets(VoxelStructureRuntime runtime, int count)
        {
            var keys = runtime.Cells.Where(p => p.Value.Alive).Select(p => p.Key).OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToList();
            var surface = keys.Where(p => VoxelGeometry.Directions.Any(d => !runtime.Cells.TryGetValue(p + d, out var neighbor) || !neighbor.Alive)).ToList();
            if (surface.Count == 0) surface = keys;
            var result = new List<Vector3Int>();
            if (surface.Count == 0) return result;
            count = Math.Min(count, surface.Count);
            for (int i = 0; i < count; i++) result.Add(surface[i * surface.Count / count]);
            return result;
        }
        static Vector3Int PerfInteriorTarget(VoxelStructureRuntime runtime, Vector3Int? avoid)
        {
            // A cell with all six neighbours alive cannot disconnect the island when removed.
            var alive = new HashSet<Vector3Int>(runtime.Cells.Where(pair => pair.Value.Alive).Select(pair => pair.Key));
            var ordered = alive.OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToList();
            if (ordered.Count == 0) return default;
            if (ordered.Count == 1) return ordered[0];
            int stride = Math.Max(1, ordered.Count / 4096);
            Vector3Int best = ordered[0]; int bestOpen = 7; long bestDistance = long.MinValue;
            for (int i = 0; i < ordered.Count; i += stride)
            {
                var p = ordered[i]; int open = 0;
                for (int d = 0; d < VoxelGeometry.Directions.Length; d++) if (!alive.Contains(p + VoxelGeometry.Directions[d])) open++;
                long distance = avoid.HasValue ? (p - avoid.Value).sqrMagnitude : 0;
                if (open < bestOpen || (open == bestOpen && distance > bestDistance)) { best = p; bestOpen = open; bestDistance = distance; }
            }
            return best;
        }
        static Vector3Int PerfEndTarget(VoxelStructureRuntime runtime, bool maximum)
        {
            var alive = runtime.Cells.Where(pair => pair.Value.Alive).Select(pair => pair.Key).ToList();
            if (alive.Count == 0) return default;
            int minX = alive.Min(p => p.x), maxX = alive.Max(p => p.x);
            int minY = alive.Min(p => p.y), maxY = alive.Max(p => p.y);
            int minZ = alive.Min(p => p.z), maxZ = alive.Max(p => p.z);
            int x = maximum ? maxX : minX;
            int centerY = (minY + maxY) / 2, centerZ = (minZ + maxZ) / 2;
            var slice = alive.Where(p => p.x == x).ToList();
            if (slice.Count == 0) return alive[0];
            return slice.OrderBy(p => Math.Abs(p.y - centerY) + Math.Abs(p.z - centerZ)).ThenBy(p => p.y).ThenBy(p => p.z).First();
        }
        static List<Vector3Int> PerfCutTargets(VoxelStructureRuntime runtime, VoxelAssetDefinition asset)
        {
            Vector3 size = runtime.LocalBounds.size; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float center = runtime.LocalBounds.center[axis] / asset.cellSize;
            var all = runtime.Cells.Where(p => p.Value.Alive).Select(p => p.Key).ToList();
            if (all.Count == 0) return all;
            int layer = all.OrderBy(p => Mathf.Abs(p[axis] - center)).ThenBy(p => p[axis]).First()[axis];
            var cut = all.Where(p => p[axis] == layer).OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToList();
            var reduced = new HashSet<Vector3Int>(all); reduced.ExceptWith(cut);
            if (VerifyCountIslands(reduced) < 2) cut = all.Where(p => Math.Abs(p[axis] - layer) <= 1).OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToList();
            return cut;
        }
        static string PerfChunkList(IEnumerable<Vector3Int> chunks)
        {
            return string.Join(",", chunks.OrderBy(c => c.x).ThenBy(c => c.y).ThenBy(c => c.z).Select(PerfCoordinate));
        }
        static object PerfChunksObject(VoxelStructureRuntime runtime) => PerfMember(runtime, "chunks");
        static object PerfChunksTemplate(VoxelStructureRuntime runtime) => PerfMember(PerfChunksObject(runtime), "template");
        static List<Vector3Int> PerfChunkCoordinateList(object template)
        {
            var result = new List<Vector3Int>();
            var chunks = template == null ? null : PerfMember(template, "chunks") as IEnumerable;
            if (chunks == null) return result;
            foreach (var chunk in chunks)
                if (PerfMember(chunk, "coordinate") is Vector3Int coordinate) result.Add(coordinate);
            return result;
        }
        // Authoritative affected chunks: the runtime's own cellChunks map, exactly what Invalidate marks.
        static HashSet<Vector3Int> PerfAffectedChunks(VoxelStructureRuntime runtime, Vector3Int position)
        {
            var result = new HashSet<Vector3Int>();
            var template = PerfChunksTemplate(runtime);
            var coordinates = PerfChunkCoordinateList(template);
            var cellChunks = template == null ? null : PerfMember(template, "cellChunks") as IDictionary;
            if (cellChunks == null) return result;
            void Add(Vector3Int value)
            {
                if (!cellChunks.Contains(value)) return;
                int id = Convert.ToInt32(cellChunks[value]);
                if (id >= 0 && id < coordinates.Count) result.Add(coordinates[id]);
            }
            Add(position);
            foreach (var direction in VoxelGeometry.Directions) Add(position + direction);
            return result;
        }
        static Dictionary<Vector3Int, int> PerfMountedChunkMeshes(VoxelStructureRuntime runtime)
        {
            var result = new Dictionary<Vector3Int, int>();
            var chunkRoot = PerfChunksObject(runtime);
            var template = PerfChunksTemplate(runtime);
            var coordinates = PerfChunkCoordinateList(template);
            var bindings = chunkRoot == null ? null : PerfMember(chunkRoot, "bindings") as IDictionary;
            if (bindings == null) return result;
            foreach (DictionaryEntry entry in bindings)
            {
                int id = Convert.ToInt32(entry.Key);
                if (id < 0 || id >= coordinates.Count) continue;
                var resource = PerfMember(entry.Value, "resource");
                var mesh = resource == null ? null : PerfMember(resource, "mesh") as Mesh;
                if (mesh) result[coordinates[id]] = mesh.GetInstanceID();
            }
            return result;
        }
        static VoxelStructureRuntime PerfFindOwner(GameObject container, Vector3Int p)
        {
            foreach (var runtime in container.GetComponentsInChildren<VoxelStructureRuntime>(true))
                if (runtime.Cells.TryGetValue(p, out var cell) && cell.Alive) return runtime;
            return null;
        }
        static bool PerfUniqueOwners(GameObject container, HashSet<Vector3Int> destroyed)
        {
            var unique = new HashSet<Vector3Int>();
            foreach (var runtime in container.GetComponentsInChildren<VoxelStructureRuntime>(true))
            {
                int count = 0;
                foreach (var entry in runtime.Cells)
                {
                    if (!entry.Value.Alive) continue;
                    if (!unique.Add(entry.Key) || destroyed.Contains(entry.Key) || entry.Value.owner != runtime) return false;
                    count++;
                }
                if (count != runtime.AliveCount) return false;
            }
            return true;
        }
        static void PerfCaptureFiles(List<TerrainFileAudit> output, List<string> assetPaths)
        {
            var paths = new HashSet<string>(assetPaths, StringComparer.OrdinalIgnoreCase) { "Assets/_Starframe/Maps/Assets/1.asset", PerfStagingScene };
            foreach (string name in new[] { "mainstru", "scatter", "debri", "astro" }) paths.Add("Assets/_Starframe/assetsForCodex/scenemodel/" + name + ".fbx");
            foreach (string path in paths.OrderBy(p => p, StringComparer.Ordinal))
                foreach (string file in new[] { path, path + ".meta" }) output.Add(new TerrainFileAudit { path = file, before = VerifyFileHash(file) });
        }
        static List<TerrainSceneStamp> PerfSceneStamps()
        {
            var result = new List<TerrainSceneStamp>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                result.Add(new TerrainSceneStamp { handle = scene.handle, path = scene.path, dirty = scene.isDirty, roots = scene.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToList() });
            }
            return result;
        }
        static bool PerfSameScenes(List<TerrainSceneStamp> a, List<TerrainSceneStamp> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].handle != b[i].handle || a[i].path != b[i].path || a[i].dirty != b[i].dirty || !a[i].roots.SequenceEqual(b[i].roots)) return false;
            return true;
        }
        static void PerfSummarize(TerrainProfileReport report)
        {
            foreach (var group in report.rows.SelectMany(r => r.operations.Select(op => new { r.asset, r.scenario, op })).GroupBy(x => x.asset + "|" + x.scenario + "|" + x.op.stage))
            {
                var first = group.First(); var ms = group.Select(x => x.op.cpuMs).OrderBy(x => x).ToList();
                var bytes = group.Where(x => x.op.allocatedBytes >= 0).Select(x => (double)x.op.allocatedBytes).OrderBy(x => x).ToList();
                bool allocationAvailable = report.allocation != null && report.allocation.supported && bytes.Count == ms.Count && ms.Count > 0;
                report.summaries.Add(new TerrainProfileSummary { asset = first.asset, scenario = first.scenario, stage = first.op.stage, samples = ms.Count, allocationSamples = bytes.Count, allocationAvailable = allocationAvailable, minMs = ms[0], maxMs = ms[ms.Count - 1], medianMs = PerfMedian(ms), p95Ms = ms[Math.Min(ms.Count - 1, (int)Math.Ceiling(ms.Count * .95) - 1)], medianAllocatedBytes = allocationAvailable ? PerfMedian(bytes) : -1 });
            }
        }
        static double PerfMedian(List<double> sorted) { int n = sorted.Count; return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) * .5; }
        static void PerfWriteNew(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) writer.Write(text);
        }
    }
}

