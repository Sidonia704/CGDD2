using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    /// <summary>Updates rock definitions and native caches only; never touches layouts, facilities or joints.</summary>
    public static class NaturalRockRebuilder
    {
        const string Saturn = "Assets/_Starframe/Maps/SaturnRingVoxels";
        const string Range = "Assets/_Starframe/Maps/Range115Voxels";
        public const string Samples = "Assets/_Starframe/Maps/NaturalRockSamples";
        const string Reports = "Documentation/NaturalRocks-2026-11-05";
        sealed class Job { public string source, target, mesh; public int cap; }
        [Serializable] public sealed class Record
        {
            public string name, assetPath, error;
            public string[] materialPaths;
            public int oldCells, cells, cap, trimmedParityCells;
            public float oldCellSize, cellSize, massPerCell;
            public double oldMass, mass;
            public Vector3 oldBoundsCenter, boundsCenter, oldBoundsSize, boundsSize;
            public long oldTriangles, triangles;
        }
        [Serializable] sealed class Report
        {
            public string operation;
            public bool complete;
            public int expected, errors;
            public double elapsedSeconds;
            public List<Record> records;
        }
        static readonly List<Job> jobs = new List<Job>();
        static readonly List<Record> records = new List<Record>();
        static int next, batch, failures;
        static bool running, samples, finished;
        static double started, ended;
        static string reportPath;

        public static object BeginSamples(int assetsPerUpdate = 1) => Begin(true, assetsPerUpdate);
        public static object BeginApply(int assetsPerUpdate = 8) => Begin(false, assetsPerUpdate);
        public static Material MatteRockMaterial(string key)
        {
            string[] names = { "RockStoneGrey", "RockWarmGrey", "RockCoolGrey" };
            uint seed = 0; foreach (char character in key) seed = unchecked(seed * 31 + character);
            string path = "Assets/_Starframe/Art/SceneryArt/" + names[(int)(seed % 3)] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) throw new InvalidOperationException("Missing existing matte rock material: " + path);
            return material;
        }
        public static object Status() => new
        {
            running, finished, samples, completed = next, total = jobs.Count, errors = failures,
            cells = records.Sum(r => (long)r.cells), triangles = records.Sum(r => r.triangles),
            elapsedSeconds = started > 0 ? (ended > 0 ? ended : EditorApplication.timeSinceStartup) - started : 0,
            reportPath, latestErrors = records.Where(r => !string.IsNullOrEmpty(r.error)).Select(r => r.name + ": " + r.error).Take(5).ToArray()
        };

        static object Begin(bool sampleOnly, int assetsPerUpdate)
        {
            if (running) throw new InvalidOperationException("Natural rock rebuild already running.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before rebuilding rocks.");
            var prototypes = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { Range + "/Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p).StartsWith("Asteroid_Rock_", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var oldRocks = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { Saturn + "/Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p).StartsWith("field_", StringComparison.Ordinal) || Path.GetFileName(p).StartsWith("scatter_", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (prototypes.Length != 8 || oldRocks.Length != 1294) throw new InvalidOperationException("Expected 8 + 1294 finished rock definitions.");
            var originalPaths = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { "Assets/_Starframe/Maps/Range115/Assets", "Assets/_Starframe/Maps/ReferenceMiningStation/Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).ToDictionary(Path.GetFileNameWithoutExtension, p => p);
            jobs.Clear(); records.Clear(); next = failures = 0; finished = false; ended = 0;
            samples = sampleOnly; batch = Mathf.Clamp(assetsPerUpdate, 1, 30);
            var chosen = sampleOnly ? new[] { prototypes[0], prototypes[prototypes.Length / 2], prototypes[prototypes.Length - 1] } : prototypes.Concat(oldRocks).ToArray();
            if (sampleOnly) { Folder(Samples + "/Assets"); Folder(Samples + "/Meshes"); }
            foreach (string path in chosen)
            {
                string key = Path.GetFileNameWithoutExtension(path);
                if (!originalPaths.TryGetValue(key, out string originalPath)) throw new InvalidOperationException("Missing original budget source: " + key);
                var original = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(originalPath);
                int cap = Mathf.Min(VoxelAssetDefinition.MaximumCells, Mathf.Max(path.StartsWith(Range, StringComparison.Ordinal) ? 256 : 1, original.cells.Count * 4));
                jobs.Add(new Job { source = path, target = sampleOnly ? Samples + "/Assets/" + key + ".asset" : path,
                    mesh = sampleOnly ? Samples + "/Meshes/" + key + ".asset" : path.Replace("/Assets/", "/Meshes/"), cap = cap });
            }
            Directory.CreateDirectory(Reports); reportPath = Reports + (sampleOnly ? "/Samples.json" : "/Apply.json");
            // Reuse the existing completed scene module library before batching imports.
            SceneVoxelModules.GetOrCreate();
            started = EditorApplication.timeSinceStartup; running = true; EditorApplication.update += Tick;
            return Status();
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); Folder(path.Substring(0, slash)); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static void Tick()
        {
            if (!running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            int end = Math.Min(next + batch, jobs.Count);
            AssetDatabase.StartAssetEditing();
            try
            {
                for (; next < end; next++)
                {
                    try { Rebuild(jobs[next]); }
                    catch (Exception failure)
                    {
                        failures++; records.Add(new Record { name = Path.GetFileNameWithoutExtension(jobs[next].source), assetPath = jobs[next].target, error = failure.ToString() });
                        Debug.LogException(failure);
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            SaveReport(false);
            if (next < jobs.Count) return;
            try
            {
                if (!samples && failures == 0) UpdateBackdrops();
                AssetDatabase.SaveAssets(); finished = failures == 0;
            }
            catch (Exception failure) { failures++; records.Add(new Record { name = "Backdrop", error = failure.ToString() }); Debug.LogException(failure); }
            running = false; ended = EditorApplication.timeSinceStartup; EditorApplication.update -= Tick; SaveReport(finished);
        }

        static void Rebuild(Job job)
        {
            var source = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(job.source);
            if (!source) throw new InvalidOperationException("Missing finished rock: " + job.source);
            var beforeBounds = source.LocalBounds;
            // All 1302 accepted finished rocks were verified to have a zero local occupied-box centre.
            // Reject a future offset asset rather than shifting an instance or silently moving its shape.
            if (beforeBounds.center.sqrMagnitude > 1e-8f) throw new InvalidOperationException("Offset rock requires an explicit authoring-grid phase: " + source.name);
            var originalCells = source.cells; float originalCell = source.cellSize, originalDensity = source.massPerCell;
            var originalMesh = source.initialLodMesh;
            var originalSurfaces = source.surfaces; var originalBaseColor = source.baseColor; var originalSurfaceColors = source.surfaceColors;
            double oldMass = originalCells.Count * (double)originalDensity * Math.Pow(originalCell, 3);
            long oldTriangles = VoxelAssemblyBaker.TriangleCount(originalMesh);
            float cell = originalCell; List<VoxelCell> result = null; int trimmed = 0;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    result = IrregularVoxelRocks.Build(beforeBounds, cell, job.cap, source.name, out _);
                    trimmed = CenterAtZero(result);
                    if (result.Count == 0 || result.Count > job.cap) throw new InvalidOperationException("Natural rock violates its cell budget.");
                    break;
                }
                catch (InvalidOperationException) { if (cell >= 10f) throw; cell = Mathf.Min(10f, cell * 1.12f); result = null; }
            }
            if (result == null) throw new InvalidOperationException("Natural rock exceeded bounded sampling attempts.");
            var target = samples ? AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(job.target) : source;
            if (!target) { target = UnityEngine.Object.Instantiate(source); target.name = source.name; AssetDatabase.CreateAsset(target, job.target); }
            else if (samples) EditorUtility.CopySerialized(source, target);
            try
            {
                target.cells = result; target.cellSize = cell; target.massPerCell = (float)(oldMass / (result.Count * Math.Pow(cell, 3)));
                target.hullModules = source.hullModules; target.NotifyChanged();
                if (job.source.StartsWith(Range + "/", StringComparison.Ordinal))
                {
                    target.surfaces = new[] { MatteRockMaterial(target.name) };
                    target.baseColor = Color.white; target.surfaceColors = new List<Color> { Color.white };
                }
                var mesh = VoxelAssemblyBaker.Bake(target, job.mesh);
                var afterBounds = target.LocalBounds;
                if (afterBounds.center.sqrMagnitude > 1e-8f) throw new InvalidOperationException("Natural rock centre changed.");
                records.Add(new Record { name = target.name, assetPath = job.target, oldCells = originalCells.Count, cells = result.Count, cap = job.cap,
                    trimmedParityCells = trimmed, oldCellSize = originalCell, cellSize = cell, massPerCell = target.massPerCell,
                    oldMass = oldMass, mass = result.Count * (double)target.massPerCell * Math.Pow(cell, 3), oldTriangles = oldTriangles,
                    triangles = VoxelAssemblyBaker.TriangleCount(mesh), oldBoundsCenter = beforeBounds.center, boundsCenter = afterBounds.center,
                    oldBoundsSize = beforeBounds.size, boundsSize = afterBounds.size,
                    materialPaths = target.surfaces?.Select(AssetDatabase.GetAssetPath).ToArray() ?? Array.Empty<string>() });
            }
            catch
            {
                if (!samples)
                {
                    target.cells = originalCells; target.cellSize = originalCell; target.massPerCell = originalDensity; target.initialLodMesh = originalMesh; target.NotifyChanged();
                    target.surfaces = originalSurfaces; target.baseColor = originalBaseColor; target.surfaceColors = originalSurfaceColors;
                    VoxelAssemblyBaker.Bake(target, job.mesh);
                }
                throw;
            }
        }

        static int CenterAtZero(List<VoxelCell> cells)
        {
            int before = cells.Count;
            for (int axis = 0; axis < 3; axis++)
            {
                int minimum = cells.Min(c => c.position[axis]), maximum = cells.Max(c => c.position[axis]);
                // A half-grid bbox centre cannot be translated onto zero with integer cell positions.
                // Remove only the outermost positive layer, then shift whole native modules by integers.
                for (int guard = 0; ((minimum + maximum) & 1) != 0 && guard < 4; guard++)
                {
                    int cut = maximum; cells.RemoveAll(c => c.position[axis] == cut);
                    if (cells.Count == 0) throw new InvalidOperationException("Parity trim emptied the rock.");
                    minimum = cells.Min(c => c.position[axis]); maximum = cells.Max(c => c.position[axis]);
                }
                if (((minimum + maximum) & 1) != 0) throw new InvalidOperationException("Cannot preserve the original zero centre.");
                int offset = -(minimum + maximum) / 2;
                foreach (var voxel in cells) { var position = voxel.position; position[axis] += offset; voxel.position = position; }
            }
            return before - cells.Count;
        }

        static void SaveReport(bool complete)
        {
            File.WriteAllText(reportPath, JsonUtility.ToJson(new Report { operation = samples ? "Independent natural rock samples; main map unchanged" : "Existing rock definitions/native caches only; layouts and movement unchanged",
                complete = complete, expected = jobs.Count, errors = failures, elapsedSeconds = (ended > 0 ? ended : EditorApplication.timeSinceStartup) - started, records = records }, true));
        }

        static void UpdateBackdrops()
        {
            var rocks = jobs.Where(j => j.target.StartsWith(Saturn, StringComparison.Ordinal)).Select(j => AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(j.target))
                .Where(a => a && a.cells.Count >= 32 && a.cells.Count <= 768).OrderBy(a => a.cells.Count).ToArray();
            if (rocks.Length < 4) throw new InvalidOperationException("Insufficient low-cell natural rocks for the existing backdrop caches.");
            int[] picks = { rocks.Length - 1, rocks.Length * 3 / 4, 0, rocks.Length / 8 };
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var source = rocks[picks[i]].initialLodMesh;
                    string path = Saturn + "/Backdrop/VoxelBackdrop" + i + ".asset";
                    var target = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (!target) throw new InvalidOperationException("Missing existing native backdrop: " + path);
                    var bounds = source.bounds; float scale = 2 / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    target.Clear(); target.indexFormat = source.indexFormat;
                    target.vertices = source.vertices.Select(v => (v - bounds.center) * scale).ToArray();
                    target.normals = source.normals; target.uv = source.uv; target.tangents = source.tangents;
                    target.subMeshCount = 1; target.SetTriangles(source.triangles, 0); target.RecalculateBounds(); target.UploadMeshData(false);
                    EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
    }
}
