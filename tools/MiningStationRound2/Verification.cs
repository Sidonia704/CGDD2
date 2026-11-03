// Mining round 2 / Verification fragment.
// Editor-only, edit-mode-only verification for the reusable floating scenery assets used by the
// mining-station layout. It never enters Play mode, never saves scenes or assets, never clears
// VoxelTemplateCache and never writes .asset/.meta/.unity files.
//
// API
//   public static string VerifyAssets(string assetFolder, string layoutPath, string outputJson)
//     assetFolder : folder (or single asset) with VoxelAssetDefinition assets, e.g.
//                   "Assets/_Starframe/Maps/Assets". Empty -> that default folder.
//     layoutPath  : optional SpaceMapDefinition asset, folder containing layouts, or a layout JSON
//                   (see VerifyLayoutJson below). Empty -> layout checks are skipped.
//     outputJson  : report path (project-relative or absolute). Empty -> the JSON text is returned.
//   Returns the absolute report path, or the JSON text when outputJson is empty.
//
// The report is emitted with the shared Json(object) diagnostic writer of this partial class.
// Source-asset identity is still proven with JsonUtility.ToJson(VoxelAssetDefinition) plus file
// SHA-256: those are real Unity asset types, so JsonUtility is valid for them.
//
// Limits (also written into the report):
//   - timings are one-shot Editor CPU milliseconds, not frame times and not FPS;
//   - every station-like asset plus the largest rock and debris is damage-tested (7 probes for five station pieces);
//   - physics is a manual 120 x 1/60 s step loop on an Editor preview scene PhysicsScene
//     that is separate from Physics.defaultPhysicsScene (isolation is proven per run), with no rendering and no gameplay;
//   - if PhysX does not advance in edit mode the physics section is reported "unsupported",
//     never as a pass.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Starframe.Construction;
using Starframe.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        const string VerifyDefaultAssetFolder = "Assets/_Starframe/Maps/Assets";
        const string VerifyReportVersion = "mining-round2-verification/1";
        const int VerifyMaxAssets = 64;
        // Every station-like prototype is benchmarked, so the cap only guards a pathological folder.
        const int VerifyMaxStationProbes = 12;
        const int VerifyPhysicsSteps = 120;
        const float VerifyPhysicsStep = 1f / 60f;
        const int VerifyDensityCopies = 8;
        const int VerifyMaxCellSamples = 200000;
        const int VerifyLayoutGrid = 4;

        // ------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------
        public static string VerifyAssets(string assetFolder, string layoutPath, string outputJson)
        {
            var report = new VerifyReport();
            report.version = VerifyReportVersion;
            report.generatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            report.mode = "Unity Editor, edit mode (Play mode refused)";
            report.assetFolder = VerifyProjectRelative(string.IsNullOrEmpty(assetFolder) ? VerifyDefaultAssetFolder : assetFolder);
            report.layoutPath = layoutPath ?? string.Empty;
            report.environment = VerifyCaptureEnvironment();

            var total = new System.Diagnostics.Stopwatch();
            total.Start();

            var failures = report.failures;
            var unsupported = report.unsupported;
            var notes = report.notes;
            var checks = report.checks;
            var assets = new List<VoxelAssetDefinition>();
            var assetPaths = new List<string>();
            var synthetic = new List<VoxelAssetDefinition>();
            var roots = new List<GameObject>();
            VerifyScene test = null;
            VerifySourceSnapshot before = null;
            var cacheBefore = new List<VoxelAssetDefinition>();
            bool cacheProbeOk = VerifyTryReadTemplateCache(cacheBefore);
            var layoutInstances = new List<VerifyLayoutInstance>();

            var modeBefore = Physics.simulationMode;
            var gravityBefore = Physics.gravity;
            var fixedBefore = Time.fixedDeltaTime;
            var activeBefore = SceneManager.GetActiveScene();
            int openBefore = SceneManager.sceneCount;

            report.source.templateCacheCleared = false;
            report.source.cacheProbeSupported = cacheProbeOk;

            try
            {
                if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    failures.Add("VerifyAssets requires Unity Editor edit mode. Play mode was refused so no gameplay, automatic physics or user scene state is touched.");
                }
                else
                {
                    VerifyLoadAssets(report.assetFolder, assets, assetPaths, notes, failures);
                    if (assets.Count > VerifyMaxAssets)
                    {
                        notes.Add("Only the first " + VerifyMaxAssets + " assets of " + assets.Count + " were verified.");
                        assets.RemoveRange(VerifyMaxAssets, assets.Count - VerifyMaxAssets);
                        assetPaths.RemoveRange(VerifyMaxAssets, assetPaths.Count - VerifyMaxAssets);
                    }
                    report.assetCount = assets.Count;
                    if (assets.Count == 0)
                        failures.Add("No VoxelAssetDefinition was found in '" + report.assetFolder + "'.");
                    else
                        checks.Add("asset discovery: " + assets.Count + " VoxelAssetDefinition(s)");

                    before = VerifyCaptureSource(assets, assetPaths, report.assetFolder);

                    report.layout = VerifyInspectLayout(report.layoutPath, layoutInstances, assets, unsupported, notes, failures);

                    test = VerifyCreateScene(unsupported, notes);
                    if (test.valid)
                    {
                        if (test.previewPhysics && test.localPhysics) checks.Add("Editor preview scene with its own PhysicsScene (manual stepping only)");
                        else if (test.previewPhysics) checks.Add("Editor preview scene without a valid PhysicsScene");
                        else checks.Add("temporary additive scene only (no isolated physics)");
                        notes.Add("Working scene: " + (test.previewPhysics ? "Editor preview scene (own PhysicsScene)" : "temporary additive scene") +
                            "; new GameObjects are created in a staging scene so the user's open scene is never modified.");
                    }

                    for (int i = 0; i < assets.Count; i++)
                    {
                        if (test.valid)
                            report.assets.Add(VerifyMeasureAsset(assets[i], i < assetPaths.Count ? assetPaths[i] : string.Empty, test, roots, failures, notes));
                        else
                            report.assets.Add(VerifyMeasureAssetDataOnly(assets[i], i < assetPaths.Count ? assetPaths[i] : string.Empty, failures));

                        var record = report.assets[report.assets.Count - 1];
                        if (record.valid && record.measurement == "measured")
                            checks.Add("asset '" + record.name + "': " + record.cellCount + " cells, " + record.islandCount + " islands, " + record.meshVertexCount + " vertices, " + record.colliderCount + " colliders");
                    }

                    if (test.valid && assets.Count > 0)
                    {
                        var roles = new List<string>();
                        var indices = new List<int>();
                        VerifyPickRepresentatives(assets, indices, roles, notes);
                        for (int i = 0; i < indices.Count; i++)
                        {
                            var damage = VerifyDamageProbe(roles[i], assets[indices[i]], test, roots, failures, notes);
                            report.representatives.Add(damage);
                            if (damage.ran && damage.aliveConserved && damage.hitCellDestroyed)
                                checks.Add("surface single-cell lethal damage (" + roles[i] + " " + damage.assetName + "): flush " + VerifyNumber(damage.flushMs) + " ms, " + damage.fragments + " fragment(s), " +
                                    damage.aliveTotal + " cells kept, islands " + damage.islandsBefore + " -> " + damage.islandsAfter);
                        }

                        var lightest = VerifyLightest(assets);
                        report.cut = VerifyCutProbe(test, assets, roots, failures, notes);
                        if (report.cut.ran)
                        {
                            if (report.cut.aliveConserved && report.cut.fragments == report.cut.fragmentsExpected && report.cut.expectationMet)
                                checks.Add("real-sample cross-section cut ('" + report.cut.assetName + "', " + report.cut.axis + "=" + report.cut.cutLayer + "): " +
                                    report.cut.removedCells + " cell(s) removed, " + report.cut.fragments + " fragment(s), flush " + VerifyNumber(report.cut.flushMs) + " ms");
                            else if (report.cut.aliveConserved && report.cut.fragments == report.cut.fragmentsExpected)
                                checks.Add("real-sample cross-section cut ('" + report.cut.assetName + "'): removed " + report.cut.removedCells + " cell(s), FragmentCreated=" + report.cut.fragments + " (measured as-is, no 2+ segment split)");
                        }
                        if (lightest != null)
                        {
                            report.density = VerifyDensityProbe(lightest, test, roots, failures);
                            if (report.density.sharedMesh)
                                checks.Add("density probe: " + report.density.instances + " instances share one mesh, " + VerifyNumber(report.density.averageInitMs) + " ms each");
                        }

                        report.physics = VerifyPhysics(test, lightest, roots, synthetic, unsupported, failures, notes);
                        if (report.physics.status == "passed")
                        {
                            checks.Add("physics: preview-scene isolation proven (default physics scene did not advance)");
                            checks.Add("physics: gravity-free drift over 120 x 1/60 s");
                            checks.Add("physics: stationary asset does not fall");
                            checks.Add("physics: collision pushes a dynamic body");
                            checks.Add("physics: no NaN after " + report.physics.steps + " steps");
                        }

                        if (report.layout.status == "validated" && layoutInstances.Count > 0)
                        {
                            report.patchPhysics = VerifyPatchPhysics(test, layoutInstances, report.layout.totalCells, roots, unsupported, failures, notes);
                            if (report.patchPhysics.status == "passed")
                                checks.Add("patch physics: " + report.patchPhysics.bodies + " bodies / " + report.patchPhysics.totalCells + " cells / " +
                                    report.patchPhysics.totalColliders + " colliders, " + report.patchPhysics.steps + " steps, avg " +
                                    VerifyNumber(report.patchPhysics.stepAvgMs) + " ms/step CPU (not FPS)");
                        }
                        else report.patchPhysics.note = "Whole-zone patch physics was skipped because no validated layout with instances was available.";
                    }
                }
            }
            catch (Exception e)
            {
                failures.Add("Verification aborted: " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                for (int i = 0; i < roots.Count; i++) VerifyDestroy(roots[i]);
                roots.Clear();
                VerifyCloseScene(test, notes);
                for (int i = 0; i < synthetic.Count; i++) if (synthetic[i]) UnityEngine.Object.DestroyImmediate(synthetic[i]);
                synthetic.Clear();

                if (before != null && assets.Count > 0)
                    VerifyCompareSource(report, before, assets, assetPaths, report.assetFolder, cacheBefore, cacheProbeOk);
                else
                    report.source.note = "No assets were loaded; source integrity was not evaluated.";

                VerifyRestoreScene(activeBefore);
                report.environment.activeScenePathAfter = SceneManager.GetActiveScene().IsValid() ? SceneManager.GetActiveScene().path : string.Empty;
                report.environment.openSceneCountAfter = SceneManager.sceneCount;
                report.environment.sceneRestored = report.environment.openSceneCountAfter == openBefore &&
                    string.Equals(report.environment.activeScenePathAfter ?? string.Empty, report.environment.activeScenePath ?? string.Empty, StringComparison.Ordinal);

                report.environment.simulationModeAfter = Physics.simulationMode.ToString();
                report.environment.gravityAfterX = Physics.gravity.x;
                report.environment.gravityAfterY = Physics.gravity.y;
                report.environment.gravityAfterZ = Physics.gravity.z;
                report.environment.fixedDeltaTimeAfter = Time.fixedDeltaTime;
                report.environment.globalPhysicsUntouched =
                    string.Equals(report.environment.simulationMode, report.environment.simulationModeAfter, StringComparison.Ordinal) &&
                    Mathf.Approximately(report.environment.fixedDeltaTime, report.environment.fixedDeltaTimeAfter) &&
                    Mathf.Approximately(report.environment.gravityX, report.environment.gravityAfterX) &&
                    Mathf.Approximately(report.environment.gravityY, report.environment.gravityAfterY) &&
                    Mathf.Approximately(report.environment.gravityZ, report.environment.gravityAfterZ);
                report.environment.simulationModeBefore = modeBefore.ToString();
                if (report.environment.globalPhysicsUntouched) checks.Add("global physics settings untouched (simulationMode, gravity, fixedDeltaTime)");
                else failures.Add("Global physics settings changed during verification.");

                var gravityNow = gravityBefore;
                if (!Mathf.Approximately(gravityNow.x, Physics.gravity.x) || !Mathf.Approximately(gravityNow.y, Physics.gravity.y) || !Mathf.Approximately(gravityNow.z, Physics.gravity.z))
                    failures.Add("Physics.gravity differs from its pre-run value.");
                if (!Mathf.Approximately(fixedBefore, Time.fixedDeltaTime))
                    failures.Add("Time.fixedDeltaTime differs from its pre-run value.");
                if (Physics.simulationMode != modeBefore)
                    failures.Add("Physics.simulationMode differs from its pre-run value.");

                if (cacheProbeOk)
                {
                    var cacheAfter = new List<VoxelAssetDefinition>();
                    if (VerifyTryReadTemplateCache(cacheAfter))
                    {
                        report.source.cacheEntriesAfter = cacheAfter.Count;
                        report.source.cachePreserved = true;
                        for (int i = 0; i < cacheBefore.Count; i++)
                        {
                            if (cacheBefore[i] && !cacheAfter.Contains(cacheBefore[i])) { report.source.cachePreserved = false; break; }
                        }
                        if (!report.source.cachePreserved)
                            failures.Add("A VoxelTemplateCache entry that existed before verification is gone afterwards; an open editor preview may have been disturbed.");
                    }
                }
            }

            total.Stop();
            report.durationMs = total.Elapsed.TotalMilliseconds;
            VerifyFillLimits(report);
            VerifyFillBaseline(report);
            VerifyFinalize(report);
            return VerifyWriteReport(report, outputJson);
        }

        // ------------------------------------------------------------------
        // Report types (public fields only, no UnityEngine.Object references,
        // no dictionaries and no Vector3, so the shared reflection Json writer
        // cannot recurse through Unity properties).
        // ------------------------------------------------------------------
        [Serializable] public sealed class VerifyReport
        {
            public string version;
            public bool passed;
            public string verdict;
            public string baseline;
            public List<string> baselineMetrics;
            public List<string> limits;
            public List<string> unsupported;
            public List<string> failures;
            public List<string> notes;
            public List<string> checks;
            public string generatedUtc;
            public string mode;
            public double durationMs;
            public string assetFolder;
            public string layoutPath;
            public string outputJson;
            public int assetCount;
            public VerifyEnvironment environment;
            public VerifySourceIntegrity source;
            public VerifyLayoutRecord layout;
            public List<VerifyAssetRecord> assets;
            public List<VerifyDamageRecord> representatives;
            public VerifyCutRecord cut;
            public VerifyDensityRecord density;
            public VerifyPhysicsRecord physics;
            public VerifyPatchPhysicsRecord patchPhysics;

            public VerifyReport()
            {
                baselineMetrics = new List<string>();
                limits = new List<string>();
                unsupported = new List<string>();
                failures = new List<string>();
                notes = new List<string>();
                checks = new List<string>();
                assets = new List<VerifyAssetRecord>();
                representatives = new List<VerifyDamageRecord>();
                environment = new VerifyEnvironment();
                source = new VerifySourceIntegrity();
                layout = new VerifyLayoutRecord();
                cut = new VerifyCutRecord();
                density = new VerifyDensityRecord();
                physics = new VerifyPhysicsRecord();
                patchPhysics = new VerifyPatchPhysicsRecord();
            }
        }

        [Serializable] public sealed class VerifyEnvironment
        {
            public string unityVersion;
            public string platform;
            public bool editorMode;
            public bool isPlaying;
            public string processor;
            public int processorCount;
            public string gpu;
            public string operatingSystem;
            public string activeScenePath;
            public string activeScenePathAfter;
            public int openSceneCount;
            public int openSceneCountAfter;
            public bool sceneRestored;
            public string simulationMode;
            public string simulationModeBefore;
            public string simulationModeAfter;
            public float gravityX, gravityY, gravityZ;
            public float gravityAfterX, gravityAfterY, gravityAfterZ;
            public float fixedDeltaTime;
            public float fixedDeltaTimeAfter;
            public bool globalPhysicsUntouched;
        }

        [Serializable] public sealed class VerifySourceEntry
        {
            public string name;
            public string path;
            public string jsonHashBefore;
            public string jsonHashAfter;
            public string fileHashBefore;
            public string fileHashAfter;
            public int jsonLengthBefore;
            public int jsonLengthAfter;
            public bool jsonEqual;
            public bool fileEqual;
            public bool unchanged;
        }

        [Serializable] public sealed class VerifySourceIntegrity
        {
            public bool integrityPassed;
            public bool jsonUnchanged;
            public bool filesUnchanged;
            public bool fileSetUnchanged;
            public int assetFilesBefore;
            public int assetFilesAfter;
            public int changedAssets;
            public string combinedJsonHashBefore;
            public string combinedJsonHashAfter;
            public string combinedFileHashBefore;
            public string combinedFileHashAfter;
            public bool cacheProbeSupported;
            public bool cachePreserved;
            public int cacheEntriesBefore;
            public int cacheEntriesAfter;
            public bool templateCacheCleared;
            public string note;
            public List<VerifySourceEntry> entries;

            public VerifySourceIntegrity()
            {
                entries = new List<VerifySourceEntry>();
            }
        }

        [Serializable] public sealed class VerifyLayoutAssetCount
        {
            public string asset;
            public int instances;
            public int cells;
        }

        [Serializable] public sealed class VerifyDensitySample
        {
            public string index;
            public float centerX, centerY, centerZ;
            public int instances;
            public int cells;
        }

        [Serializable] public sealed class VerifyLayoutRecord
        {
            public string status;
            public string path;
            public string note;
            public string validateError;
            public string jsonSha256;
            public bool mapSizeKnown;
            public float mapSizeX, mapSizeY, mapSizeZ;
            public int layoutCount;
            public int instanceCount;
            public int distinctAssets;
            public int totalCells;
            public bool densitySampled;
            public bool densityEstimated;
            public int densityGrid;
            public float densityCellSize;
            public int maxCellsPerSample;
            public int maxInstancesPerSample;
            public int meanCellsPerSample;
            public int stationInstances;
            public bool stationKeywordMatched;
            public string stationLabel;
            public float stationExtentX, stationExtentY, stationExtentZ;
            public float stationExtentMax;
            public string targetNote;
            public List<VerifyDensitySample> samples;
            public List<VerifyLayoutAssetCount> byAsset;

            public VerifyLayoutRecord()
            {
                samples = new List<VerifyDensitySample>();
                byAsset = new List<VerifyLayoutAssetCount>();
                stationLabel = string.Empty;
                status = "skipped";
            }
        }

        [Serializable] public sealed class VerifyShapeCount
        {
            public string shape;
            public int cells;
        }

        [Serializable] public sealed class VerifyAssetRecord
        {
            public string name;
            public string path;
            public string category;
            public string measurement;
            public bool valid;
            public string validateError;
            public int cellCount;
            public int islandCount;
            public int largestIslandCells;
            public int surfaceCount;
            public bool usesHullModels;
            public float cellSize;
            public float blockHealth;
            public float damageResetSeconds;
            public float massPerCell;
            public float fragmentSeparationSpeed;
            public float boundsX, boundsY, boundsZ;
            public int meshVertexCount;
            public int meshSubMeshCount;
            public int meshIndexCount;
            public int meshInstanceId;
            // Actual render meshes reported for this runtime: one shared intact aggregate, or several
            // render-only chunk children on the same physical root after lethal damage.
            public string meshSource;
            public int renderMeshCount;
            public string meshInstanceIds;
            public int colliderCount;
            public float bodyMass;
            public bool bodyGravityFree;
            public bool bodyKinematic;
            public bool templateCachedAtStart;
            public double firstBuildMs;
            public double repeatBuildMs;
            public bool sharedMeshReused;
            public int revisionBefore;
            public int revisionAfter;
            public List<VerifyShapeCount> shapes;

            public VerifyAssetRecord()
            {
                shapes = new List<VerifyShapeCount>();
            }
        }

        [Serializable] public sealed class VerifyDamageRecord
        {
            public string role;
            public string assetName;
            public bool ran;
            public string note;
            public int cellCount;
            public int islandsBefore;
            public int islandsAfter;
            public bool split;
            public int surfaceFaces;
            public bool islandsUnchanged;
            public string hitCell;
            public double lethalDamage;
            public double dealtDamage;
            public double applyDamageMs;
            public double flushMs;
            public double damageMs;
            public int fragments;
            public int fragmentsExpected;
            public int aliveTotal;
            public int expectedAlive;
            public bool aliveConserved;
            public bool hitCellDestroyed;
            public int collidersAfter;
            public int bodiesAfter;
            public int verticesAfter;
            public int renderMeshesAfter;
        }

        [Serializable] public sealed class VerifyCutRecord
        {
            public string role;
            public string assetName;
            public string selection;
            public bool ran;
            public string note;
            public int cellCount;
            public string axis;
            public float axisExtent;
            public int cutLayer;
            public int layers;
            public int removedCells;
            public double applyDamageMs;
            public double flushMs;
            public double damageMs;
            public int islandsBefore;
            public int islandsAfter;
            public int fragments;
            public int fragmentsExpected;
            public int aliveTotal;
            public int expectedAlive;
            public bool aliveConserved;
            public int collidersAfter;
            public int bodiesAfter;
            public int verticesAfter;
            public int renderMeshesAfter;
            public bool expectationMet;
        }

        [Serializable] public sealed class VerifyDensityRecord
        {
            public string assetName;
            public int instances;
            public int cellCount;
            public int colliders;
            public int vertices;
            public bool sharedMesh;
            public string meshSource;
            public int renderMeshesPerInstance;
            public double totalInitMs;
            public double averageInitMs;
            public string note;
        }

        [Serializable] public sealed class VerifyPhysicsRecord
        {
            public string status;
            public string note;
            public bool localPhysicsScene;
            public bool previewScenePhysics;
            public bool isolationProven;
            public float guardDefaultMoved;
            public float guardPreviewMoved;
            public bool stepped;
            public int steps;
            public float stepSeconds;
            public string driftAsset;
            public float driftExpectedDistance;
            public float driftDistance;
            public float driftLateralError;
            public int driftAliveBefore;
            public int driftAliveAfter;
            public float stationaryDisplacement;
            public float collisionTargetSpeed;
            public float collisionTargetDisplacement;
            public int collisionAliveBefore;
            public int collisionAliveAfter;
            public bool realAssetPushTested;
            public float realPushMoverMass;
            public float realPushTargetMass;
            public float realPushTargetSpeed;
            public float realPushTargetDisplacement;
            public int bodiesChecked;
            public int nanChecks;
            public bool gravityFree;
            public bool allBodiesFinite;
            public List<string> findings;
            public List<string> nan;

            public VerifyPhysicsRecord()
            {
                findings = new List<string>();
                nan = new List<string>();
                status = "unsupported";
                steps = VerifyPhysicsSteps;
                stepSeconds = VerifyPhysicsStep;
            }
        }

        // Whole sample zone: every layout instance in one isolated physics scene, stepped manually.
        // Timing is PhysX CPU per step, never a frame time or FPS.
        [Serializable] public sealed class VerifyPatchPhysicsRecord
        {
            public string status;
            public string note;
            public int instances;
            public int bodies;
            public int distinctAssets;
            public int uniqueMeshes;
            public int sharedMeshInstances;
            public int totalCells;
            public int totalColliders;
            public double firstPerAssetInitMs;
            public double warmInitMs;
            public double warmInitAvgMs;
            public double initTotalMs;
            public int steps;
            public float stepSeconds;
            public double stepAvgMs;
            public double stepMaxMs;
            public double stepTotalMs;
            public int finiteChecks;
            public int nanCount;
            public bool gravityFreeAll;
            public bool isolationProven;
            public float guardPreviewMoved;
            public float guardDefaultMoved;
            public int movedBodies;
            public int outOfBoundsBodies;
            public float maxDisplacement;
            public int overlapPairsBefore;
            public int overlapPairsAfter;
            public int newOverlapPairs;
            public float patchExtentX;
            public float patchExtentY;
            public float patchExtentZ;
            public float driftMargin;
            public List<string> nan;

            public VerifyPatchPhysicsRecord()
            {
                nan = new List<string>();
                status = "unsupported";
                steps = VerifyPhysicsSteps;
                stepSeconds = VerifyPhysicsStep;
            }
        }

        // Layout JSON mirror. Only used when layoutPath ends with .json; unknown fields are ignored
        // and unresolved entries downgrade the layout section to "unsupported" instead of failing.
        [Serializable] sealed class VerifyLayoutJson
        {
            public string name;
            public Vector3 size;
            public int grid;
            public VerifyLayoutJsonInstance[] instances;
        }

        [Serializable] sealed class VerifyLayoutJsonInstance
        {
            public string assetPath;
            public string asset;
            public string label;
            public Vector3 position;
            public Vector3 eulerAngles;
            public float scale;
        }

        sealed class VerifyLayoutInstance
        {
            public VoxelAssetDefinition asset;
            public string id;
            public string label;
            public Vector3 position;
            public Vector3 eulerAngles;
            public float scale = 1f;
            public float healthMultiplier = 1f;
            public Vector3 driftVelocity;
            public Vector3 angularVelocity;
        }

        sealed class VerifyScene
        {
            public Scene scene;
            public bool valid;
            public bool localPhysics;
            public bool closed;
            public PhysicsScene physics;
            // Editor preview scene: the only edit-mode scene that owns a physics scene of its own
            // (PhysicsScene index differs from Physics.defaultPhysicsScene) and can be stepped manually.
            public bool previewPhysics;
            public bool isolatedProven;
            // Temporary additive scene used as the creation target for new GameObjects. A preview scene
            // cannot be made active, so this keeps every transient object out of the user's scene.
            public Scene staging;
            public Scene previousActive;
        }

        sealed class VerifySourceSnapshot
        {
            public readonly List<string> jsonHash = new List<string>();
            public readonly List<int> jsonLength = new List<int>();
            public readonly List<string> fileHash = new List<string>();
            public readonly List<string> files = new List<string>();
            public string combinedJsonHash = string.Empty;
            public string combinedFileHash = string.Empty;
        }

        // ------------------------------------------------------------------
        // Environment
        // ------------------------------------------------------------------
        static VerifyEnvironment VerifyCaptureEnvironment()
        {
            var env = new VerifyEnvironment();
            env.unityVersion = Application.unityVersion;
            env.platform = Application.platform.ToString();
            env.editorMode = !Application.isPlaying;
            env.isPlaying = Application.isPlaying;
            env.processor = SystemInfo.processorType;
            env.processorCount = SystemInfo.processorCount;
            env.gpu = SystemInfo.graphicsDeviceName;
            env.operatingSystem = SystemInfo.operatingSystem;
            var active = SceneManager.GetActiveScene();
            env.activeScenePath = active.IsValid() ? active.path : string.Empty;
            env.openSceneCount = SceneManager.sceneCount;
            env.simulationMode = Physics.simulationMode.ToString();
            env.simulationModeBefore = env.simulationMode;
            env.gravityX = Physics.gravity.x;
            env.gravityY = Physics.gravity.y;
            env.gravityZ = Physics.gravity.z;
            env.fixedDeltaTime = Time.fixedDeltaTime;
            return env;
        }

        // ------------------------------------------------------------------
        // Paths and hashes
        // ------------------------------------------------------------------
        static string VerifyProjectRoot()
        {
            try
            {
                var parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Directory.GetCurrentDirectory();
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        static string VerifyProjectRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string value = path.Replace('\\', '/').Trim();
            while (value.EndsWith("/", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 1);
            string root = VerifyProjectRoot().Replace('\\', '/').TrimEnd('/');
            if (value.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(root.Length + 1);
            if (value.StartsWith("./", StringComparison.Ordinal)) value = value.Substring(2);
            return value;
        }

        static string VerifyFullPath(string path)
        {
            string value = path.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
            string root = VerifyProjectRoot();
            return Path.GetFullPath(Path.Combine(root, value.Replace('/', Path.DirectorySeparatorChar)));
        }

        static string VerifySha256(byte[] bytes)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(bytes);
                    var text = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    return text.ToString();
                }
            }
            catch (Exception e)
            {
                return "unavailable:" + e.GetType().Name;
            }
        }

        static string VerifyFileHash(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "no-file";
            try
            {
                string full = VerifyFullPath(assetPath);
                if (!File.Exists(full)) return "missing";
                return VerifySha256(File.ReadAllBytes(full));
            }
            catch (Exception e)
            {
                return "error:" + e.GetType().Name;
            }
        }

        static string VerifyNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Loading
        // ------------------------------------------------------------------
        static void VerifyLoadAssets(string folder, List<VoxelAssetDefinition> assets, List<string> paths, List<string> notes, List<string> failures)
        {
            string normalized = VerifyProjectRelative(folder);
            if (string.IsNullOrEmpty(normalized)) return;

            var direct = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(normalized);
            if (direct)
            {
                assets.Add(direct);
                paths.Add(AssetDatabase.GetAssetPath(direct));
                return;
            }

            if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("assetFolder must be a folder or a VoxelAssetDefinition asset, not a JSON file: " + normalized);
                return;
            }

            string[] guids = null;
            try { guids = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new string[] { normalized }); }
            catch (Exception e) { failures.Add("AssetDatabase.FindAssets failed for '" + normalized + "': " + e.Message); return; }
            if (guids == null || guids.Length == 0)
            {
                if (!Directory.Exists(VerifyFullPath(normalized)))
                    notes.Add("assetFolder does not exist on disk: " + VerifyFullPath(normalized));
                return;
            }

            var found = new List<VoxelAssetDefinition>();
            var foundPaths = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);
                if (!asset) continue;
                found.Add(asset);
                foundPaths.Add(path);
            }
            for (int i = 0; i < found.Count; i++)
                for (int j = i + 1; j < found.Count; j++)
                    if (string.Compare(foundPaths[i], foundPaths[j], StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        var tempAsset = found[i]; found[i] = found[j]; found[j] = tempAsset;
                        string tempPath = foundPaths[i]; foundPaths[i] = foundPaths[j]; foundPaths[j] = tempPath;
                    }
            assets.AddRange(found);
            paths.AddRange(foundPaths);
        }

        // ------------------------------------------------------------------
        // Layout (optional)
        // ------------------------------------------------------------------
        static VerifyLayoutRecord VerifyInspectLayout(string layoutPath, List<VerifyLayoutInstance> instances, List<VoxelAssetDefinition> loaded, List<string> unsupported, List<string> notes, List<string> failures)
        {
            var record = new VerifyLayoutRecord();
            record.status = "skipped";
            record.note = "No layout path was supplied; layout and density sampling were skipped.";
            instances.Clear();
            if (string.IsNullOrEmpty(layoutPath))
            {
                notes.Add(record.note);
                return record;
            }

            string normalized = VerifyProjectRelative(layoutPath);
            record.path = normalized;
            bool validated = true;
            string validateError = string.Empty;
            bool mapSizeKnown = false;
            float sizeX = 0f, sizeY = 0f, sizeZ = 0f;

            if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string full = VerifyFullPath(normalized);
                if (!File.Exists(full))
                {
                    record.status = "unsupported";
                    record.note = "Layout JSON not found on disk: " + full;
                    unsupported.Add(record.note);
                    return record;
                }
                string text = File.ReadAllText(full, Encoding.UTF8);
                record.jsonSha256 = VerifySha256(Encoding.UTF8.GetBytes(text));
                VerifyLayoutJson json = null;
                try { json = JsonUtility.FromJson<VerifyLayoutJson>(text); }
                catch (Exception e) { notes.Add("Layout JSON parse failed: " + e.GetType().Name + ": " + e.Message); }
                if (json == null || json.instances == null || json.instances.Length == 0)
                {
                    record.status = "unsupported";
                    record.note = "Layout JSON has no 'instances' array that Verification.cs can read; layout checks were skipped. See VerifyLayoutJson in this file for the accepted fields.";
                    unsupported.Add(record.note);
                    return record;
                }
                sizeX = json.size.x; sizeY = json.size.y; sizeZ = json.size.z;
                mapSizeKnown = sizeX > 0f && sizeY > 0f && sizeZ > 0f;
                for (int i = 0; i < json.instances.Length; i++)
                {
                    var entry = json.instances[i];
                    if (entry == null) continue;
                    var asset = VerifyResolveAsset(entry.assetPath, entry.asset, loaded);
                    if (!asset)
                    {
                        validated = false;
                        validateError = "Layout JSON instance " + i + " did not resolve to a VoxelAssetDefinition (assetPath='" + (entry.assetPath ?? string.Empty) + "', asset='" + (entry.asset ?? string.Empty) + "').";
                        continue;
                    }
                    instances.Add(new VerifyLayoutInstance
                    {
                        asset = asset,
                        label = string.IsNullOrEmpty(entry.label) ? asset.name : entry.label,
                        position = entry.position,
                        eulerAngles = entry.eulerAngles,
                        scale = entry.scale > 0.01f ? entry.scale : 1f
                    });                }
            }
            else
            {
                var map = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(normalized);
                if (map) VerifyCollectMap(map, instances, ref validated, ref validateError, ref mapSizeKnown, ref sizeX, ref sizeY, ref sizeZ);
                else
                {
                    string[] guids = null;
                    try { guids = AssetDatabase.FindAssets("t:SpaceMapDefinition", new string[] { normalized }); }
                    catch { guids = null; }
                    if (guids == null || guids.Length == 0)
                    {
                        record.status = "unsupported";
                        record.note = "layoutPath is neither a SpaceMapDefinition asset, a folder holding one, nor a readable layout JSON: " + normalized;
                        unsupported.Add(record.note);
                        return record;
                    }
                    int maps = 0;
                    for (int i = 0; i < guids.Length; i++)
                    {
                        var each = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                        if (!each) continue;
                        maps++;
                        VerifyCollectMap(each, instances, ref validated, ref validateError, ref mapSizeKnown, ref sizeX, ref sizeY, ref sizeZ);
                    }
                    record.layoutCount = maps;
                    if (maps > 1)
                        notes.Add("layoutPath is a folder; " + maps + " SpaceMapDefinition assets were aggregated for the density picture.");
                }
            }

            record.validateError = validateError;
            if (!validated)
            {
                record.status = "failed";
                record.note = validateError;
                failures.Add("Layout validation failed: " + validateError);
                return record;
            }

            record.mapSizeKnown = mapSizeKnown;
            record.mapSizeX = sizeX;
            record.mapSizeY = sizeY;
            record.mapSizeZ = sizeZ;
            VerifySummarizeLayout(record, instances, notes);
            record.status = "validated";
            record.note = "Layout read with " + record.instanceCount + " instance(s) and " + record.totalCells + " authored cells.";
            return record;
        }

        static void VerifyCollectMap(SpaceMapDefinition map, List<VerifyLayoutInstance> instances, ref bool validated, ref string validateError, ref bool mapSizeKnown, ref float sizeX, ref float sizeY, ref float sizeZ)
        {
            string error;
            if (!map.Validate(out error))
            {
                validated = false;
                validateError = "SpaceMapDefinition '" + map.name + "' failed Validate: " + (error ?? "unknown");
                return;
            }
            mapSizeKnown = map.size.x > 0f && map.size.y > 0f && map.size.z > 0f;
            sizeX = map.size.x; sizeY = map.size.y; sizeZ = map.size.z;
            for (int i = 0; i < map.instances.Count; i++)
            {
                var item = map.instances[i];
                if (item == null || !item.asset) continue;
                instances.Add(new VerifyLayoutInstance
                {
                    asset = item.asset,
                    id = item.id,
                    label = string.IsNullOrEmpty(item.label) ? item.asset.name : item.label,
                    position = item.position,
                    eulerAngles = item.eulerAngles,
                    scale = item.scale,
                    healthMultiplier = item.healthMultiplier,
                    driftVelocity = item.driftVelocity,
                    angularVelocity = item.angularVelocity
                });
            }
        }

        static VoxelAssetDefinition VerifyResolveAsset(string assetPath, string assetName, List<VoxelAssetDefinition> loaded)
        {
            if (!string.IsNullOrEmpty(assetPath))
            {
                string path = VerifyProjectRelative(assetPath);
                var direct = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);
                if (direct) return direct;
            }
            if (!string.IsNullOrEmpty(assetName))
            {
                for (int i = 0; i < loaded.Count; i++)
                {
                    if (!loaded[i]) continue;
                    if (loaded[i].name == assetName) return loaded[i];
                    if (loaded[i].name + ".asset" == assetName) return loaded[i];
                }
                string[] guids = null;
                try { guids = AssetDatabase.FindAssets("t:VoxelAssetDefinition"); }
                catch { guids = null; }
                if (guids != null)
                {
                    for (int i = 0; i < guids.Length; i++)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                        if (string.Equals(Path.GetFileNameWithoutExtension(path), assetName, StringComparison.OrdinalIgnoreCase))
                            return AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);
                    }
                }
            }
            return null;
        }

        static void VerifySummarizeLayout(VerifyLayoutRecord record, List<VerifyLayoutInstance> instances, List<string> notes)
        {
            record.instanceCount = instances.Count;
            if (instances.Count == 0)
            {
                notes.Add("Layout has no scenery instances; density sampling was skipped.");
                return;
            }

            var counts = new List<VerifyLayoutAssetCount>();
            var bounds = new Bounds();
            bool first = true;
            int totalCells = 0;
            for (int i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                int cells = instance.asset.cells != null ? instance.asset.cells.Count : 0;
                totalCells += cells;
                VerifyLayoutAssetCount found = null;
                for (int c = 0; c < counts.Count; c++) if (counts[c].asset == instance.asset.name) { found = counts[c]; break; }
                if (found == null)
                {
                    found = new VerifyLayoutAssetCount { asset = instance.asset.name };
                    counts.Add(found);
                }
                found.instances++;
                found.cells += cells;

                var instanceBounds = VerifyInstanceBounds(instance.asset, instance.position, instance.eulerAngles, instance.scale);
                if (first) { bounds = instanceBounds; first = false; }
                else bounds.Encapsulate(instanceBounds);
            }
            record.totalCells = totalCells;
            record.distinctAssets = counts.Count;
            record.byAsset = counts;

            bool stationMatched;
            var station = VerifyLargestStation(instances, out stationMatched);
            record.stationKeywordMatched = stationMatched;
            if (station != null)
            {
                var stationBounds = VerifyInstanceBounds(station.asset, station.position, station.eulerAngles, station.scale);
                record.stationInstances = 1;
                record.stationLabel = stationMatched ? station.label : station.label + " (largest instance; no station-like asset name matched in this layout)";
                record.stationExtentX = stationBounds.size.x;
                record.stationExtentY = stationBounds.size.y;
                record.stationExtentZ = stationBounds.size.z;
                record.stationExtentMax = Mathf.Max(stationBounds.size.x, Mathf.Max(stationBounds.size.y, stationBounds.size.z));
            }

            float extentX = bounds.size.x, extentY = bounds.size.y, extentZ = bounds.size.z;
            float extentMax = Mathf.Max(extentX, Mathf.Max(extentY, extentZ));
            record.targetNote = "Measured scenery extent " + VerifyNumber(extentX) + " x " + VerifyNumber(extentY) + " x " + VerifyNumber(extentZ) +
                "; largest station-like instance extent " + VerifyNumber(record.stationExtentMax) + ". Round-2 design references are ~2500 map units and ~500 main-station units; a local density patch may be smaller and is not compared automatically.";
            notes.Add(record.targetNote);

            if (extentMax <= 0.001f || instances.Count == 0) return;

            int grid = Mathf.Clamp(VerifyLayoutGrid, 1, 8);
            record.densityGrid = grid;
            record.densityCellSize = extentMax / grid;
            var instanceCounts = new int[grid * grid * grid];
            var cellCounts = new int[grid * grid * grid];
            var centers = new Vector3[grid * grid * grid];
            var filled = new bool[grid * grid * grid];

            int stride = Mathf.Max(1, totalCells / VerifyMaxCellSamples);
            record.densityEstimated = stride > 1;
            record.densitySampled = true;
            int buckets = 0;
            for (int i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                var matrix = Matrix4x4.TRS(instance.position, Quaternion.Euler(instance.eulerAngles), Vector3.one * instance.scale);
                var instanceBounds = VerifyInstanceBounds(instance.asset, instance.position, instance.eulerAngles, instance.scale);
                int bucket = VerifyBucket(instanceBounds.center, bounds, record.densityCellSize, grid);
                instanceCounts[bucket]++;
                if (!filled[bucket]) { centers[bucket] = VerifyBucketCenter(bucket, bounds, record.densityCellSize, grid); filled[bucket] = true; }

                if (instance.asset.cells == null) continue;
                float size = instance.asset.cellSize;
                for (int c = 0; c < instance.asset.cells.Count; c += stride)
                {
                    var cell = instance.asset.cells[c];
                    if (cell == null) continue;
                    Vector3 world = matrix.MultiplyPoint3x4((Vector3)cell.position * size);
                    cellCounts[VerifyBucket(world, bounds, record.densityCellSize, grid)]++;
                }
            }

            int maxCells = 0, maxInstances = 0, sum = 0;
            for (int i = 0; i < instanceCounts.Length; i++)
            {
                if (instanceCounts[i] > 0 || cellCounts[i] > 0) buckets++;
                if (cellCounts[i] > maxCells) maxCells = cellCounts[i];
                if (instanceCounts[i] > maxInstances) maxInstances = instanceCounts[i];
                sum += cellCounts[i];
            }
            record.maxCellsPerSample = maxCells;
            record.maxInstancesPerSample = maxInstances;
            record.meanCellsPerSample = buckets > 0 ? sum / buckets : 0;

            var order = new List<int>();
            for (int i = 0; i < cellCounts.Length; i++) if (cellCounts[i] > 0 || instanceCounts[i] > 0) order.Add(i);
            order.Sort(delegate (int a, int b) { return cellCounts[b].CompareTo(cellCounts[a]); });
            int take = Mathf.Min(8, order.Count);
            for (int i = 0; i < take; i++)
            {
                int index = order[i];
                var sample = new VerifyDensitySample();
                sample.index = (index % grid) + "/" + ((index / grid) % grid) + "/" + (index / (grid * grid));
                sample.centerX = centers[index].x;
                sample.centerY = centers[index].y;
                sample.centerZ = centers[index].z;
                sample.instances = instanceCounts[index];
                sample.cells = cellCounts[index];
                record.samples.Add(sample);
            }
            if (record.densityEstimated)
                notes.Add("Layout density used a stride of " + stride + " cells because the layout exceeds " + VerifyMaxCellSamples + " authored cells.");
        }

        static VerifyLayoutInstance VerifyLargestStation(List<VerifyLayoutInstance> instances, out bool matched)
        {
            string[] keywords = { "station", "platform", "mining", "hub", "base", "tank", "truss", "cage", "deck" };
            VerifyLayoutInstance best = null;
            float bestExtent = -1f;
            matched = false;
            for (int i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                string text = ((instance.asset.name ?? string.Empty) + " " + (instance.label ?? string.Empty)).ToLowerInvariant();
                bool match = false;
                for (int k = 0; k < keywords.Length; k++) if (text.IndexOf(keywords[k], StringComparison.Ordinal) >= 0) { match = true; break; }
                if (!match) continue;
                var bounds = VerifyInstanceBounds(instance.asset, instance.position, instance.eulerAngles, instance.scale);
                float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (extent > bestExtent) { bestExtent = extent; best = instance; }
            }
            if (best != null) { matched = true; return best; }
            for (int i = 0; i < instances.Count; i++)
            {
                var bounds = VerifyInstanceBounds(instances[i].asset, instances[i].position, instances[i].eulerAngles, instances[i].scale);
                float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (extent > bestExtent) { bestExtent = extent; best = instances[i]; }
            }
            return best;
        }

        static Bounds VerifyInstanceBounds(VoxelAssetDefinition asset, Vector3 position, Vector3 eulerAngles, float scale)
        {
            var local = asset.LocalBounds;
            var matrix = Matrix4x4.TRS(position, Quaternion.Euler(eulerAngles), Vector3.one * Mathf.Max(0.01f, scale));
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                Vector3 world = matrix.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
            return new Bounds((min + max) * 0.5f, max - min);
        }

        static int VerifyBucket(Vector3 point, Bounds bounds, float size, int grid)
        {
            if (size <= 0.0001f) return 0;
            int x = Mathf.Clamp(Mathf.FloorToInt((point.x - bounds.min.x) / size), 0, grid - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((point.y - bounds.min.y) / size), 0, grid - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((point.z - bounds.min.z) / size), 0, grid - 1);
            return x + y * grid + z * grid * grid;
        }

        static Vector3 VerifyBucketCenter(int index, Bounds bounds, float size, int grid)
        {
            int x = index % grid;
            int y = (index / grid) % grid;
            int z = index / (grid * grid);
            return new Vector3(bounds.min.x + (x + 0.5f) * size, bounds.min.y + (y + 0.5f) * size, bounds.min.z + (z + 0.5f) * size);
        }

        // ------------------------------------------------------------------
        // Isolated scene
        // ------------------------------------------------------------------
        // The Editor refuses SceneManager.CreateScene outside Play mode ("This can only be used during
        // play mode"), and EditorSceneManager.NewScene never creates a local-physics scene. The one
        // edit-mode scene that owns its own PhysicsScene is the preview scene created by
        // EditorSceneManager.NewPreviewScene(): it can be stepped manually and is separate from
        // Physics.defaultPhysicsScene. A preview scene can never be made active, so a temporary
        // additive staging scene is the creation target and no GameObject is ever created inside the
        // user's open scene.
        static VerifyScene VerifyCreateScene(List<string> unsupported, List<string> notes)
        {
            var result = new VerifyScene();
            result.previousActive = SceneManager.GetActiveScene();

            try
            {
                var preview = EditorSceneManager.NewPreviewScene();
                if (preview.IsValid() && preview.isLoaded)
                {
                    result.scene = preview;
                    result.valid = true;
                    result.previewPhysics = true;
                    result.physics = preview.GetPhysicsScene();
                    result.localPhysics = result.physics.IsValid();
                    if (!result.localPhysics)
                        unsupported.Add("The Editor preview scene reported an invalid PhysicsScene; manual stepping was skipped.");
                }
            }
            catch (Exception e)
            {
                unsupported.Add("EditorSceneManager.NewPreviewScene failed: " + e.GetType().Name + ": " + e.Message);
            }

            try
            {
                var staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (staging.IsValid() && staging.isLoaded) result.staging = staging;
            }
            catch (Exception e)
            {
                unsupported.Add("EditorSceneManager.NewScene(Additive) for the staging scene failed: " + e.GetType().Name + ": " + e.Message);
            }

            if (!result.staging.IsValid())
            {
                unsupported.Add("No staging scene could be created. Runtime instance, damage and physics checks were skipped so that nothing is ever created inside the user's open scene.");
                if (result.scene.IsValid())
                {
                    try { EditorSceneManager.ClosePreviewScene(result.scene); } catch { }
                }
                result.scene = default(Scene);
                result.valid = false;
                result.localPhysics = false;
                result.previewPhysics = false;
                return result;
            }

            if (!result.valid)
            {
                result.scene = result.staging;
                result.valid = true;
                result.localPhysics = false;
                result.previewPhysics = false;
                notes.Add("No Editor preview scene was available, so runtime checks use the temporary additive scene only and physics stays unsupported.");
            }

            try { SceneManager.SetActiveScene(result.staging); }
            catch (Exception e)
            {
                unsupported.Add("SceneManager.SetActiveScene(staging) failed: " + e.GetType().Name + ": " + e.Message);
            }
            return result;
        }

        static void VerifyCloseScene(VerifyScene test, List<string> notes)
        {
            if (test == null) return;

            if (test.scene.IsValid() && test.scene.isLoaded)
            {
                try
                {
                    var roots = test.scene.GetRootGameObjects();
                    for (int i = 0; i < roots.Length; i++) VerifyDestroy(roots[i]);
                }
                catch (Exception e)
                {
                    notes.Add("Root cleanup before closing the isolated scene failed: " + e.Message);
                }
            }

            VerifyRestoreScene(test.previousActive);

            bool previewClosed = true;
            bool closePreview = test.previewPhysics && test.scene.IsValid() && test.scene != test.staging;
            if (closePreview)
            {
                try { EditorSceneManager.ClosePreviewScene(test.scene); }
                catch (Exception e)
                {
                    previewClosed = false;
                    notes.Add("EditorSceneManager.ClosePreviewScene failed: " + e.GetType().Name + ": " + e.Message);
                }
            }

            bool stagingClosed = true;
            if (test.staging.IsValid() && test.staging.isLoaded)
            {
                try { EditorSceneManager.CloseScene(test.staging, true); }
                catch (Exception e)
                {
                    stagingClosed = false;
                    notes.Add("EditorSceneManager.CloseScene(staging) failed: " + e.GetType().Name + ": " + e.Message);
                    try { SceneManager.UnloadSceneAsync(test.staging); }
                    catch (Exception inner) { notes.Add("SceneManager.UnloadSceneAsync(staging) also failed: " + inner.GetType().Name + ": " + inner.Message); }
                }
            }

            test.closed = previewClosed && stagingClosed;
            if (!test.closed)
                notes.Add("The isolated scenes could not be closed automatically. They were emptied, but empty scenes may remain in the hierarchy.");
        }
        static void VerifyRestoreScene(Scene previous)
        {
            try
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            catch { }
        }

        static GameObject VerifyContainer(VerifyScene scene, string name, List<GameObject> roots)
        {
            var go = new GameObject(name);
            if (scene != null && scene.valid && scene.scene.IsValid() && go.scene != scene.scene)
                SceneManager.MoveGameObjectToScene(go, scene.scene);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            roots.Add(go);
            return go;
        }

        // Edit mode DestroyImmediate does not deliver MonoBehaviour.OnDestroy, so runtime-owned template
        // leases, chunk meshes, materials and detached render-only visuals would leak. The real ownership
        // callbacks are invoked explicitly first, resolved by name (public DisposeResources() on the
        // current runtime, private OnDestroy() on runtime revisions that only have that), so this file
        // still compiles against the original baseline runtime without the detached-visual type.
        // Only ownership cleanup runs: no play-mode lifecycle event or gameplay callback is simulated.
        // Every call site reaches this after its timed window has stopped, so release work never enters
        // a phase timing; it does contribute to the report's whole-run duration.
        const string VerifyDetachedVisualsTypeName = "VoxelDetachedVisualFragments";

        static void VerifyDestroy(GameObject go)
        {
            if (!go) return;
            try { VerifyReleaseOwnership(go); }
            catch { }
            UnityEngine.Object.DestroyImmediate(go);
        }

        static void VerifyReleaseOwnership(GameObject go)
        {
            if (!go || Application.isPlaying) return;

            var runtimes = go.GetComponentsInChildren<VoxelStructureRuntime>(true);
            for (int i = 0; i < runtimes.Length; i++)
            {
                var runtime = runtimes[i];
                if (!runtime) continue;
                // Current runtime: public DisposeResources(). Original baseline runtime: private OnDestroy().
                if (VerifyInvokeOwnershipCallback(runtime, "DisposeResources")) continue;
                VerifyInvokeOwnershipCallback(runtime, "OnDestroy");
            }

            // Detached visuals are owned by a manager component that may sit on this GameObject itself and
            // has no runtime of its own; its declared OnDestroy() is the only ownership cleanup.
            var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (!behaviour || behaviour.GetType().Name != VerifyDetachedVisualsTypeName) continue;
                VerifyInvokeOwnershipCallback(behaviour, "OnDestroy");
            }
        }

        static bool VerifyInvokeOwnershipCallback(object target, string methodName)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return false;
            try
            {
                var method = target.GetType().GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (method == null || method.IsAbstract || method.ContainsGenericParameters) return false;
                method.Invoke(target, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void VerifyDestroy(List<GameObject> roots)
        {
            for (int i = 0; i < roots.Count; i++) VerifyDestroy(roots[i]);
            roots.Clear();
        }

        // ------------------------------------------------------------------
        // Render mesh aggregation
        // ------------------------------------------------------------------
        // VoxelStructureRuntime.RenderMeshes reports the meshes that are actually drawn for one physical
        // runtime: the single shared intact aggregate, or several shared chunk children attached to the
        // same physical root after lethal damage. Every reported mesh stays in the asset's original local
        // coordinates and keeps the same material slots, so metrics aggregate by summing real vertices and
        // indices. Render-only chunk children are never counted as runtimes, fragments or bodies.
        // Older baseline runtimes have no RenderMeshes member: that path falls back to the old root
        // MeshFilter and reports no invented chunk data.
        const string VerifyRenderMeshesMember = "RenderMeshes";
        const string VerifyRenderMeshesFallbackSource = "root MeshFilter (baseline runtime, no RenderMeshes)";

        static List<Mesh> VerifyRenderMeshes(VoxelStructureRuntime runtime)
        {
            string source;
            return VerifyRenderMeshes(runtime, out source);
        }

        static List<Mesh> VerifyRenderMeshes(VoxelStructureRuntime runtime, out string source)
        {
            var meshes = new List<Mesh>();
            source = string.Empty;
            if (!runtime) return meshes;

            PropertyInfo property = null;
            try
            {
                property = runtime.GetType().GetProperty(VerifyRenderMeshesMember, BindingFlags.Instance | BindingFlags.Public);
            }
            catch
            {
                property = null;
            }

            if (property != null && property.GetIndexParameters().Length == 0 &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                // The API exists: report exactly what it says, even when the list is empty or unreadable.
                source = VerifyRenderMeshesMember;
                try
                {
                    var sequence = property.GetValue(runtime, null) as System.Collections.IEnumerable;
                    if (sequence != null)
                    {
                        foreach (object element in sequence)
                        {
                            var mesh = element as Mesh;
                            if (mesh) meshes.Add(mesh);
                        }
                    }
                }
                catch
                {
                    meshes.Clear();
                }
                return meshes;
            }

            // Baseline runtime: no RenderMeshes property, so the root MeshFilter is the single aggregate.
            var filter = runtime.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh)
            {
                meshes.Add(filter.sharedMesh);
                source = VerifyRenderMeshesFallbackSource;
            }
            return meshes;
        }

        static int VerifyRenderMeshVertexCount(List<Mesh> meshes)
        {
            int total = 0;
            for (int i = 0; i < meshes.Count; i++) total += meshes[i].vertexCount;
            return total;
        }

        static int VerifyRenderMeshIndexCount(List<Mesh> meshes)
        {
            long indices = 0;
            for (int m = 0; m < meshes.Count; m++)
                for (int i = 0; i < meshes[m].subMeshCount; i++)
                    indices += checked((int)meshes[m].GetIndexCount(i));
            return indices > int.MaxValue ? int.MaxValue : (int)indices;
        }

        // Every chunk mesh of one asset is built with the same material slots; report the shared slot
        // count and state a disagreement instead of inventing a combined slot number.
        static int VerifyRenderMeshSlotCount(List<Mesh> meshes, out bool disagree)
        {
            disagree = false;
            if (meshes.Count == 0) return 0;
            int slots = meshes[0].subMeshCount;
            for (int i = 1; i < meshes.Count; i++) if (meshes[i].subMeshCount != slots) disagree = true;
            return slots;
        }

        // Identity of the whole reported mesh set: intact instances share one template mesh, damaged
        // instances share the same chunk resources. Sorted ids make the comparison order-insensitive.
        static string VerifyRenderMeshSignature(List<Mesh> meshes)
        {
            var ids = new List<int>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++) if (meshes[i]) ids.Add(meshes[i].GetInstanceID());
            ids.Sort();
            var text = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(ids[i].ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        static bool VerifySameRenderMeshes(List<Mesh> first, List<Mesh> second)
        {
            return first.Count == second.Count &&
                string.Equals(VerifyRenderMeshSignature(first), VerifyRenderMeshSignature(second), StringComparison.Ordinal);
        }

        // The optional tiny-fragment render-only policy is deliberately OFF for every probe: fragment
        // counts, bodies and cell conservation below stay the fully physical baseline expectations. The
        // members only exist on the optimized runtime, so they are set by name and stay absent on the
        // original baseline API instead of becoming a compile-time dependency.
        static void VerifyKeepTinyFragmentsPhysical(VoxelStructureRuntime runtime)
        {
            if (!runtime) return;
            VerifySetMemberIfPresent(runtime, "tinyFragmentMaxCells", 0);
            VerifySetMemberIfPresent(runtime, "tinyFragmentMaxWorldExtent", 0f);
        }

        static void VerifySetMemberIfPresent(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name) || value == null) return;
            try
            {
                var type = target.GetType();
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && !field.IsInitOnly && !field.IsLiteral)
                {
                    field.SetValue(target, value);
                    return;
                }
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                    property.SetValue(target, value, null);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------
        // Asset measurement
        // ------------------------------------------------------------------
        static VerifyAssetRecord VerifyMeasureAssetDataOnly(VoxelAssetDefinition asset, string path, List<string> failures)
        {
            var record = VerifyFillAssetBasics(asset, path);
            record.measurement = "data only (no isolated scene)";
            if (record.valid && record.cellCount > 0)
            {
                var states = new Dictionary<Vector3Int, VoxelCellState>();
                for (int i = 0; i < asset.cells.Count; i++)
                {
                    var cell = asset.cells[i];
                    if (cell == null) continue;
                    states[cell.position] = new VoxelCellState
                    {
                        position = cell.position,
                        surface = cell.surface,
                        shape = cell.shape,
                        quarterTurns = cell.quarterTurns,
                        modelOffset = cell.modelOffset
                    };
                }
                record.islandCount = VoxelGeometry.Islands(states).Count;
                record.largestIslandCells = record.islandCount > 0 ? VerifyLargestIsland(states) : 0;
            }
            record.measurement = "data only (no isolated scene); mesh/collider/damage checks were skipped";
            failures.Add("Asset '" + record.name + "': runtime checks were skipped because no isolated scene could be created.");
            return record;
        }

        static VerifyAssetRecord VerifyMeasureAsset(VoxelAssetDefinition asset, string path, VerifyScene scene, List<GameObject> roots, List<string> failures, List<string> notes)
        {
            var record = VerifyFillAssetBasics(asset, path);
            if (!record.valid)
            {
                failures.Add("Asset '" + record.name + "' failed VoxelAssetDefinition.Validate: " + record.validateError);
                record.measurement = "skipped: invalid asset";
                return record;
            }
            if (record.cellCount == 0)
            {
                failures.Add("Asset '" + record.name + "' has no cells.");
                record.measurement = "skipped: empty asset";
                return record;
            }

            var cached = new List<VoxelAssetDefinition>();
            record.templateCachedAtStart = VerifyTryReadTemplateCache(cached) && cached.Contains(asset);

            var container = VerifyContainer(scene, "MR2 measure " + record.name, roots);
            try
            {
                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();
                var runtime = VerifyInstance(container, scene, asset, Vector3.zero, 1f, 1f, Vector3.zero, Vector3.zero);
                timer.Stop();
                record.firstBuildMs = timer.Elapsed.TotalMilliseconds;
                record.measurement = "measured";

                var groups = VoxelGeometry.Islands(runtime.Cells);
                record.islandCount = groups.Count;
                record.largestIslandCells = groups.Count > 0 ? groups[0].Count : 0;
                if (record.islandCount < 1 || record.islandCount > record.cellCount)
                    failures.Add("Asset '" + record.name + "' has an impossible six-neighbour island count: " + record.islandCount + " for " + record.cellCount + " cells.");
                if (record.islandCount > 1)
                    notes.Add("Asset '" + record.name + "' already splits into " + record.islandCount + " six-neighbour islands before any damage.");

                var renderMeshes = VerifyRenderMeshes(runtime, out record.meshSource);
                record.renderMeshCount = renderMeshes.Count;
                record.meshInstanceIds = VerifyRenderMeshSignature(renderMeshes);
                record.meshVertexCount = VerifyRenderMeshVertexCount(renderMeshes);
                record.meshInstanceId = renderMeshes.Count == 1 ? renderMeshes[0].GetInstanceID() : 0;
                if (renderMeshes.Count > 0)
                {
                    bool slotMismatch;
                    record.meshSubMeshCount = VerifyRenderMeshSlotCount(renderMeshes, out slotMismatch);
                    record.meshIndexCount = VerifyRenderMeshIndexCount(renderMeshes);
                    if (slotMismatch)
                        notes.Add("Asset '" + record.name + "': its render meshes disagree on material slot count; meshSubMeshCount reports the first mesh and meshIndexCount sums every real sub-mesh index.");
                }
                record.colliderCount = runtime.ColliderCount;
                record.bodyGravityFree = runtime.Body && !runtime.Body.useGravity;
                record.bodyKinematic = runtime.Body ? runtime.Body.isKinematic : true;
                record.bodyMass = runtime.Body ? runtime.Body.mass : 0f;
                record.boundsX = runtime.LocalBounds.size.x;
                record.boundsY = runtime.LocalBounds.size.y;
                record.boundsZ = runtime.LocalBounds.size.z;

                if (record.meshVertexCount <= 0) failures.Add("Asset '" + record.name + "' produced an empty mesh.");
                if (record.colliderCount <= 0) failures.Add("Asset '" + record.name + "' produced no colliders.");
                if (!record.bodyGravityFree) failures.Add("Asset '" + record.name + "' body has useGravity enabled.");
                if (record.bodyKinematic) failures.Add("Asset '" + record.name + "' body is kinematic and cannot drift or be pushed.");
                if (record.bodyMass <= 0f) failures.Add("Asset '" + record.name + "' body mass is not positive.");

                var repeat = VerifyContainer(scene, "MR2 repeat " + record.name, roots);
                try
                {
                    var repeatTimer = new System.Diagnostics.Stopwatch();
                    repeatTimer.Start();
                    var second = VerifyInstance(repeat, scene, asset, Vector3.zero, 1f, 1f, Vector3.zero, Vector3.zero);
                    repeatTimer.Stop();
                    record.repeatBuildMs = repeatTimer.Elapsed.TotalMilliseconds;
                    var secondMeshes = VerifyRenderMeshes(second, out string secondMeshSource);
                    record.sharedMeshReused = VerifySameRenderMeshes(renderMeshes, secondMeshes);
                    if (!record.sharedMeshReused)
                    {
                        notes.Add("Asset '" + record.name + "': the first instance reported " + record.meshSource + " [" + record.meshInstanceIds +
                            "] but the repeated instance reported " + secondMeshSource + " [" + VerifyRenderMeshSignature(secondMeshes) + "].");
                        failures.Add("Asset '" + record.name + "': a second instance did not share the VoxelTemplateCache mesh.");
                    }
                    if (second.AliveCount != record.cellCount)
                        failures.Add("Asset '" + record.name + "': a repeated instance reported " + second.AliveCount + " cells instead of " + record.cellCount + ".");
                }
                finally
                {
                    VerifyDestroy(repeat);
                    roots.Remove(repeat);
                }
            }
            catch (Exception e)
            {
                record.measurement = "threw";
                failures.Add("Asset '" + record.name + "' measurement threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                VerifyDestroy(container);
                roots.Remove(container);
            }

            record.revisionAfter = asset.Revision;
            if (record.revisionAfter != record.revisionBefore)
                failures.Add("Asset '" + record.name + "' revision changed from " + record.revisionBefore + " to " + record.revisionAfter + " during verification.");
            return record;
        }

        static VerifyAssetRecord VerifyFillAssetBasics(VoxelAssetDefinition asset, string path)
        {
            var record = new VerifyAssetRecord();
            record.name = asset ? asset.name : "<missing>";
            record.path = path ?? string.Empty;
            record.category = asset ? asset.category : string.Empty;
            record.cellCount = asset && asset.cells != null ? asset.cells.Count : 0;
            if (!asset)
            {
                record.valid = false;
                record.validateError = "Asset reference is missing.";
                record.measurement = "skipped";
                return record;
            }
            record.cellSize = asset.cellSize;
            record.blockHealth = asset.blockHealth;
            record.damageResetSeconds = asset.damageResetSeconds;
            record.massPerCell = asset.massPerCell;
            record.fragmentSeparationSpeed = asset.fragmentSeparationSpeed;
            record.surfaceCount = asset.SurfaceCount;
            record.usesHullModels = asset.UsesHullModels;
            record.revisionBefore = asset.Revision;
            record.revisionAfter = asset.Revision;
            VerifyShapeDistribution(asset, record.shapes);
            string error;
            record.valid = asset.Validate(out error);
            record.validateError = record.valid ? string.Empty : (error ?? "unknown");
            return record;
        }

        static void VerifyShapeDistribution(VoxelAssetDefinition asset, List<VerifyShapeCount> output)
        {
            if (asset == null || asset.cells == null) return;
            var counts = new int[64];
            var shapes = new BlockShape[64];
            int distinct = 0;
            for (int i = 0; i < asset.cells.Count; i++)
            {
                var cell = asset.cells[i];
                if (cell == null) continue;
                int index = -1;
                for (int s = 0; s < distinct; s++) if (shapes[s] == cell.shape) { index = s; break; }
                if (index < 0)
                {
                    if (distinct >= shapes.Length) continue;
                    index = distinct++;
                    shapes[index] = cell.shape;
                }
                counts[index]++;
            }
            for (int s = 0; s < distinct; s++)
            {
                var entry = new VerifyShapeCount();
                entry.shape = Enum.GetName(typeof(BlockShape), shapes[s]) ?? shapes[s].ToString();
                entry.cells = counts[s];
                output.Add(entry);
            }
            output.Sort(delegate (VerifyShapeCount a, VerifyShapeCount b) { return b.cells.CompareTo(a.cells); });
        }

        static int VerifyLargestIsland(Dictionary<Vector3Int, VoxelCellState> states)
        {
            var groups = VoxelGeometry.Islands(states);
            return groups.Count > 0 ? groups[0].Count : 0;
        }

        static VoxelStructureRuntime VerifyInstance(GameObject parent, VerifyScene scene, VoxelAssetDefinition asset, Vector3 localPosition, float scale, float health, Vector3 drift, Vector3 spin)
        {
            var go = new GameObject(asset.name + " verify instance");
            if (parent) go.transform.SetParent(parent.transform, false);
            if (scene != null && scene.valid && scene.scene.IsValid() && go.scene != scene.scene)
                SceneManager.MoveGameObjectToScene(go, scene.scene);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            VerifyKeepTinyFragmentsPhysical(runtime);
            runtime.healthMultiplier = Mathf.Max(0.01f, health);
            runtime.driftVelocity = drift;
            runtime.angularVelocity = spin;
            runtime.Initialize(asset);
            return runtime;
        }

        static bool VerifyTryReadTemplateCache(List<VoxelAssetDefinition> output)
        {
            output.Clear();
            try
            {
                var field = typeof(VoxelTemplateCache).GetField("cache", BindingFlags.Static | BindingFlags.NonPublic);
                if (field == null) return false;
                var dictionary = field.GetValue(null) as System.Collections.IDictionary;
                if (dictionary == null) return false;
                foreach (var key in dictionary.Keys)
                {
                    var asset = key as VoxelAssetDefinition;
                    if (asset) output.Add(asset);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Representatives and damage
        // ------------------------------------------------------------------
        // Every station-like prototype gets its own single-cell surface benchmark: with several top-level
        // station pieces the largest one swaps between iterations (capsule vs slab), so a single "station"
        // sample would not be comparable across versions. Rocks and debris keep one largest sample each.
        static void VerifyPickRepresentatives(List<VoxelAssetDefinition> assets, List<int> indices, List<string> roles, List<string> notes)
        {
            indices.Clear();
            roles.Clear();
            if (assets.Count == 0) return;

            string[] stationKeywords = { "station", "platform", "mining", "hub", "base", "tank", "truss", "cage", "deck" };
            string[] rockKeywords = { "rock", "asteroid", "stone", "boulder" };
            string[] debrisKeywords = { "debris", "wreck", "fragment", "ruin" };
            var used = new HashSet<int>();

            var order = new List<int>();
            for (int i = 0; i < assets.Count; i++) order.Add(i);
            order.Sort(delegate (int a, int b) { return (assets[b].cells != null ? assets[b].cells.Count : 0).CompareTo(assets[a].cells != null ? assets[a].cells.Count : 0); });

            var stations = new List<int>();
            for (int i = 0; i < assets.Count; i++) if (VerifyNameMatches(assets[i], stationKeywords)) stations.Add(i);
            stations.Sort(delegate (int a, int b) { return (assets[b].cells != null ? assets[b].cells.Count : 0).CompareTo(assets[a].cells != null ? assets[a].cells.Count : 0); });
            int stationCount = Mathf.Min(stations.Count, VerifyMaxStationProbes);
            for (int i = 0; i < stationCount; i++)
            {
                used.Add(stations[i]);
                indices.Add(stations[i]);
                roles.Add("station");
            }
            if (stations.Count > stationCount)
                notes.Add("Station surface benchmark was capped at " + VerifyMaxStationProbes + " of " + stations.Count + " station-like assets.");
            if (stationCount == 0)
            {
                int fallback = VerifyFirstUnused(order, used);
                if (fallback >= 0)
                {
                    used.Add(fallback);
                    indices.Add(fallback);
                    roles.Add("station");
                    notes.Add("No station-like asset name matched; the largest asset is used as the station benchmark.");
                }
            }
            if (stationCount > 1)
                notes.Add("Station surface benchmark covers " + stationCount + " station-like assets: " + VerifyNames(assets, indices) + ".");

            int rock = VerifyPickLargestMatch(assets, rockKeywords, used);
            if (rock < 0) rock = VerifyMiddleUnused(order, used);
            if (rock >= 0)
            {
                used.Add(rock);
                indices.Add(rock);
                roles.Add("rock");
            }

            int debris = VerifyPickLargestMatch(assets, debrisKeywords, used);
            if (debris < 0) debris = VerifyLastUnused(order, used);
            if (debris >= 0)
            {
                used.Add(debris);
                indices.Add(debris);
                roles.Add("debris");
            }
        }

        static bool VerifyNameMatches(VoxelAssetDefinition asset, string[] keywords)
        {
            if (!asset) return false;
            string text = (asset.name ?? string.Empty).ToLowerInvariant() + " " + (asset.category ?? string.Empty).ToLowerInvariant();
            for (int k = 0; k < keywords.Length; k++)
                if (text.IndexOf(keywords[k], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static int VerifyPickLargestMatch(List<VoxelAssetDefinition> assets, string[] keywords, HashSet<int> used)
        {
            int found = -1;
            int foundCells = -1;
            for (int i = 0; i < assets.Count; i++)
            {
                if (used.Contains(i) || !VerifyNameMatches(assets[i], keywords)) continue;
                int cells = assets[i].cells != null ? assets[i].cells.Count : 0;
                if (cells > foundCells) { foundCells = cells; found = i; }
            }
            return found;
        }

        static string VerifyNames(List<VoxelAssetDefinition> assets, List<int> indices)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < indices.Count; i++)
            {
                if (i > 0) text.Append(", ");
                int index = indices[i];
                text.Append(index >= 0 && index < assets.Count && assets[index] ? assets[index].name : "<missing>");
            }
            return text.ToString();
        }

        static int VerifyFirstUnused(List<int> order, HashSet<int> used)
        {
            for (int i = 0; i < order.Count; i++) if (!used.Contains(order[i])) return order[i];
            return -1;
        }

        static int VerifyLastUnused(List<int> order, HashSet<int> used)
        {
            for (int i = order.Count - 1; i >= 0; i--) if (!used.Contains(order[i])) return order[i];
            return -1;
        }

        static int VerifyMiddleUnused(List<int> order, HashSet<int> used)
        {
            int middle = order.Count / 2;
            for (int offset = 0; offset < order.Count; offset++)
            {
                int forward = middle + offset;
                if (forward < order.Count && !used.Contains(order[forward])) return order[forward];
                int backward = middle - offset;
                if (backward >= 0 && !used.Contains(order[backward])) return order[backward];
            }
            return -1;
        }

        static VoxelAssetDefinition VerifyLightest(List<VoxelAssetDefinition> assets)
        {
            VoxelAssetDefinition best = null;
            int bestCount = int.MaxValue;
            for (int i = 0; i < assets.Count; i++)
            {
                if (!assets[i] || assets[i].cells == null) continue;
                int count = assets[i].cells.Count;
                if (count > 0 && count < bestCount) { bestCount = count; best = assets[i]; }
            }
            return best;
        }

        static VerifyDamageRecord VerifyDamageProbe(string role, VoxelAssetDefinition asset, VerifyScene scene, List<GameObject> roots, List<string> failures, List<string> notes)
        {
            var record = new VerifyDamageRecord();
            record.role = role;
            record.assetName = asset ? asset.name : "<missing>";
            record.ran = false;
            if (!asset)
            {
                record.note = "Representative asset is missing.";
                notes.Add(record.note);
                return record;
            }

            var container = VerifyContainer(scene, "MR2 damage " + role + " " + record.assetName, roots);
            try
            {
                var runtime = VerifyInstance(container, scene, asset, Vector3.zero, 1f, 1f, Vector3.zero, Vector3.zero);
                record.ran = true;
                record.cellCount = runtime.AliveCount;
                record.islandsBefore = VoxelGeometry.Islands(runtime.Cells).Count;

                int islandsAfter; int surfaceFaces;
                Vector3Int cell = VerifyPickSurfaceCell(runtime, record.islandsBefore, out islandsAfter, out surfaceFaces);
                record.islandsAfter = islandsAfter;
                record.surfaceFaces = surfaceFaces;
                record.islandsUnchanged = islandsAfter == record.islandsBefore;
                record.split = islandsAfter > record.islandsBefore;
                record.hitCell = cell.x + "," + cell.y + "," + cell.z;
                record.fragmentsExpected = Mathf.Max(0, islandsAfter - 1);

                float health = runtime.GetHealth(cell);
                record.lethalDamage = health > 0f ? health * 2.0 : 0.0;
                if (record.lethalDamage <= 0.0)
                {
                    record.note = "Selected cell reported no health; damage probe was skipped.";
                    notes.Add(record.note);
                    return record;
                }

                int fragments = 0;
                Action<VoxelStructureRuntime> onFragment = delegate (VoxelStructureRuntime fragment) { fragments++; };
                runtime.FragmentCreated += onFragment;

                var applyTimer = new System.Diagnostics.Stopwatch();
                applyTimer.Start();
                float dealt = runtime.ApplyDamage(cell, (float)record.lethalDamage);
                applyTimer.Stop();
                var flushTimer = new System.Diagnostics.Stopwatch();
                flushTimer.Start();
                runtime.FlushMeshChanges();
                flushTimer.Stop();
                runtime.FragmentCreated -= onFragment;

                record.applyDamageMs = applyTimer.Elapsed.TotalMilliseconds;
                record.flushMs = flushTimer.Elapsed.TotalMilliseconds;
                record.damageMs = record.applyDamageMs + record.flushMs;
                record.dealtDamage = dealt;
                record.fragments = fragments;
                if (!record.islandsUnchanged)
                    notes.Add("Damage probe (" + role + ", '" + record.assetName + "'): the sampled surface cell raised the island count from " + record.islandsBefore + " to " + islandsAfter + ", so this run is a split case and not the single-island surface benchmark.");
                if (surfaceFaces <= 0)
                    notes.Add("Damage probe (" + role + ", '" + record.assetName + "'): the sampled cell has no open face, so it is not a surface cell.");

                VoxelCellState state;
                record.hitCellDestroyed = runtime.Cells.TryGetValue(cell, out state) && state != null && !state.Alive;

                var pieces = container.GetComponentsInChildren<VoxelStructureRuntime>(true);
                int alive = 0, bodies = 0, vertices = 0, renderMeshes = 0;
                for (int i = 0; i < pieces.Length; i++)
                {
                    var piece = pieces[i];
                    alive += piece.AliveCount;
                    if (piece.Body) bodies++;
                    // Render-only chunk children of a damaged Hull object belong to this runtime; they are
                    // never separate runtimes, fragments or bodies.
                    var pieceMeshes = VerifyRenderMeshes(piece);
                    vertices += VerifyRenderMeshVertexCount(pieceMeshes);
                    renderMeshes += pieceMeshes.Count;
                }
                var colliders = container.GetComponentsInChildren<BoxCollider>(true);
                int enabledColliders = 0;
                for (int i = 0; i < colliders.Length; i++) if (colliders[i].enabled) enabledColliders++;

                record.aliveTotal = alive;
                record.expectedAlive = record.cellCount - 1;
                record.aliveConserved = alive == record.expectedAlive;
                record.collidersAfter = enabledColliders;
                record.bodiesAfter = bodies;
                record.verticesAfter = vertices;
                record.renderMeshesAfter = renderMeshes;

                if (!record.hitCellDestroyed)
                    failures.Add("Damage probe (" + role + ", '" + record.assetName + "'): one lethal hit did not destroy cell " + record.hitCell + ".");
                if (!record.aliveConserved)
                    failures.Add("Damage probe (" + role + ", '" + record.assetName + "'): " + alive + " cells survived but " + record.expectedAlive + " were expected.");
                if (record.fragments != record.fragmentsExpected)
                    failures.Add("Damage probe (" + role + ", '" + record.assetName + "'): FlushMeshChanges created " + record.fragments + " fragment(s) but " + record.fragmentsExpected + " were expected from " + islandsAfter + " island(s).");
                if (islandsAfter > 1 && record.bodiesAfter < 2)
                    failures.Add("Damage probe (" + role + ", '" + record.assetName + "'): islands separated but only " + record.bodiesAfter + " rigidbody exists.");
                if (record.collidersAfter <= 0)
                    failures.Add("Damage probe (" + role + ", '" + record.assetName + "'): no enabled collider survived the hit.");
                if (record.bodiesAfter != pieces.Length)
                    notes.Add("Damage probe (" + role + "): " + pieces.Length + " runtime(s) but " + record.bodiesAfter + " rigidbody(ies).");
            }
            catch (Exception e)
            {
                record.note = "Damage probe threw " + e.GetType().Name + ": " + e.Message;
                failures.Add("Damage probe (" + role + ", '" + record.assetName + "') threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                VerifyDestroy(container);
                roots.Remove(container);
            }
            return record;
        }

        // Single-cell surface benchmark: prefer a cell that is on the exterior (has open faces) and
        // whose removal does not raise the six-neighbour island count. Sampling is bounded so a large
        // asset cannot turn this probe into a full connectivity sweep of every candidate cell.
        static Vector3Int VerifyPickSurfaceCell(VoxelStructureRuntime runtime, int islandsBefore, out int islandsAfter, out int surfaceFaces)
        {
            var alive = new HashSet<Vector3Int>(runtime.Cells.Keys);
            islandsAfter = islandsBefore;
            surfaceFaces = 0;
            if (alive.Count == 0) return Vector3Int.zero;
            if (alive.Count == 1)
            {
                islandsAfter = 0;
                surfaceFaces = 6;
                foreach (var only in alive) return only;
            }

            int samples = Mathf.Clamp(alive.Count / 64, 8, 24);
            int stride = Mathf.Max(1, alive.Count / samples);
            var best = Vector3Int.zero;
            int bestScore = int.MinValue, bestIslands = islandsBefore, bestFaces = 0;
            bool found = false;
            int index = 0, tested = 0;

            foreach (var cell in alive)
            {
                if (index++ % stride != 0) continue;
                if (tested++ >= samples) break;

                int open = 0;
                for (int d = 0; d < VoxelGeometry.Directions.Length; d++)
                    if (!alive.Contains(cell + VoxelGeometry.Directions[d])) open++;

                var reduced = new HashSet<Vector3Int>(alive);
                reduced.Remove(cell);
                int islands = reduced.Count == 0 ? 0 : VerifyCountIslands(reduced);

                bool noSplit = islands == islandsBefore;
                int faces = open >= 1 && open <= 4 ? open : (open > 4 ? 1 : 0);
                int score = (noSplit ? 100 : 0) + faces * 4 - Mathf.Max(0, islands - islandsBefore) * 8;
                if (!found || score > bestScore)
                {
                    found = true;
                    bestScore = score;
                    bestIslands = islands;
                    bestFaces = open;
                    best = cell;
                }
            }
            islandsAfter = bestIslands;
            surfaceFaces = bestFaces;
            return best;
        }

        static int VerifyCountIslands(HashSet<Vector3Int> cells)
        {
            var pending = new HashSet<Vector3Int>(cells);
            var queue = new Queue<Vector3Int>();
            int islands = 0;
            foreach (var start in cells)
            {
                if (!pending.Remove(start)) continue;
                islands++;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var position = queue.Dequeue();
                    for (int d = 0; d < VoxelGeometry.Directions.Length; d++)
                    {
                        var next = position + VoxelGeometry.Directions[d];
                        if (pending.Remove(next)) queue.Enqueue(next);
                    }
                }
            }
            return islands;
        }

        // ------------------------------------------------------------------
        // Real-sample cross-section cut (two or more floating segments expected)
        // ------------------------------------------------------------------
        static VerifyCutRecord VerifyCutProbe(VerifyScene scene, List<VoxelAssetDefinition> assets, List<GameObject> roots, List<string> failures, List<string> notes)
        {
            var record = new VerifyCutRecord();
            record.role = "cross-section-cut";
            record.ran = false;
            record.axis = string.Empty;
            record.selection = string.Empty;
            if (scene == null || !scene.valid || assets.Count == 0)
            {
                record.note = "No isolated scene or no asset was available for the real-sample cut.";
                notes.Add(record.note);
                return record;
            }

            string selection;
            var asset = VerifyPickCutAsset(assets, out selection);
            if (!asset)
            {
                record.note = "No asset was available for the real-sample cut.";
                notes.Add(record.note);
                return record;
            }
            record.assetName = asset.name;
            record.selection = selection;

            var container = VerifyContainer(scene, "MR2 cut " + record.assetName, roots);
            try
            {
                var runtime = VerifyInstance(container, scene, asset, Vector3.zero, 1f, 1f, Vector3.zero, Vector3.zero);
                record.ran = true;
                record.cellCount = runtime.AliveCount;
                record.islandsBefore = VoxelGeometry.Islands(runtime.Cells).Count;

                var alive = new HashSet<Vector3Int>(runtime.Cells.Keys);
                Vector3 size = runtime.LocalBounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
                record.axis = axis == 0 ? "x" : (axis == 1 ? "y" : "z");
                record.axisExtent = size[axis];
                float cellSize = Mathf.Max(0.0001f, asset.cellSize);
                float centerCoordinate = runtime.LocalBounds.center[axis] / cellSize;

                // Nearest occupied integer layer to the bounds centre along the long axis.
                int cut = 0;
                float nearest = float.MaxValue;
                var coordinates = new HashSet<int>();
                foreach (var position in alive)
                {
                    int coordinate = position[axis];
                    coordinates.Add(coordinate);
                    float distance = Mathf.Abs(coordinate - centerCoordinate);
                    if (distance < nearest) { nearest = distance; cut = coordinate; }
                }
                record.cutLayer = cut;
                record.layers = 1;

                var cutCells = new List<Vector3Int>();
                foreach (var position in alive) if (position[axis] == cut) cutCells.Add(position);
                var reduced = new HashSet<Vector3Int>(alive);
                for (int i = 0; i < cutCells.Count; i++) reduced.Remove(cutCells[i]);
                int islands = reduced.Count == 0 ? 0 : VerifyCountIslands(reduced);

                if (islands < 2 && coordinates.Count > 1)
                {
                    cutCells.Clear();
                    reduced = new HashSet<Vector3Int>(alive);
                    foreach (var position in alive)
                    {
                        if (Mathf.Abs(position[axis] - cut) > 1) continue;
                        cutCells.Add(position);
                        reduced.Remove(position);
                    }
                    islands = reduced.Count == 0 ? 0 : VerifyCountIslands(reduced);
                    record.layers = 2;
                }
                record.islandsAfter = islands;
                record.fragmentsExpected = Mathf.Max(0, islands - 1);

                float lethal = asset.blockHealth * Mathf.Max(0.01f, runtime.healthMultiplier) * 2f;
                int fragments = 0;
                Action<VoxelStructureRuntime> onFragment = delegate (VoxelStructureRuntime fragment) { fragments++; };
                runtime.FragmentCreated += onFragment;

                int removed = 0;
                var applyTimer = new System.Diagnostics.Stopwatch();
                applyTimer.Start();
                for (int i = 0; i < cutCells.Count; i++)
                    if (runtime.ApplyDamage(cutCells[i], lethal) > 0f) removed++;
                applyTimer.Stop();
                var flushTimer = new System.Diagnostics.Stopwatch();
                flushTimer.Start();
                runtime.FlushMeshChanges();
                flushTimer.Stop();
                runtime.FragmentCreated -= onFragment;

                record.removedCells = removed;
                record.applyDamageMs = applyTimer.Elapsed.TotalMilliseconds;
                record.flushMs = flushTimer.Elapsed.TotalMilliseconds;
                record.damageMs = record.applyDamageMs + record.flushMs;
                record.fragments = fragments;

                var pieces = container.GetComponentsInChildren<VoxelStructureRuntime>(true);
                int aliveTotal = 0, bodies = 0, vertices = 0, renderMeshes = 0;
                for (int i = 0; i < pieces.Length; i++)
                {
                    aliveTotal += pieces[i].AliveCount;
                    if (pieces[i].Body) bodies++;
                    var pieceMeshes = VerifyRenderMeshes(pieces[i]);
                    vertices += VerifyRenderMeshVertexCount(pieceMeshes);
                    renderMeshes += pieceMeshes.Count;
                }
                var colliders = container.GetComponentsInChildren<BoxCollider>(true);
                int enabledColliders = 0;
                for (int i = 0; i < colliders.Length; i++) if (colliders[i].enabled) enabledColliders++;

                record.aliveTotal = aliveTotal;
                record.expectedAlive = record.cellCount - record.removedCells;
                record.aliveConserved = aliveTotal == record.expectedAlive;
                record.collidersAfter = enabledColliders;
                record.bodiesAfter = bodies;
                record.verticesAfter = vertices;
                record.renderMeshesAfter = renderMeshes;
                record.expectationMet = record.islandsAfter >= 2 && record.fragments >= 1;

                if (!record.aliveConserved)
                    failures.Add("Real-sample cut ('" + record.assetName + "'): " + aliveTotal + " cells survived but " + record.expectedAlive + " were expected after removing " + record.removedCells + ".");
                if (record.fragments != record.fragmentsExpected)
                    failures.Add("Real-sample cut ('" + record.assetName + "'): FragmentCreated reported " + record.fragments + " fragment(s) but " + record.fragmentsExpected + " were expected from " + record.islandsAfter + " island(s).");
                if (record.collidersAfter <= 0)
                    failures.Add("Real-sample cut ('" + record.assetName + "'): no enabled collider survived the cut.");
                if (!record.expectationMet)
                    notes.Add("Real-sample cut ('" + record.assetName + "', " + record.axis + "=" + record.cutLayer + "): removed " + record.removedCells + " cell(s) and produced " + record.fragments +
                        " fragment(s), so it did not separate into 2+ floating segments. Reported exactly as measured; the runtime was not changed.");
            }
            catch (Exception e)
            {
                record.note = "Real-sample cut threw " + e.GetType().Name + ": " + e.Message;
                failures.Add("Real-sample cut ('" + record.assetName + "') threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                VerifyDestroy(container);
                roots.Remove(container);
            }
            return record;
        }

        static VoxelAssetDefinition VerifyPickCutAsset(List<VoxelAssetDefinition> assets, out string selection)
        {
            selection = "largest";
            string[] preferred = { "truss", "open", "frame", "cage", "station", "platform" };
            for (int k = 0; k < preferred.Length; k++)
            {
                VoxelAssetDefinition best = null;
                int bestCells = -1;
                for (int i = 0; i < assets.Count; i++)
                {
                    if (!assets[i] || assets[i].cells == null) continue;
                    string text = (assets[i].name ?? string.Empty).ToLowerInvariant();
                    if (text.IndexOf(preferred[k], StringComparison.Ordinal) < 0) continue;
                    if (assets[i].cells.Count > bestCells) { bestCells = assets[i].cells.Count; best = assets[i]; }
                }
                if (best != null) { selection = "name:" + preferred[k]; return best; }
            }
            VoxelAssetDefinition largest = null;
            int largestCells = -1;
            for (int i = 0; i < assets.Count; i++)
            {
                if (!assets[i] || assets[i].cells == null) continue;
                if (assets[i].cells.Count > largestCells) { largestCells = assets[i].cells.Count; largest = assets[i]; }
            }
            return largest;
        }

        // ------------------------------------------------------------------
        // Density probe
        // ------------------------------------------------------------------
        static VerifyDensityRecord VerifyDensityProbe(VoxelAssetDefinition asset, VerifyScene scene, List<GameObject> roots, List<string> failures)
        {
            var record = new VerifyDensityRecord();
            record.assetName = asset ? asset.name : "<missing>";
            record.instances = VerifyDensityCopies;
            if (!asset)
            {
                record.note = "No asset was available for the density probe.";
                return record;
            }

            var container = VerifyContainer(scene, "MR2 density probe", roots);
            try
            {
                float spacing = Mathf.Max(2f, Mathf.Max(asset.LocalBounds.size.x, Mathf.Max(asset.LocalBounds.size.y, asset.LocalBounds.size.z)) + 2f);
                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();
                int firstInstanceMeshes = 0;
                string meshSource = string.Empty;
                string firstSignature = null;
                bool shared = true;
                int colliders = 0;
                int vertices = 0;
                for (int i = 0; i < VerifyDensityCopies; i++)
                {
                    var position = new Vector3((i % 3) * spacing, (i / 3) * spacing, 0f);
                    var runtime = VerifyInstance(container, scene, asset, position, 1f, 1f, Vector3.zero, Vector3.zero);
                    colliders += runtime.ColliderCount;
                    string source;
                    var meshes = VerifyRenderMeshes(runtime, out source);
                    meshSource = source;
                    vertices += VerifyRenderMeshVertexCount(meshes);
                    if (i == 0)
                    {
                        firstInstanceMeshes = meshes.Count;
                        firstSignature = VerifyRenderMeshSignature(meshes);
                    }
                    else if (!string.Equals(firstSignature, VerifyRenderMeshSignature(meshes), StringComparison.Ordinal)) shared = false;
                }
                timer.Stop();

                record.totalInitMs = timer.Elapsed.TotalMilliseconds;
                record.averageInitMs = record.totalInitMs / VerifyDensityCopies;
                record.colliders = colliders;
                record.vertices = vertices;
                record.sharedMesh = shared;
                record.meshSource = meshSource;
                record.renderMeshesPerInstance = firstInstanceMeshes;
                record.cellCount = asset.cells != null ? asset.cells.Count * VerifyDensityCopies : 0;
                record.note = "Repeated instance build cost only; this is not a frame time and not a map budget.";
                if (!shared) failures.Add("Density probe: repeated instances of '" + record.assetName + "' did not share one template mesh.");
                if (colliders <= 0) failures.Add("Density probe: instances of '" + record.assetName + "' produced no colliders.");
            }
            catch (Exception e)
            {
                record.note = "Density probe threw " + e.GetType().Name + ": " + e.Message;
                failures.Add("Density probe threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                VerifyDestroy(container);
                roots.Remove(container);
            }
            return record;
        }

        // ------------------------------------------------------------------
        // Isolated physics
        // ------------------------------------------------------------------
        static VerifyPhysicsRecord VerifyPhysics(VerifyScene scene, VoxelAssetDefinition lightest, List<GameObject> roots, List<VoxelAssetDefinition> synthetic, List<string> unsupported, List<string> failures, List<string> notes)
        {
            var record = new VerifyPhysicsRecord();
            record.status = "unsupported";

            if (scene == null || !scene.valid)
            {
                record.note = "No isolated scene was available; physics simulation was not attempted.";
                unsupported.Add(record.note);
                return record;
            }
            if (!scene.previewPhysics)
            {
                record.note = "Edit mode has no isolated physics without an Editor preview scene: SceneManager.CreateScene refuses to run outside Play mode and EditorSceneManager.NewScene always shares Physics.defaultPhysicsScene.";
                unsupported.Add(record.note);
                return record;
            }
            var physics = scene.physics;
            if (!physics.IsValid())
            {
                record.note = "The preview scene's PhysicsScene is invalid; no simulation was run.";
                unsupported.Add(record.note);
                return record;
            }
            if (!lightest)
            {
                record.note = "No valid scenery asset was available to drift; physics simulation was skipped.";
                unsupported.Add(record.note);
                return record;
            }

            record.localPhysicsScene = true;
            record.previewScenePhysics = true;
            var container = VerifyContainer(scene, "MR2 physics", roots);
            try
            {
                string gateNote;
                record.isolationProven = VerifyIsolationGate(scene, out record.guardPreviewMoved, out record.guardDefaultMoved, out gateNote);
                if (!record.isolationProven)
                {
                    record.status = "unsupported";
                    record.note = gateNote;
                    unsupported.Add(gateNote);
                    return record;
                }

                var drift = VerifyInstance(container, scene, lightest, new Vector3(0f, 60f, 0f), 1f, 1f, Vector3.right * 2f, Vector3.zero);
                var still = VerifyInstance(container, scene, lightest, new Vector3(0f, 120f, 0f), 1f, 1f, Vector3.zero, Vector3.zero);
                record.driftAsset = lightest.name;
                record.driftAliveBefore = drift.AliveCount;

                var moverAsset = VerifySyntheticCube("MR2 physics mover");
                synthetic.Add(moverAsset);
                var targetAsset = VerifySyntheticCube("MR2 physics target");
                synthetic.Add(targetAsset);

                var target = VerifyInstance(container, scene, targetAsset, Vector3.zero, 1f, 1f, Vector3.zero, Vector3.zero);
                record.collisionAliveBefore = target.AliveCount;

                Vector3 targetCenter = VerifyWorldBoundsCenter(target);
                float targetHalf = Mathf.Max(0.5f, target.LocalBounds.extents.x);
                float moverHalf = 0.6f;
                float distance = targetHalf + moverHalf + 0.35f;
                float speed = Mathf.Clamp(distance / 0.4f, 4f, 120f);
                var mover = VerifyInstance(container, scene, moverAsset, new Vector3(targetCenter.x - distance, targetCenter.y, targetCenter.z), 1f, 1f, Vector3.right * speed, Vector3.zero);

                Vector3 driftStart = drift.Body ? drift.Body.position : drift.transform.position;
                Vector3 stillStart = still.Body ? still.Body.position : still.transform.position;
                Vector3 targetStart = target.Body ? target.Body.position : target.transform.position;

                if (drift.Body) drift.Body.WakeUp();
                if (still.Body) still.Body.WakeUp();
                if (target.Body) target.Body.WakeUp();
                if (mover.Body) mover.Body.WakeUp();

                var nan = record.nan;
                int bodiesChecked = 0;
                for (int step = 1; step <= VerifyPhysicsSteps; step++)
                {
                    physics.Simulate(VerifyPhysicsStep);
                    if (step % 10 == 0)
                    {
                        bodiesChecked = 0;
                        var bodies = container.GetComponentsInChildren<Rigidbody>(true);
                        for (int i = 0; i < bodies.Length; i++)
                        {
                            bodiesChecked++;
                            string problem = VerifyBodyProblem(bodies[i]);
                            if (problem != null && nan.Count < 16)
                                nan.Add(bodies[i].name + ": " + problem + " at step " + step);
                        }
                    }
                }
                record.bodiesChecked = bodiesChecked;
                record.nanChecks = VerifyPhysicsSteps / 10;

                Vector3 driftEnd = drift.Body ? drift.Body.position : drift.transform.position;
                Vector3 stillEnd = still.Body ? still.Body.position : still.transform.position;
                Vector3 targetEnd = target.Body ? target.Body.position : target.transform.position;

                record.driftExpectedDistance = 2f * VerifyPhysicsStep * VerifyPhysicsSteps;
                record.driftDistance = driftEnd.x - driftStart.x;
                record.driftLateralError = Mathf.Max(Mathf.Abs(driftEnd.y - driftStart.y), Mathf.Abs(driftEnd.z - driftStart.z));
                record.driftAliveAfter = drift.AliveCount;
                record.stationaryDisplacement = (stillEnd - stillStart).magnitude;
                record.collisionTargetSpeed = target.Body ? target.Body.linearVelocity.magnitude : 0f;
                record.collisionTargetDisplacement = (targetEnd - targetStart).magnitude;
                record.collisionAliveAfter = target.AliveCount;

                record.stepped = Mathf.Abs(record.driftDistance) > Mathf.Max(0.05f, record.driftExpectedDistance * 0.1f);
                if (!record.stepped)
                {
                    record.status = "unsupported";
                    record.note = "PhysX did not advance in edit mode (expected about " + VerifyNumber(record.driftExpectedDistance) + " m of drift, measured " + VerifyNumber(record.driftDistance) + " m). Physics results are not claimed as a pass.";
                    unsupported.Add(record.note);
                    return record;
                }

                bool gravityFree = true;
                var allBodies = container.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < allBodies.Length; i++) if (allBodies[i].useGravity) { gravityFree = false; break; }
                record.gravityFree = gravityFree;
                record.allBodiesFinite = nan.Count == 0;

                if (!gravityFree) failures.Add("Physics: at least one scenery body has useGravity enabled.");
                if (!record.allBodiesFinite) failures.Add("Physics: non-finite body state detected in " + nan.Count + " place(s).");
                if (record.driftAliveAfter != record.driftAliveBefore) failures.Add("Physics: drifting asset lost cells during simulation.");
                if (record.collisionAliveAfter != record.collisionAliveBefore) failures.Add("Physics: collision target lost cells during simulation.");

                float driftTolerance = Mathf.Max(0.15f, record.driftExpectedDistance * 0.08f);
                if (Mathf.Abs(record.driftDistance - record.driftExpectedDistance) > driftTolerance)
                    failures.Add("Physics: free body drifted " + VerifyNumber(record.driftDistance) + " m instead of " + VerifyNumber(record.driftExpectedDistance) + " m with no gravity.");
                if (record.driftLateralError > 0.05f)
                    failures.Add("Physics: free body moved " + VerifyNumber(record.driftLateralError) + " m off-axis while drifting.");
                if (record.stationaryDisplacement > 0.02f)
                    failures.Add("Physics: a zero-velocity body moved " + VerifyNumber(record.stationaryDisplacement) + " m, so gravity is leaking in.");
                if (record.collisionTargetSpeed <= 0.01f && record.collisionTargetDisplacement <= 0.02f)
                    failures.Add("Physics: a controlled mover at " + VerifyNumber(speed) + " m/s did not push the target body.");

                // Real content: a scaled synthetic mover hits the lightest real scenery asset.
                var pushTargetAsset = lightest;
                var pushTarget = VerifyInstance(container, scene, pushTargetAsset, new Vector3(0f, -60f, 0f), 0.5f, 1f, Vector3.zero, Vector3.zero);
                float pushScale = 0.5f;
                float pushTargetMass = pushTarget.Body ? pushTarget.Body.mass : 0f;
                float moverScale = Mathf.Clamp(Mathf.Pow(Mathf.Max(0.01f, pushTargetMass) * 2f / 100f, 1f / 3f), 1f, 12f);
                Vector3 pushCenter = VerifyWorldBoundsCenter(pushTarget);
                float pushHalfX = Mathf.Max(0.25f, pushTarget.LocalBounds.extents.x * pushScale);
                float pushDistance = pushHalfX + moverScale * 0.5f + 0.4f;
                float pushSpeed = Mathf.Clamp(pushDistance / 0.35f, 6f, 400f);
                var pusher = VerifyInstance(container, scene, moverAsset, new Vector3(pushCenter.x - pushDistance, pushCenter.y, pushCenter.z), moverScale, 1f, Vector3.right * pushSpeed, Vector3.zero);
                if (pusher.Body) pusher.Body.WakeUp();
                if (pushTarget.Body) pushTarget.Body.WakeUp();

                Vector3 pushStart = pushTarget.Body ? pushTarget.Body.position : pushTarget.transform.position;
                Vector3 pusherStart = pusher.Body ? pusher.Body.position : pusher.transform.position;
                float pusherMass = pusher.Body ? pusher.Body.mass : 0f;
                for (int step = 0; step < VerifyPhysicsSteps; step++) physics.Simulate(VerifyPhysicsStep);
                Vector3 pushEnd = pushTarget.Body ? pushTarget.Body.position : pushTarget.transform.position;
                record.realAssetPushTested = true;
                record.realPushMoverMass = pusherMass;
                record.realPushTargetMass = pushTargetMass;
                record.realPushTargetSpeed = pushTarget.Body ? pushTarget.Body.linearVelocity.magnitude : 0f;
                record.realPushTargetDisplacement = (pushEnd - pushStart).magnitude;

                string pusherProblem = VerifyBodyProblem(pusher.Body);
                string pushTargetProblem = VerifyBodyProblem(pushTarget.Body);
                if (pusherProblem != null) failures.Add("Physics: real-content pusher became invalid: " + pusherProblem);
                if (pushTargetProblem != null) failures.Add("Physics: real-content target became invalid: " + pushTargetProblem);
                if (record.realPushTargetSpeed <= 0.001f && record.realPushTargetDisplacement <= 0.01f)
                    failures.Add("Physics: a " + VerifyNumber(pusherMass) + " kg mover at " + VerifyNumber(pushSpeed) + " m/s did not move the real asset ('" + pushTargetAsset.name + "', " + VerifyNumber(pushTargetMass) + " kg).");

                record.findings.Add("drift: " + VerifyNumber(record.driftDistance) + " m of " + VerifyNumber(record.driftExpectedDistance) + " m expected");
                record.findings.Add("lateral error: " + VerifyNumber(record.driftLateralError) + " m");
                record.findings.Add("stationary displacement: " + VerifyNumber(record.stationaryDisplacement) + " m");
                record.findings.Add("controlled collision: target speed " + VerifyNumber(record.collisionTargetSpeed) + " m/s");
                record.findings.Add("real asset push: mover " + VerifyNumber(pusherMass) + " kg -> '" + pushTargetAsset.name + "' " + VerifyNumber(pushTargetMass) + " kg, target speed " + VerifyNumber(record.realPushTargetSpeed) + " m/s");
                record.findings.Add("pusher travelled from " + VerifyNumber(pusherStart.x) + " to " + VerifyNumber(pusher.Body ? pusher.Body.position.x : pusherStart.x));
                record.note = "Manual 120 x 1/60 s steps on an Editor preview scene PhysicsScene that is separate from Physics.defaultPhysicsScene (isolation gate passed); no gameplay, damage, AI or rendering involved.";
                record.status = "passed";
            }
            catch (Exception e)
            {
                record.status = "unsupported";
                record.note = "Physics simulation could not be completed in edit mode: " + e.GetType().Name + ": " + e.Message;
                unsupported.Add(record.note);
                notes.Add(record.note);
            }
            finally
            {
                VerifyDestroy(container);
                roots.Remove(container);
            }
            return record;
        }

        // Shared isolation gate: a body in the staging scene lives in Physics.defaultPhysicsScene, so it
        // must stay still while only the preview scene's physics is stepped. Every physics probe reuses it.
        static bool VerifyIsolationGate(VerifyScene scene, out float previewMoved, out float defaultMoved, out string note)
        {
            previewMoved = 0f;
            defaultMoved = 0f;
            note = string.Empty;
            GameObject stagingGuard = null;
            GameObject previewGuard = null;
            try
            {
                var physics = scene.physics;
                stagingGuard = new GameObject("MR2 default-scene guard");
                if (stagingGuard.scene != scene.staging && scene.staging.IsValid()) SceneManager.MoveGameObjectToScene(stagingGuard, scene.staging);
                stagingGuard.transform.position = new Vector3(0f, -300f, 0f);
                var stagingBody = stagingGuard.AddComponent<Rigidbody>();
                stagingBody.useGravity = false;
                stagingBody.linearVelocity = Vector3.right * 6f;
                stagingBody.WakeUp();

                previewGuard = new GameObject("MR2 preview-scene guard");
                if (previewGuard.scene != scene.scene) SceneManager.MoveGameObjectToScene(previewGuard, scene.scene);
                previewGuard.transform.position = new Vector3(0f, 300f, 0f);
                var previewBody = previewGuard.AddComponent<Rigidbody>();
                previewBody.useGravity = false;
                previewBody.linearVelocity = Vector3.right * 6f;
                previewBody.WakeUp();

                Vector3 defaultStart = stagingBody.position;
                Vector3 previewStart = previewBody.position;
                for (int i = 0; i < 6; i++) physics.Simulate(VerifyPhysicsStep);
                defaultMoved = (stagingBody.position - defaultStart).magnitude;
                previewMoved = (previewBody.position - previewStart).magnitude;
                bool isolated = previewMoved > 0.05f && defaultMoved < 0.001f;
                if (!isolated)
                    note = "Isolation gate failed: stepping the preview scene moved " + VerifyNumber(previewMoved) +
                        " m on the preview body and " + VerifyNumber(defaultMoved) +
                        " m on a body in the default physics scene, so no physics result is claimed.";
                return isolated;
            }
            catch (Exception e)
            {
                note = "Isolation gate threw " + e.GetType().Name + ": " + e.Message;
                return false;
            }
            finally
            {
                if (previewGuard) UnityEngine.Object.DestroyImmediate(previewGuard);
                if (stagingGuard) UnityEngine.Object.DestroyImmediate(stagingGuard);
            }
        }

        // Mirrors SpaceMapRuntime.Build: child localPosition / Euler(localRotation) / uniform localScale,
        // then healthMultiplier, AuthoringId, driftVelocity and angularVelocity before Initialize.
        static VoxelStructureRuntime VerifyMapInstance(VerifyScene scene, Transform parent, VerifyLayoutInstance item)
        {
            var go = new GameObject(string.IsNullOrEmpty(item.label) ? item.asset.name : item.label);
            if (parent) go.transform.SetParent(parent, false);
            if (scene != null && scene.valid && scene.scene.IsValid() && go.scene != scene.scene)
                SceneManager.MoveGameObjectToScene(go, scene.scene);
            go.transform.localPosition = item.position;
            go.transform.localRotation = Quaternion.Euler(item.eulerAngles);
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, item.scale);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            VerifyKeepTinyFragmentsPhysical(runtime);
            runtime.healthMultiplier = Mathf.Max(0.01f, item.healthMultiplier);
            runtime.AuthoringId = item.id;
            runtime.driftVelocity = item.driftVelocity;
            runtime.angularVelocity = item.angularVelocity;
            runtime.Initialize(item.asset);
            return runtime;
        }

        static Bounds VerifyWorldBounds(VoxelStructureRuntime runtime)
        {
            var local = runtime.LocalBounds;
            var body = runtime.Body;
            Vector3 position = body ? body.position : runtime.transform.position;
            Quaternion rotation = body ? body.rotation : runtime.transform.rotation;
            var matrix = Matrix4x4.TRS(position, rotation, runtime.transform.lossyScale);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z);
                Vector3 world = matrix.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
            return new Bounds((min + max) * 0.5f, max - min);
        }

        static int VerifyOverlapPairs(Bounds[] boxes)
        {
            int pairs = 0;
            for (int i = 0; i < boxes.Length; i++)
                for (int j = i + 1; j < boxes.Length; j++)
                    if (boxes[i].Intersects(boxes[j])) pairs++;
            return pairs;
        }

        static int VerifyNewOverlapPairs(Bounds[] before, Bounds[] after)
        {
            int pairs = 0;
            for (int i = 0; i < before.Length; i++)
                for (int j = i + 1; j < before.Length; j++)
                    if (!before[i].Intersects(before[j]) && after[i].Intersects(after[j])) pairs++;
            return pairs;
        }

        // Whole sample zone: every authored layout instance, one isolated preview-scene physics world,
        // 120 manual steps. No rendering, no AI, no gameplay; timings are PhysX CPU, never FPS.
        static VerifyPatchPhysicsRecord VerifyPatchPhysics(VerifyScene scene, List<VerifyLayoutInstance> instances, int expectedCells, List<GameObject> roots, List<string> unsupported, List<string> failures, List<string> notes)
        {
            var record = new VerifyPatchPhysicsRecord();
            record.instances = instances != null ? instances.Count : 0;
            if (scene == null || !scene.valid || !scene.previewPhysics)
            {
                record.note = "No isolated preview scene was available; whole-zone patch physics was not attempted.";
                unsupported.Add(record.note);
                return record;
            }
            if (instances == null || instances.Count == 0)
            {
                record.note = "The layout has no instances, so whole-zone patch physics was skipped.";
                notes.Add(record.note);
                return record;
            }
            var physics = scene.physics;
            if (!physics.IsValid())
            {
                record.note = "The preview scene PhysicsScene is invalid; whole-zone patch physics was skipped.";
                unsupported.Add(record.note);
                return record;
            }

            string gateNote;
            record.isolationProven = VerifyIsolationGate(scene, out record.guardPreviewMoved, out record.guardDefaultMoved, out gateNote);
            if (!record.isolationProven)
            {
                record.note = gateNote;
                unsupported.Add(gateNote);
                return record;
            }

            var container = VerifyContainer(scene, "MR2 patch physics", roots);
            try
            {
                var runtimes = new List<VoxelStructureRuntime>(instances.Count);
                var meshByAsset = new Dictionary<VoxelAssetDefinition, string>();
                var uniqueMeshIds = new HashSet<int>();
                int warmInstances = 0;
                var initTimer = new System.Diagnostics.Stopwatch();
                initTimer.Start();
                for (int i = 0; i < instances.Count; i++)
                {
                    var item = instances[i];
                    if (item == null || !item.asset) continue;
                    var itemTimer = System.Diagnostics.Stopwatch.StartNew();
                    var runtime = VerifyMapInstance(scene, container.transform, item);
                    itemTimer.Stop();
                    double ms = itemTimer.Elapsed.TotalMilliseconds;
                    bool warm = meshByAsset.ContainsKey(item.asset);
                    if (warm) { record.warmInitMs += ms; warmInstances++; }
                    else record.firstPerAssetInitMs += ms;
                    var meshes = VerifyRenderMeshes(runtime);
                    for (int m = 0; m < meshes.Count; m++) uniqueMeshIds.Add(meshes[m].GetInstanceID());
                    string meshSignature = VerifyRenderMeshSignature(meshes);
                    if (!warm) meshByAsset.Add(item.asset, meshSignature);
                    if (string.Equals(meshByAsset[item.asset], meshSignature, StringComparison.Ordinal)) record.sharedMeshInstances++;
                    runtimes.Add(runtime);
                    record.totalCells += runtime.AliveCount;
                    record.totalColliders += runtime.ColliderCount;
                }
                initTimer.Stop();
                record.initTotalMs = initTimer.Elapsed.TotalMilliseconds;
                record.warmInitAvgMs = warmInstances > 0 ? record.warmInitMs / warmInstances : 0.0;
                record.distinctAssets = meshByAsset.Count;
                record.uniqueMeshes = uniqueMeshIds.Count;
                record.bodies = container.GetComponentsInChildren<Rigidbody>(true).Length;

                int count = runtimes.Count;
                var before = new Bounds[count];
                var startPositions = new Vector3[count];
                bool gravityFree = true;
                for (int i = 0; i < count; i++)
                {
                    before[i] = VerifyWorldBounds(runtimes[i]);
                    startPositions[i] = runtimes[i].Body ? runtimes[i].Body.position : runtimes[i].transform.position;
                    if (!runtimes[i].Body || runtimes[i].Body.useGravity) gravityFree = false;
                }
                record.gravityFreeAll = gravityFree;
                record.overlapPairsBefore = VerifyOverlapPairs(before);
                var patch = count > 0 ? before[0] : new Bounds();
                for (int i = 1; i < count; i++) patch.Encapsulate(before[i]);
                record.patchExtentX = patch.size.x;
                record.patchExtentY = patch.size.y;
                record.patchExtentZ = patch.size.z;
                record.driftMargin = Mathf.Max(10f, Mathf.Max(patch.size.x, Mathf.Max(patch.size.y, patch.size.z)) * 0.05f);
                var allowed = new Bounds(patch.center, patch.size + Vector3.one * (record.driftMargin * 2f));

                var stepTimer = new System.Diagnostics.Stopwatch();
                for (int step = 1; step <= VerifyPhysicsSteps; step++)
                {
                    stepTimer.Restart();
                    physics.Simulate(VerifyPhysicsStep);
                    stepTimer.Stop();
                    double ms = stepTimer.Elapsed.TotalMilliseconds;
                    record.stepTotalMs += ms;
                    if (ms > record.stepMaxMs) record.stepMaxMs = ms;
                    if (step % 20 == 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            record.finiteChecks++;
                            string problem = VerifyBodyProblem(runtimes[i].Body);
                            if (problem != null && record.nan.Count < 12)
                                record.nan.Add(runtimes[i].name + ": " + problem + " at step " + step);
                        }
                    }
                }
                record.stepAvgMs = record.stepTotalMs / VerifyPhysicsSteps;
                record.nanCount = record.nan.Count;

                var after = new Bounds[count];
                int moved = 0, outside = 0;
                float maxDisplacement = 0f;
                for (int i = 0; i < count; i++)
                {
                    after[i] = VerifyWorldBounds(runtimes[i]);
                    Vector3 position = runtimes[i].Body ? runtimes[i].Body.position : runtimes[i].transform.position;
                    float displacement = (position - startPositions[i]).magnitude;
                    if (displacement > 0.001f) moved++;
                    if (displacement > maxDisplacement) maxDisplacement = displacement;
                    if (!allowed.Contains(position)) outside++;
                }
                record.movedBodies = moved;
                record.maxDisplacement = maxDisplacement;
                record.outOfBoundsBodies = outside;
                record.overlapPairsAfter = VerifyOverlapPairs(after);
                record.newOverlapPairs = VerifyNewOverlapPairs(before, after);

                bool bodiesComplete = count > 0 && count == record.instances && record.bodies == count;
                bool cellsComplete = expectedCells > 0 ? record.totalCells == expectedCells : record.totalCells > 0;
                bool finite = record.nan.Count == 0;
                record.status = bodiesComplete && cellsComplete && finite && gravityFree ? "passed" : "failed";
                record.note = "Whole sample zone (" + count + " instances, " + record.totalCells + " cells, " + record.totalColliders +
                    " colliders) stepped " + VerifyPhysicsSteps + " x 1/60 s on an isolated preview-scene physics world with no rendering, AI or gameplay. " +
                    "firstPerAssetInitMs covers the first copy per asset within this probe, not a cold template build; earlier asset validation has already warmed the cache. stepAvgMs/stepMaxMs are PhysX CPU per step, not frame time and not FPS. Overlap counts compare world-AABB pairs before and after, an approximation that inflates under spin.";
                if (!bodiesComplete) failures.Add("Patch physics: " + count + " runtimes but " + record.bodies + " rigidbodies in the patch.");
                if (!cellsComplete) failures.Add("Patch physics: " + record.totalCells + " cells but " + expectedCells + " were authored in the layout.");
                if (!finite) failures.Add("Patch physics: non-finite body state detected (" + record.nanCount + " finding(s)).");
                if (!gravityFree) failures.Add("Patch physics: at least one patch body has useGravity enabled.");
            }
            catch (Exception e)
            {
                record.status = "failed";
                record.note = "Patch physics threw " + e.GetType().Name + ": " + e.Message;
                failures.Add(record.note);
            }
            finally
            {
                VerifyDestroy(container);
                roots.Remove(container);
            }
            return record;
        }
        static VoxelAssetDefinition VerifySyntheticCube(string name)
        {
            var asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            asset.name = name;
            asset.category = "verification";
            asset.cellSize = 1f;
            asset.blockHealth = 100f;
            asset.damageResetSeconds = 2f;
            asset.massPerCell = 100f;
            asset.fragmentSeparationSpeed = 0.5f;
            asset.surfaces = new Material[0];
            asset.surfaceColors = new List<Color> { Color.white };
            asset.cells = new List<VoxelCell> { new VoxelCell { position = Vector3Int.zero, shape = BlockShape.Cube, quarterTurns = Vector3Int.zero, modelOffset = Vector3Int.zero } };
            return asset;
        }

        static Vector3 VerifyWorldBoundsCenter(VoxelStructureRuntime runtime)
        {
            if (!runtime) return Vector3.zero;
            Vector3 position = runtime.Body ? runtime.Body.position : runtime.transform.position;
            float scale = Mathf.Abs(runtime.transform.lossyScale.x);
            return position + runtime.LocalBounds.center * scale;
        }

        static string VerifyBodyProblem(Rigidbody body)
        {
            if (!body) return "body is missing";
            if (!VerifyFinite(body.position)) return "position is not finite";
            if (!VerifyFinite(body.rotation.x) || !VerifyFinite(body.rotation.y) || !VerifyFinite(body.rotation.z) || !VerifyFinite(body.rotation.w)) return "rotation is not finite";
            if (!VerifyFinite(body.linearVelocity)) return "linearVelocity is not finite";
            if (!VerifyFinite(body.angularVelocity)) return "angularVelocity is not finite";
            if (!VerifyFinite(body.mass)) return "mass is not finite";
            if (!VerifyFinite(body.worldCenterOfMass)) return "worldCenterOfMass is not finite";
            return null;
        }

        static bool VerifyFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool VerifyFinite(Vector3 value)
        {
            return VerifyFinite(value.x) && VerifyFinite(value.y) && VerifyFinite(value.z);
        }

        // ------------------------------------------------------------------
        // Source integrity
        // ------------------------------------------------------------------
        static VerifySourceSnapshot VerifyCaptureSource(List<VoxelAssetDefinition> assets, List<string> paths, string folder)
        {
            var snapshot = new VerifySourceSnapshot();
            var json = new StringBuilder();
            var files = new StringBuilder();
            for (int i = 0; i < assets.Count; i++)
            {
                string text = assets[i] ? JsonUtility.ToJson(assets[i]) : string.Empty;
                string hash = VerifySha256(Encoding.UTF8.GetBytes(text));
                snapshot.jsonHash.Add(hash);
                snapshot.jsonLength.Add(text.Length);
                string path = i < paths.Count ? paths[i] : string.Empty;
                string fileHash = VerifyFileHash(path);
                snapshot.fileHash.Add(fileHash);
                json.Append(assets[i] ? assets[i].name : "<missing>").Append(':').Append(hash).Append('|');
                files.Append(path).Append(':').Append(fileHash).Append('|');
            }
            snapshot.combinedJsonHash = VerifySha256(Encoding.UTF8.GetBytes(json.ToString()));
            snapshot.combinedFileHash = VerifySha256(Encoding.UTF8.GetBytes(files.ToString()));
            VerifyCollectAssetFiles(folder, snapshot.files);
            return snapshot;
        }

        static void VerifyCollectAssetFiles(string folder, List<string> output)
        {
            output.Clear();
            try
            {
                string full = VerifyFullPath(folder);
                if (!Directory.Exists(full)) return;
                string[] found = Directory.GetFiles(full, "*.asset", SearchOption.AllDirectories);
                string root = VerifyProjectRoot().Replace('\\', '/').TrimEnd('/') + "/";
                for (int i = 0; i < found.Length; i++)
                {
                    string normalized = found[i].Replace('\\', '/');
                    if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(root.Length);
                    output.Add(normalized);
                }
                output.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                output.Clear();
            }
        }

        static void VerifyCompareSource(VerifyReport report, VerifySourceSnapshot before, List<VoxelAssetDefinition> assets, List<string> paths, string folder, List<VoxelAssetDefinition> cacheBefore, bool cacheProbeOk)
        {
            var source = report.source;
            var after = VerifyCaptureSource(assets, paths, folder);

            source.combinedJsonHashBefore = before.combinedJsonHash;
            source.combinedJsonHashAfter = after.combinedJsonHash;
            source.combinedFileHashBefore = before.combinedFileHash;
            source.combinedFileHashAfter = after.combinedFileHash;
            source.jsonUnchanged = string.Equals(before.combinedJsonHash, after.combinedJsonHash, StringComparison.Ordinal);
            source.filesUnchanged = string.Equals(before.combinedFileHash, after.combinedFileHash, StringComparison.Ordinal);
            source.assetFilesBefore = before.files.Count;
            source.assetFilesAfter = after.files.Count;

            bool sameFiles = before.files.Count == after.files.Count;
            if (sameFiles)
            {
                for (int i = 0; i < before.files.Count; i++)
                    if (!string.Equals(before.files[i], after.files[i], StringComparison.OrdinalIgnoreCase)) { sameFiles = false; break; }
            }
            source.fileSetUnchanged = sameFiles;

            int changed = 0;
            for (int i = 0; i < assets.Count; i++)
            {
                var entry = new VerifySourceEntry();
                entry.name = assets[i] ? assets[i].name : "<missing>";
                entry.path = i < paths.Count ? paths[i] : string.Empty;
                entry.jsonHashBefore = before.jsonHash[i];
                entry.jsonHashAfter = after.jsonHash[i];
                entry.fileHashBefore = before.fileHash[i];
                entry.fileHashAfter = after.fileHash[i];
                entry.jsonLengthBefore = before.jsonLength[i];
                entry.jsonLengthAfter = after.jsonLength[i];
                entry.jsonEqual = string.Equals(entry.jsonHashBefore, entry.jsonHashAfter, StringComparison.Ordinal);
                entry.fileEqual = string.Equals(entry.fileHashBefore, entry.fileHashAfter, StringComparison.Ordinal);
                entry.unchanged = entry.jsonEqual && entry.fileEqual;
                if (!entry.unchanged) changed++;
                source.entries.Add(entry);
            }
            source.changedAssets = changed;
            source.cacheEntriesBefore = cacheBefore.Count;
            source.cacheProbeSupported = cacheProbeOk;
            source.templateCacheCleared = false;
            source.integrityPassed = source.jsonUnchanged && source.filesUnchanged && source.fileSetUnchanged && changed == 0;
            source.note = "JsonUtility.ToJson(VoxelAssetDefinition) and the .asset file bytes were hashed before and after all runtime tests. VoxelTemplateCache was never cleared; entries that existed before the run must still exist afterwards.";
            if (!source.integrityPassed)
                report.failures.Add("Source assets changed during verification (changed assets: " + changed + ", json equal: " + source.jsonUnchanged + ", files equal: " + source.filesUnchanged + ", file set equal: " + source.fileSetUnchanged + ").");
        }

        // ------------------------------------------------------------------
        // Report finishing
        // ------------------------------------------------------------------
        static void VerifyFillLimits(VerifyReport report)
        {
            var limits = report.limits;
            limits.Add("Timings are single-run Editor edit-mode CPU milliseconds. They are not frame times, not FPS and not a sustained-load benchmark.");
            limits.Add("Every station-like asset (all Station_* pieces, capped at " + VerifyMaxStationProbes + ") plus the largest rock and debris asset get the single-cell surface benchmark; only one asset gets the full cross-section cut. The remaining assets are measured for validation, islands, mesh, colliders and build cost only.");
            limits.Add("The cross-section cut removes the one or two integer layers nearest the bounds centre along the asset's longest local axis. A cut that produces no fragment is reported as measured, not retried or patched.");
            limits.Add("Physics is a manual " + VerifyPhysicsSteps + " x 1/60 s step loop. Edit mode cannot create a LocalPhysicsMode scene (SceneManager.CreateScene is play-mode only), so it uses the Editor preview scene's own PhysicsScene, gated by a proof that a body in the default physics scene does not move. Body poses are read from Rigidbody because edit mode does not write simulation back to Transform.");
            limits.Add("Whole-zone patch physics instantiates every layout instance (56 bodies in the current patch) in one isolated preview-scene physics world with no rendering, AI or gameplay. Its step timing is PhysX CPU per step, not frame time and not FPS, and its overlap counts compare world-AABB pairs, which inflates under spin.");
            limits.Add("VoxelTemplateCache is never cleared globally, so an asset whose template was already built by an open editor preview reports a warm first build. Every asset record states templateCachedAtStart.");
            limits.Add("ColliderCount is the merged compound box count from VoxelGeometry.CollisionBoxes, not a physics-world budget for a whole map.");
            limits.Add("Mesh metrics (meshVertexCount, meshSubMeshCount, meshIndexCount, verticesAfter, density.vertices, uniqueMeshes) are taken from the actual VoxelStructureRuntime.RenderMeshes without estimating: one shared intact aggregate mesh, or several render-only chunk children on the same physical root after lethal damage. Chunk children are never counted as runtimes, fragments or bodies. A runtime without RenderMeshes uses the root MeshFilter fallback, which asset and density records state in meshSource.");
            limits.Add("The optional tiny-fragment render-only policy is OFF (tinyFragmentMaxCells = 0) for every probe, so fragment counts, bodies and cell conservation stay fully physical.");
            limits.Add("Layout density buckets are a coarse " + VerifyLayoutGrid + " x " + VerifyLayoutGrid + " x " + VerifyLayoutGrid + " grid over authored instance bounds and describe authored content, not runtime fragments.");
            limits.Add("Source integrity covers JsonUtility JSON and .asset file bytes only; materials, meshes, prefabs and scenes are not audited.");
            limits.Add("Verification requires an asset folder that exists in the project; it does not create, import or save any Unity asset.");
        }

        static void VerifyFillBaseline(VerifyReport report)
        {
            report.baseline = "Baseline = the numbers below, captured once in Unity " + report.environment.unityVersion + " Editor edit mode on " + report.generatedUtc +
                ". Compare later runs against assets[].cellCount/islandCount/meshVertexCount/colliderCount/firstBuildMs/repeatBuildMs, representatives[].damageMs/fragments/collidersAfter, density.averageInitMs and physics.*. " +
                "Editor milliseconds are build/one-shot CPU costs, never FPS.";
            var metrics = report.baselineMetrics;
            for (int i = 0; i < report.assets.Count; i++)
            {
                var asset = report.assets[i];
                metrics.Add("asset " + asset.name + ": cells=" + asset.cellCount + " islands=" + asset.islandCount + " verts=" + asset.meshVertexCount +
                    " colliders=" + asset.colliderCount + " firstMs=" + VerifyNumber(asset.firstBuildMs) + " repeatMs=" + VerifyNumber(asset.repeatBuildMs) +
                    " sharedMesh=" + asset.sharedMeshReused);
            }
            for (int i = 0; i < report.representatives.Count; i++)
            {
                var damage = report.representatives[i];
                metrics.Add("damage " + damage.role + " " + damage.assetName + ": islandsBefore=" + damage.islandsBefore + " islandsAfter=" + damage.islandsAfter +
                    " fragments=" + damage.fragments + " flushMs=" + VerifyNumber(damage.flushMs) + " collidersAfter=" + damage.collidersAfter + " bodiesAfter=" + damage.bodiesAfter);
            }
            if (report.cut != null && report.cut.ran)
                metrics.Add("cut " + report.cut.assetName + " (" + report.cut.selection + ", " + report.cut.axis + "=" + report.cut.cutLayer + "): removed=" + report.cut.removedCells +
                    " islandsAfter=" + report.cut.islandsAfter + " fragments=" + report.cut.fragments + " flushMs=" + VerifyNumber(report.cut.flushMs) +
                    " collidersAfter=" + report.cut.collidersAfter + " bodiesAfter=" + report.cut.bodiesAfter + " expectationMet=" + report.cut.expectationMet);
            if (report.physics.status == "passed")
                metrics.Add("physics: drift=" + VerifyNumber(report.physics.driftDistance) + " stationary=" + VerifyNumber(report.physics.stationaryDisplacement) +
                    " pushSpeed=" + VerifyNumber(report.physics.realPushTargetSpeed) + " steps=" + report.physics.steps);
            if (report.patchPhysics != null && report.patchPhysics.status == "passed")
                metrics.Add("patch: instances=" + report.patchPhysics.instances + " bodies=" + report.patchPhysics.bodies + " cells=" + report.patchPhysics.totalCells +
                    " colliders=" + report.patchPhysics.totalColliders + " initTotalMs=" + VerifyNumber(report.patchPhysics.initTotalMs) +
                    " warmAvgMs=" + VerifyNumber(report.patchPhysics.warmInitAvgMs) + " stepAvgMs=" + VerifyNumber(report.patchPhysics.stepAvgMs) +
                    " stepMaxMs=" + VerifyNumber(report.patchPhysics.stepMaxMs) + " newOverlaps=" + report.patchPhysics.newOverlapPairs +
                    " outOfBounds=" + report.patchPhysics.outOfBoundsBodies);
            if (report.layout.status == "validated")
                metrics.Add("layout: instances=" + report.layout.instanceCount + " cells=" + report.layout.totalCells + " maxCellsPerSample=" + report.layout.maxCellsPerSample);
        }

        static void VerifyFinalize(VerifyReport report)
        {
            if (report.source.integrityPassed)
            {
                report.checks.Add("source asset JSON unchanged");
                report.checks.Add("source asset files unchanged (SHA-256, same file set)");
            }
            if (report.source.cacheProbeSupported && report.source.cachePreserved)
                report.checks.Add("VoxelTemplateCache entries preserved (never cleared)");
            if (report.layout.status == "validated")
                report.checks.Add("layout validated: " + report.layout.instanceCount + " instance(s), " + report.layout.totalCells + " cell(s)");

            report.assetCount = report.assets.Count;
            report.passed = report.failures.Count == 0 && report.assets.Count > 0 && report.source.integrityPassed;
            if (report.passed)
                report.verdict = "PASSED - " + report.assets.Count + " asset(s), " + report.representatives.Count + " damage probe(s), " +
                    (report.physics.status == "passed" ? "physics stepped " + report.physics.steps + " x 1/60 s" : "physics unsupported") +
                    ", source unchanged. Editor one-shot CPU milliseconds; not FPS.";
            else
                report.verdict = "FAILED - " + report.failures.Count + " failure(s). See failures[]; unsupported checks are listed separately and were not claimed as passes.";
        }

        static string VerifyWriteReport(VerifyReport report, string outputJson)
        {
            report.outputJson = string.IsNullOrEmpty(outputJson) ? string.Empty : VerifyFullPath(outputJson);
            string json = Json(report, true);
            if (string.IsNullOrEmpty(outputJson))
            {
                Debug.Log("[MiningRound2] " + report.verdict);
                return json;
            }
            try
            {
                string directory = Path.GetDirectoryName(report.outputJson);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(report.outputJson, json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("[MiningRound2] Could not write the verification report to '" + report.outputJson + "': " + e.GetType().Name + ": " + e.Message);
                return json;
            }
            Debug.Log("[MiningRound2] " + report.verdict + " Report: " + report.outputJson);
            return report.outputJson;
        }
    }
}
