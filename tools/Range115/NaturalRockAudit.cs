#pragma warning disable CS0649 // Report DTO fields are populated by the JSON deserializer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.Construction;
using Starframe.EditorTools;

// External read-only CLI audit. No asset writes, imports, temporary scene objects, or cache rebuilds.
public static class NaturalRockAudit
{
    [Serializable] sealed class Entry
    {
        public string name, assetPath, error;
        public int oldCells, cells, cap;
        public float cellSize;
        public double oldMass, mass;
        public Vector3 boundsCenter, boundsSize;
        public long triangles;
    }
    [Serializable] sealed class Report { public bool complete; public int expected, errors; public Entry[] records; }
    static readonly Vector3Int[] Neighbors = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };

    public static object Run(bool samples = true)
    {
        string reportPath = "Documentation/NaturalRocks-2026-11-05/" + (samples ? "Samples.json" : "Apply.json");
        var errors = new List<string>();
        Report report = null;
        if (File.Exists(reportPath))
        {
            // External CLI code is loaded from a transient assembly. Resolve the full JSON
            // serializer explicitly rather than relying on Unity's nested DTO type cache.
            var serializer = System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
            var deserialize = serializer.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
            report = (Report)deserialize.Invoke(null, new object[] { File.ReadAllText(reportPath), typeof(Report) });
            if (!report.complete || report.errors != 0) errors.Add("Rebuild report is incomplete or contains errors.");
        }
        var reportByPath = (report?.records ?? Array.Empty<Entry>()).Where(r => !string.IsNullOrEmpty(r.assetPath))
            .GroupBy(r => NormalizePath(r.assetPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var reportByName = (report?.records ?? Array.Empty<Entry>()).Where(r => !string.IsNullOrEmpty(r.name))
            .GroupBy(r => r.name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        string[] roots = samples ? new[] { NaturalRockRebuilder.Samples + "/Assets" }
            : new[] { "Assets/_Starframe/Maps/Range115Voxels/Assets", "Assets/_Starframe/Maps/SaturnRingVoxels/Assets" };
        var paths = AssetDatabase.FindAssets("t:VoxelAssetDefinition", roots).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => samples || IsRock(Path.GetFileNameWithoutExtension(p))).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        int expected = samples ? (report?.expected ?? 3) : 1302;
        if (paths.Length != expected) errors.Add("Expected " + expected + " rocks, found " + paths.Length + ".");
        var originalPaths = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { "Assets/_Starframe/Maps/Range115/Assets", "Assets/_Starframe/Maps/ReferenceMiningStation/Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).ToDictionary(Path.GetFileNameWithoutExtension, p => p);
        int connected = 0, cacheValid = 0, capValid = 0, centered = 0, massValid = 0;
        long totalCells = 0, totalTriangles = 0;
        double maxMassRelativeError = 0, maximumCenterMetres = 0;
        var componentErrors = new List<object>();
        var shapeCounts = new Dictionary<string, long>();
        foreach (string path in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);
            if (!asset) { errors.Add(path + ": missing definition"); continue; }
            if (!asset.Validate(out string validation)) errors.Add(asset.name + ": " + validation);
            totalCells += asset.cells.Count;
            foreach (var cell in asset.cells)
            {
                string shape = cell.shape.ToString(); shapeCounts.TryGetValue(shape, out long count); shapeCounts[shape] = count + 1;
            }
            if (asset.cells.Any(c => c.shape != BlockShape.Cube && c.shape != BlockShape.Wedge)) errors.Add(asset.name + ": unexpected non Cube/Wedge natural-rock module");
            int components = Components(asset.cells);
            if (components == 1) connected++;
            else { errors.Add(asset.name + ": " + components + " six-neighbor components"); componentErrors.Add(new { asset = asset.name, components, cells = asset.cells.Count }); }
            if (VoxelAssemblyBaker.IsVoxelAssemblyCache(asset)) cacheValid++;
            else errors.Add(asset.name + ": invalid native assembly cache/signature");
            totalTriangles += VoxelAssemblyBaker.TriangleCount(asset.initialLodMesh);
            double centerDistance = asset.LocalBounds.center.magnitude; maximumCenterMetres = Math.Max(maximumCenterMetres, centerDistance);
            if (centerDistance < .0001) centered++; else errors.Add(asset.name + ": nonzero local centre " + asset.LocalBounds.center);
            if (!originalPaths.TryGetValue(asset.name, out string originalPath)) { errors.Add(asset.name + ": missing source budget/mass reference"); continue; }
            var original = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(originalPath);
            int cap = Math.Min(VoxelAssetDefinition.MaximumCells, Math.Max(asset.name.StartsWith("Asteroid_Rock_", StringComparison.Ordinal) ? 256 : 1, original.cells.Count * 4));
            double expectedMass = original.cells.Count * (double)original.massPerCell * Math.Pow(original.cellSize, 3);
            if (reportByPath.TryGetValue(NormalizePath(path), out var entry) || reportByName.TryGetValue(asset.name, out entry))
            {
                if (!string.IsNullOrEmpty(entry.error)) errors.Add(asset.name + ": report contains rebuild failure");
                if (entry.cap > 0) cap = Math.Min(cap, entry.cap);
                if (entry.oldMass > 0) expectedMass = entry.oldMass;
                if (entry.cells != asset.cells.Count || Math.Abs(entry.cellSize - asset.cellSize) > .00001f) errors.Add(asset.name + ": report cell count/size differs from saved definition");
                if ((entry.boundsCenter - asset.LocalBounds.center).sqrMagnitude > 1e-8f || (entry.boundsSize - asset.LocalBounds.size).sqrMagnitude > 1e-6f) errors.Add(asset.name + ": report bounds differ from saved definition");
                if (entry.triangles != VoxelAssemblyBaker.TriangleCount(asset.initialLodMesh)) errors.Add(asset.name + ": report triangles differ from saved cache");
            }
            else if (report != null) errors.Add(asset.name + ": missing individual report record");
            if (asset.cells.Count <= cap) capValid++; else errors.Add(asset.name + ": exceeds source four-times cell budget (" + cap + ")");
            double mass = asset.cells.Count * (double)asset.massPerCell * Math.Pow(asset.cellSize, 3);
            double relative = Math.Abs(mass - expectedMass) / Math.Max(expectedMass, 1e-10); maxMassRelativeError = Math.Max(maxMassRelativeError, relative);
            if (relative <= 1e-6) massValid++; else errors.Add(asset.name + ": relative mass mismatch " + relative);
            if (asset.name.StartsWith("Asteroid_Rock_", StringComparison.Ordinal))
            {
                if (asset.surfaces == null || asset.surfaces.Length != 1 || asset.surfaces[0] != NaturalRockRebuilder.MatteRockMaterial(asset.name) ||
                    asset.baseColor != Color.white || asset.surfaceColors == null || asset.surfaceColors.Count != 1 || asset.surfaceColors[0] != Color.white)
                    errors.Add(asset.name + ": matte material/white tint rule mismatch");
            }
        }
        return new { samples, reportPath, reportFound = report != null, parsedReportRecords = report?.records?.Length ?? 0, assets = paths.Length, expected, connected, cacheValid, capValid, centered, massValid,
            totalCells, totalTriangles, maxMassRelativeError, maximumCenterMetres, shapeCounts, componentErrors, allChecksPassed = errors.Count == 0, errors };
    }

    static string NormalizePath(string path) => (path ?? "").Replace('\\', '/').Trim();

    static bool IsRock(string name) => name.StartsWith("Asteroid_Rock_", StringComparison.Ordinal) || name.StartsWith("field_", StringComparison.Ordinal) || name.StartsWith("scatter_", StringComparison.Ordinal);
    static int Components(List<VoxelCell> cells)
    {
        var remaining = new HashSet<Vector3Int>(cells.Select(c => c.position));
        var pending = new Queue<Vector3Int>(); int components = 0;
        while (remaining.Count > 0)
        {
            var first = remaining.First(); remaining.Remove(first); pending.Enqueue(first); components++;
            while (pending.Count > 0)
            {
                var point = pending.Dequeue();
                foreach (var direction in Neighbors) { var neighbor = point + direction; if (remaining.Remove(neighbor)) pending.Enqueue(neighbor); }
            }
        }
        return components;
    }
}
