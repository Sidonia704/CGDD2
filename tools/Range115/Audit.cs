using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;

// External read-only Unity CLI script; never saves, imports, or edits project assets.
public static class Range115Audit
{
    [Serializable] public sealed class AssetResult
    {
        public string path;
        public int instances, cells, vertices, submeshes, sampledVertices, uncoveredSamples;
        public long triangles;
        public bool signature, materialSlots, verticesInBounds;
        public float maximumSurfaceGapCells;
    }
    [Serializable] public sealed class Report
    {
        public string mapPath;
        public bool validMap, serializationFidelity, referencePreservation;
        public int instances, uniqueAssets;
        public long uniqueCells, weightedCells, uniqueTriangles, weightedTriangles;
        public List<string> errors = new List<string>();
        public List<AssetResult> assets = new List<AssetResult>();
        public string limitation = "Roundtrip checks in-memory Unity JSON serialization of loaded persisted assets; does not unload/reimport. Sampled collision coverage checks occupied cube bounds only, not exact hull faces or physics contacts.";
    }
    public static string Run(string mapPath, string[] requiredReferencePaths = null, bool sampleCoverage = true)
    {
        var report = new Report { mapPath = mapPath, serializationFidelity = true, referencePreservation = true };
        var map = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(mapPath);
        if (!map) throw new ArgumentException("Map not found: " + mapPath);
        report.validMap = map.Validate(out var error);
        if (!report.validMap) report.errors.Add(error);
        report.instances = map.instances.Count;
        var copy = ScriptableObject.CreateInstance<SpaceMapDefinition>();
        copy.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(map), copy);
            report.serializationFidelity = copy.instances.Count == map.instances.Count;
            for (int i = 0; i < map.instances.Count && i < copy.instances.Count; i++)
            {
                var a = map.instances[i]; var b = copy.instances[i];
                if (a == null || b == null || a.id != b.id || a.asset != b.asset || a.position != b.position ||
                    a.eulerAngles != b.eulerAngles || a.scale != b.scale ||
                    Quaternion.Angle(Quaternion.Euler(a.eulerAngles), Quaternion.Euler(b.eulerAngles)) > .001f)
                { report.serializationFidelity = false; report.errors.Add("Roundtrip instance mismatch at " + i); }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        foreach (var group in map.instances.Where(i => i != null && i.asset).GroupBy(i => i.asset))
        {
            var asset = group.Key; var mesh = asset.initialLodMesh;
            var result = new AssetResult { path = AssetDatabase.GetAssetPath(asset), instances = group.Count(), cells = asset.cells.Count,
                signature = asset.HasValidInitialLod, materialSlots = mesh && mesh.subMeshCount == asset.RenderMaterialCount, verticesInBounds = true };
            report.assets.Add(result);
            if (!mesh) { report.errors.Add(result.path + ": missing surface"); continue; }
            result.vertices = mesh.vertexCount; result.submeshes = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) result.triangles += (long)mesh.GetIndexCount(s) / 3;
            if (mesh.isReadable)
            {
                var vertices = mesh.vertices; var bounds = asset.LocalBounds; bounds.Expand(asset.cellSize * .02f);
                foreach (var vertex in vertices) if (!bounds.Contains(vertex)) { result.verticesInBounds = false; break; }
                if (sampleCoverage)
                {
                    var occupied = new HashSet<Vector3Int>(asset.cells.Select(c => c.position));
                    int step = Math.Max(1, (vertices.Length + 511) / 512);
                    for (int v = 0; v < vertices.Length; v += step)
                    {
                        var p = vertices[v] / asset.cellSize; var nearest = Vector3Int.RoundToInt(p); float minimum = float.PositiveInfinity;
                        for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) for (int z = -2; z <= 2; z++)
                        {
                            var cell = nearest + new Vector3Int(x,y,z); if (!occupied.Contains(cell)) continue;
                            var delta = p - (Vector3)cell;
                            float gap = new Vector3(Mathf.Max(0, Mathf.Abs(delta.x)-.5f), Mathf.Max(0, Mathf.Abs(delta.y)-.5f), Mathf.Max(0, Mathf.Abs(delta.z)-.5f)).magnitude;
                            minimum = Mathf.Min(minimum, gap);
                        }
                        result.sampledVertices++;
                        if (minimum > 1.001f) result.uncoveredSamples++;
                        result.maximumSurfaceGapCells = Mathf.Max(result.maximumSurfaceGapCells, float.IsInfinity(minimum) ? 999f : minimum);
                    }
                }
            }
            else { result.verticesInBounds = false; report.errors.Add(result.path + ": unreadable vertices; containment unverified"); }
            if (!result.signature || !result.materialSlots || !result.verticesInBounds || result.uncoveredSamples > 0) report.errors.Add(result.path + ": surface validation failed");
            report.uniqueCells += result.cells; report.weightedCells += (long)result.cells * result.instances;
            report.uniqueTriangles += result.triangles; report.weightedTriangles += result.triangles * result.instances;
        }
        report.uniqueAssets = report.assets.Count;
        if (requiredReferencePaths != null) foreach (string path in requiredReferencePaths)
        {
            int count = map.instances.Count(i => i != null && i.asset && AssetDatabase.GetAssetPath(i.asset) == path);
            if (count != 1) { report.referencePreservation = false; report.errors.Add("Expected exactly one preserved reference: " + path + ", found " + count); }
        }
        return JsonUtility.ToJson(report, true);
    }
}
