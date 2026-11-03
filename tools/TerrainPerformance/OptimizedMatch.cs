using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        // Same five warm-cache VerifyAssets probes as the preserved original runtime baseline.
        // Verification.cs consumes actual mounted meshes; the scene/physics/source assertions remain intact.
        public static string TerrainOptimizedMatch()
        {
            if (Application.isPlaying) throw new InvalidOperationException("EditMode probe only.");
            const string output = "Documentation/TerrainPerformance-2026-11-01/optimized-matched";
            if (Directory.Exists(output)) throw new IOException("No-overwrite: " + output);
            const string leaseOutput = "Documentation/TerrainPerformance-2026-11-01/matched-cache-lease-audit.json";
            if (File.Exists(leaseOutput)) throw new IOException("No-overwrite: " + leaseOutput);
            Directory.CreateDirectory(output);
            const string root = "Assets/_Starframe/Maps/MiningStationRound2/Generated_v04";
            var leases = new List<VoxelTemplateCache.Template>();
            var audit = new TerrainMatchedLeaseAudit();
            var clock = Stopwatch.StartNew();
            try
            {
                // Correct EditMode cleanup releases each probe instance. Keep warm-cache conditions explicit,
                // rather than accidentally warming subsequent probes through leaked instance ownership.
                foreach (string guid in AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { root + "/Assets" }))
                {
                    var source = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!source) continue;
                    var lease = VoxelTemplateCache.Acquire(source);
                    leases.Add(lease);
                    audit.rows.Add(new TerrainMatchedLeaseRow { asset = source.name, usersBefore = lease.users - 1 });
                }
                clock.Stop(); audit.warmLeasePreparationMs = clock.Elapsed.TotalMilliseconds;
                audit.explicitWarmLeases = leases.Count;
                if (leases.Count != 10) throw new InvalidOperationException("Expected ten explicit warm source-template leases.");
                for (int i = 0; i < 5; i++)
                    VerifyAssets(root + "/Assets", root + "/Layouts/LocalDensityPatch.asset", output + "/run-" + i + ".json");
            }
            finally
            {
                for (int i = leases.Count - 1; i >= 0; i--)
                {
                    VoxelTemplateCache.Release(leases[i]);
                    audit.rows[i].usersAfter = leases[i].users;
                    audit.rows[i].ownershipRestored = audit.rows[i].usersBefore == audit.rows[i].usersAfter;
                    audit.ownershipRestored &= audit.rows[i].ownershipRestored;
                }
                File.WriteAllText(leaseOutput, Json(audit, true));
            }
            if (!audit.ownershipRestored) throw new InvalidOperationException("Matched probes leaked instance/template ownership; inspect " + leaseOutput);
            return "Five matched optimized-runtime reports: " + output + ". Ten explicit warm leases, released after all runs. CPU ms only, not FPS.";
        }

        [Serializable] public sealed class TerrainMatchedLeaseAudit
        {
            public string measurement = "Ten source-template leases explicitly preserve the baseline's warm-cache conditions across five VerifyAssets runs. Preparation, including any cold template connectivity/mesh/collider prewarm, is outside these warm-instance timings and is separately measured by the private-key prewarm audit. Cleanup explicitly releases EditMode ownership; whole-report duration therefore includes different cleanup work from the preserved baseline and is not a matched speedup metric.";
            public int explicitWarmLeases;
            public double warmLeasePreparationMs;
            public bool ownershipRestored = true;
            public List<TerrainMatchedLeaseRow> rows = new List<TerrainMatchedLeaseRow>();
        }
        [Serializable] public sealed class TerrainMatchedLeaseRow
        {
            public string asset;
            public int usersBefore, usersAfter;
            public bool ownershipRestored;
        }

        [Serializable] public sealed class TerrainPrewarmRow
        {
            public string asset;
            public int cells, aggregateVertices, aggregateSubmeshes, prewarmedChunks, prewarmedVertices;
            public double coldPrivateTemplateMs, chunkWarmupMs;
            public long aggregateUnityReportedBytes, chunkMeshStreamLowerBoundBytes, allocatedManagedBytes;
            public bool freshPrivateCacheKey;
        }
        [Serializable] public sealed class TerrainPrewarmReport
        {
            public string measurement = "A new private ScriptableObject clone per sample ensures no template cache entry exists for that identity; no global cache clear. Includes template aggregate mesh, chunk prewarm and collision-description creation, not a body instance. Source model/driver/JIT caches may be warm. The current-thread allocation counter is checked against a 1 MiB positive control; allocatedManagedBytes=-1 means unavailable, never zero allocations. Supported allocated bytes are main-thread managed allocations, not retained heap. Unity-reported aggregate memory and chunk stream estimates are different measurements and must not be added as an exact total. No renderer objects are created by template prewarm. CPU ms, not FPS.";
            public bool allocationCounterAvailable;
            public int allocationPositiveControlBytes;
            public long allocationObservedDeltaBytes;
            public List<TerrainPrewarmRow> rows = new List<TerrainPrewarmRow>();
        }
        public static string TerrainPrewarmAudit()
        {
            if (Application.isPlaying) throw new InvalidOperationException("EditMode probe only.");
            const string output = "Documentation/TerrainPerformance-2026-11-01/prewarm-audit.json";
            if (File.Exists(output)) throw new IOException("No-overwrite: " + output);
            const int positiveControlBytes = 1048576;
            long probeBefore = GC.GetAllocatedBytesForCurrentThread();
            var positiveControl = new byte[positiveControlBytes];
            positiveControl[0] = 1;
            long observedDelta = GC.GetAllocatedBytesForCurrentThread() - probeBefore;
            GC.KeepAlive(positiveControl);
            var report = new TerrainPrewarmReport
            {
                allocationCounterAvailable = observedDelta >= positiveControlBytes,
                allocationPositiveControlBytes = positiveControlBytes,
                allocationObservedDeltaBytes = observedDelta
            };
            const string folder = "Assets/_Starframe/Maps/MiningStationRound2/Generated_v04/Assets";
            foreach (string guid in AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { folder }))
            {
                var source = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (!source) continue;
                var clone = UnityEngine.Object.Instantiate(source);
                clone.name = source.name + " private prewarm probe";
                clone.hideFlags = HideFlags.HideAndDontSave;
                VoxelTemplateCache.Template template = null;
                try
                {
                    var row = new TerrainPrewarmRow { asset = source.name, cells = clone.cells.Count, freshPrivateCacheKey = true };
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var clock = Stopwatch.StartNew();
                    template = VoxelTemplateCache.Acquire(clone);
                    clock.Stop(); row.coldPrivateTemplateMs = clock.Elapsed.TotalMilliseconds;
                    row.allocatedManagedBytes = report.allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
                    row.aggregateVertices = template.mesh.vertexCount;
                    row.aggregateSubmeshes = template.mesh.subMeshCount;
                    row.aggregateUnityReportedBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(template.mesh);
                    var chunks = template.renderChunks;
                    if (chunks != null)
                    {
                        row.prewarmedChunks = chunks.ChunkCount; row.prewarmedVertices = chunks.TotalVertices;
                        row.chunkMeshStreamLowerBoundBytes = chunks.EstimatedMeshBytes; row.chunkWarmupMs = chunks.WarmupMilliseconds;
                    }
                    report.rows.Add(row);
                }
                finally
                {
                    VoxelTemplateCache.Release(template);
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
            if (report.rows.Count != 10) throw new InvalidOperationException("Expected ten unchanged v04 source assets.");
            File.WriteAllText(output, Json(report, true));
            return output;
        }
    }
}
