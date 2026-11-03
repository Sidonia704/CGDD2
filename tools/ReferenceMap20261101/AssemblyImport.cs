using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;

// Run as a standalone Unity Editor script after the frozen catalog's layout is imported.
// The JSON lives outside Assets and is never copied into the runtime build.
public static class ReferenceMapAssemblyImport
{
    const string Root = "Assets/_Starframe/Maps/ReferenceMiningStation";
    const string LayoutPath = Root + "/Layouts/ReferenceMiningStation3500.asset";
    const string CatalogFile = "Temp/ReferenceMap20261101/geometry-full-scene-final.json";
    const string LinksFile = "Temp/ReferenceMap20261101/scene-assembly-links.json";
    const string FrozenCatalogHash = "f5434e45ad1a4afe940771a79081b25f5e54547b9c00f284364c8c7a936d3862";
    const string FrozenLinksHash = "e642eeb2133169e076492e9873f704d51da4ff3c8501078688617abf8bcf9461";

    public sealed class CatalogSource { public string @object; public int sourceShell; public int[] sourceShells; public string family; public string replacement; }
    public sealed class CatalogAsset { public string assetKey; public int cells; public float cellSize; public string occupancyMode; public CatalogSource source; }
    public sealed class CatalogInstance { public string id, assetKey, label; public float[] position, rotationMatrix; public float scale; public CatalogSource source; }
    public sealed class Catalog { public float[] globalTranslation; public CatalogAsset[] assets; public CatalogInstance[] instances; }
    public sealed class LinkRow
    {
        public string instanceIdA, instanceIdB, assetKeyA, assetKeyB, sourceEvidence;
        public int[] anchorCellA, anchorCellB;
        public float[] worldAnchor;
        public bool createFixedJoint, ignoreMutualCollision;
    }
    public sealed class LinkFile { public string catalogSha256; public bool globalTranslationApplied; public LinkRow[] links; }

    static string ProjectPath(string relative) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    static T Read<T>(string relative)
    {
        var jsonType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Newtonsoft.Json.JsonConvert", false)).FirstOrDefault(t => t != null);
        if (jsonType == null) throw new InvalidOperationException("Newtonsoft.Json.JsonConvert is unavailable in the Unity Editor.");
        var method = jsonType.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
        if (method == null) throw new InvalidOperationException("Newtonsoft.Json DeserializeObject(string, Type) is unavailable.");
        return (T)method.Invoke(null, new object[] { File.ReadAllText(ProjectPath(relative)), typeof(T) });
    }
    static string Hash(string relative)
    {
        using (var input = File.OpenRead(ProjectPath(relative)))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
    static Vector3 V(float[] a)
    {
        if (a == null || a.Length != 3) throw new InvalidOperationException("Expected a 3-vector in the frozen manifest.");
        return new Vector3(a[0], a[1], a[2]);
    }
    static Vector3Int Cell(int[] a)
    {
        if (a == null || a.Length != 3) throw new InvalidOperationException("Expected a 3-cell anchor in the link sidecar.");
        return new Vector3Int(a[0], a[1], a[2]);
    }
    static SpaceMapDefinition Map()
    {
        var map = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(LayoutPath);
        if (!map) throw new InvalidOperationException("Map layout is missing: " + LayoutPath);
        return map;
    }
    static string KeyOf(VoxelAssetDefinition asset) => Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(asset));
    static Catalog CheckedCatalog()
    {
        if (Hash(CatalogFile) != FrozenCatalogHash) throw new InvalidOperationException("Frozen scene catalog SHA-256 changed.");
        var catalog = Read<Catalog>(CatalogFile);
        if (catalog.assets == null || catalog.assets.Length != 2345 || catalog.instances == null || catalog.instances.Length != 2854 ||
            catalog.globalTranslation == null || V(catalog.globalTranslation) != new Vector3(0, -420, 0))
            throw new InvalidOperationException("Frozen scene catalog count or global placement changed.");
        return catalog;
    }
    static LinkFile CheckedLinks()
    {
        if (Hash(LinksFile) != FrozenLinksHash) throw new InvalidOperationException("Frozen assembly sidecar SHA-256 changed.");
        var links = Read<LinkFile>(LinksFile);
        if (links.catalogSha256 != FrozenCatalogHash || links.globalTranslationApplied || links.links == null || links.links.Length != 125)
            throw new InvalidOperationException("Assembly links do not match the unshifted frozen scene catalog.");
        return links;
    }
    static string Label(CatalogInstance row)
    {
        var source = row.source;
        if (source == null || string.IsNullOrEmpty(source.@object)) return row.label ?? row.assetKey;
        string family = source.@object == "Cylinder.018" ? "Station" :
            source.@object == "Cube.001" ? "Field rock" : "Debris";
        if (source.sourceShells != null && source.sourceShells.Length > 0)
            return family + " shells " + source.sourceShells[0] + "–" + source.sourceShells[source.sourceShells.Length - 1];
        if (source.replacement != null) return family + " " + (row.label ?? row.assetKey);
        return family + " shell " + source.sourceShell;
    }
    static void CheckConnected(VoxelAssetDefinition asset)
    {
        var remaining = new HashSet<Vector3Int>(asset.cells.Select(c => c.position));
        if (remaining.Count != asset.cells.Count || remaining.Count == 0)
            throw new InvalidOperationException("Empty or duplicate native cells: " + asset.name);
        var queue = new Queue<Vector3Int>();
        var first = remaining.First(); remaining.Remove(first); queue.Enqueue(first);
        var directions = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
        while (queue.Count != 0)
        {
            var cell = queue.Dequeue();
            foreach (var direction in directions)
            {
                var adjacent = cell + direction;
                if (remaining.Remove(adjacent)) queue.Enqueue(adjacent);
            }
        }
        if (remaining.Count != 0) throw new InvalidOperationException("Disconnected native asset: " + asset.name + ", cells outside main body=" + remaining.Count);
    }

    public static object Apply()
    {
        var catalog = CheckedCatalog();
        var sidecar = CheckedLinks();
        var map = Map();
        var native = map.instances.ToDictionary(i => i.id);
        var authored = catalog.instances.ToDictionary(i => i.id);
        if (native.Count != catalog.instances.Length) throw new InvalidOperationException("Native layout instance count differs from frozen catalog.");
        var occupied = new Dictionary<VoxelAssetDefinition, HashSet<Vector3Int>>();
        var built = new List<VoxelAssemblyLink>(sidecar.links.Length);
        var pairs = new HashSet<string>();
        int fixedCount = 0, ignoreOnlyCount = 0;
        foreach (var row in sidecar.links)
        {
            if (!native.TryGetValue(row.instanceIdA ?? "", out var a) || !native.TryGetValue(row.instanceIdB ?? "", out var b) || a == b ||
                !authored.TryGetValue(row.instanceIdA ?? "", out var authoredA) || !authored.TryGetValue(row.instanceIdB ?? "", out var authoredB) ||
                authoredA.assetKey != row.assetKeyA || authoredB.assetKey != row.assetKeyB ||
                KeyOf(a.asset) != row.assetKeyA || KeyOf(b.asset) != row.assetKeyB)
                throw new InvalidOperationException("Assembly link references a missing or mismatched native instance: " + row.instanceIdA + " / " + row.instanceIdB);
            string pair = string.CompareOrdinal(a.id, b.id) < 0 ? a.id + ":" + b.id : b.id + ":" + a.id;
            if (!pairs.Add(pair)) throw new InvalidOperationException("Duplicate assembly pair: " + pair);
            if (!occupied.TryGetValue(a.asset, out var cellsA)) occupied[a.asset] = cellsA = new HashSet<Vector3Int>(a.asset.cells.Select(c => c.position));
            if (!occupied.TryGetValue(b.asset, out var cellsB)) occupied[b.asset] = cellsB = new HashSet<Vector3Int>(b.asset.cells.Select(c => c.position));
            var anchorA = Cell(row.anchorCellA); var anchorB = Cell(row.anchorCellB);
            if (!cellsA.Contains(anchorA) || !cellsB.Contains(anchorB)) throw new InvalidOperationException("Assembly anchor cell is missing: " + pair);
            if (!row.createFixedJoint && !row.ignoreMutualCollision) throw new InvalidOperationException("Inert assembly pair: " + pair);
            if (row.createFixedJoint) fixedCount++; else ignoreOnlyCount++;
            built.Add(new VoxelAssemblyLink
            {
                instanceIdA = a.id, instanceIdB = b.id,
                anchorCellA = anchorA, anchorCellB = anchorB,
                worldAnchor = V(row.worldAnchor) + V(catalog.globalTranslation),
                createFixedJoint = row.createFixedJoint,
                ignoreMutualCollision = row.ignoreMutualCollision
            });
        }
        if (fixedCount != 112 || ignoreOnlyCount != 13) throw new InvalidOperationException("Assembly source-evidence policy changed.");
        // Rebuild from canonical sidecar on every run. This makes the -420 m shift idempotent.
        var oldLinks = map.assemblyLinks;
        var oldLabels = map.instances.Select(i => i.label).ToArray();
        Undo.RecordObject(map, "Import reference map assembly links");
        try
        {
            map.assemblyLinks = built;
            for (int i = 0; i < catalog.instances.Length; i++) map.instances[i].label = Label(authored[map.instances[i].id]);
            if (!map.Validate(out var error)) throw new InvalidOperationException("Native layout rejected assembly links: " + error);
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }
        catch
        {
            map.assemblyLinks = oldLinks;
            for (int i = 0; i < oldLabels.Length; i++) map.instances[i].label = oldLabels[i];
            throw;
        }
        return new { layout = LayoutPath, instances = native.Count, links = built.Count, fixedJoints = fixedCount, ignoreOnly = ignoreOnlyCount,
            anchorsVerified = built.Count * 2, catalogSha256 = FrozenCatalogHash, translation = new[] { 0, -420, 0 } };
    }

    // Read-only audit for the parent Editor batch, after Apply. No asset is mutated.
    public static object VerifyWorld()
    {
        var catalog = CheckedCatalog(); var sidecar = CheckedLinks(); var map = Map();
        if (map.instances.Count != catalog.instances.Length || map.assemblyLinks == null || map.assemblyLinks.Count != sidecar.links.Length)
            throw new InvalidOperationException("Native map instance or link count differs from frozen manifests.");
        var assets = catalog.assets.ToDictionary(a => a.assetKey);
        var native = map.instances.ToDictionary(i => i.id);
        long instancedCells = 0;
        var seenAssets = new Dictionary<string, VoxelAssetDefinition>();
        var worldMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var worldMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (var row in catalog.instances)
        {
            if (!native.TryGetValue(row.id ?? "", out var item) || !item.asset || KeyOf(item.asset) != row.assetKey ||
                !assets.TryGetValue(row.assetKey, out var expected))
                throw new InvalidOperationException("Native instance missing or asset mismatch: " + row.id);
            if (item.asset.cells.Count != expected.cells || Mathf.Abs(item.asset.cellSize - expected.cellSize) > .0001f)
                throw new InvalidOperationException("Native asset cell count/size mismatch: " + row.assetKey);
            if ((item.position - (V(row.position) + V(catalog.globalTranslation))).magnitude > .002f || Mathf.Abs(item.scale - row.scale) > .0001f)
                throw new InvalidOperationException("Native instance pose mismatch: " + row.id);
            if (row.rotationMatrix != null && row.rotationMatrix.Length == 9)
            {
                var expectedRotation = Quaternion.LookRotation(new Vector3(row.rotationMatrix[2], row.rotationMatrix[5], row.rotationMatrix[8]),
                    new Vector3(row.rotationMatrix[1], row.rotationMatrix[4], row.rotationMatrix[7]));
                if (Quaternion.Angle(expectedRotation, Quaternion.Euler(item.eulerAngles)) > .05f)
                    throw new InvalidOperationException("Native instance rotation mismatch: " + row.id);
            }
            instancedCells += item.asset.cells.Count;
            seenAssets[row.assetKey] = item.asset;
            var bounds = item.asset.LocalBounds;
            var m = item.Matrix;
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float y in new[] { bounds.min.y, bounds.max.y })
            foreach (float z in new[] { bounds.min.z, bounds.max.z })
            {
                var point = m.MultiplyPoint3x4(new Vector3(x, y, z));
                worldMin = Vector3.Min(worldMin, point);
                worldMax = Vector3.Max(worldMax, point);
            }
        }
        if (seenAssets.Count != 2338 || instancedCells != 3123469)
            throw new InvalidOperationException("Native placed asset or placed cell count differs from frozen audit.");
        // Seven scatter prototypes are in the frozen library but have no scene instance.
        var libraryPaths = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { Root + "/Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p), p => p);
        int unplacedAssets = 0;
        long uniqueCells = 0;
        var shapes = new long[8];
        foreach (var row in catalog.assets)
        {
            if (!libraryPaths.TryGetValue(row.assetKey, out var path))
                throw new InvalidOperationException("Catalog asset missing from native library: " + row.assetKey);
            var asset = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);
            if (!asset || asset.cells.Count != row.cells || Mathf.Abs(asset.cellSize - row.cellSize) > .0001f)
                throw new InvalidOperationException("Catalog asset count or size differs in native library: " + row.assetKey);
            if (seenAssets.TryGetValue(row.assetKey, out var placedAsset))
            {
                if (asset != placedAsset) throw new InvalidOperationException("Placed asset differs from library asset: " + row.assetKey);
            }
            else unplacedAssets++;
            CheckConnected(asset);
            uniqueCells += asset.cells.Count;
            foreach (var cell in asset.cells)
            {
                int shape = Array.IndexOf(VoxelHullAppearance.Shapes, cell.shape);
                if (shape < 0 || shape >= shapes.Length) throw new InvalidOperationException("Invalid native hull shape: " + row.assetKey);
                shapes[shape]++;
            }
        }
        if (unplacedAssets != 7 || uniqueCells != 2559830 || shapes.Any(n => n == 0))
            throw new InvalidOperationException("Native library asset, unique cell or eight-hull coverage differs from frozen audit.");
        foreach (var row in sidecar.links)
        {
            var link = map.assemblyLinks.FirstOrDefault(l => l.instanceIdA == row.instanceIdA && l.instanceIdB == row.instanceIdB);
            if (link == null || link.anchorCellA != Cell(row.anchorCellA) || link.anchorCellB != Cell(row.anchorCellB) ||
                link.createFixedJoint != row.createFixedJoint || link.ignoreMutualCollision != row.ignoreMutualCollision ||
                (link.worldAnchor - (V(row.worldAnchor) + V(catalog.globalTranslation))).magnitude > .002f)
                throw new InvalidOperationException("Native assembly link mismatch: " + row.instanceIdA + " / " + row.instanceIdB);
        }
        if (!map.Validate(out var error)) throw new InvalidOperationException("Native map validation failed: " + error);
        return new { layout = LayoutPath, instances = map.instances.Count, placedUniqueAssets = seenAssets.Count,
            libraryAssets = catalog.assets.Length, unplacedPrototypeAssets = unplacedAssets,
            uniqueCells, instancedCells, links = map.assemblyLinks.Count, hullShapeCounts = shapes, sourceCatalogHash = FrozenCatalogHash,
            disconnectedAssets = 0, worldBoundsMin = new[] { worldMin.x, worldMin.y, worldMin.z },
            worldBoundsMax = new[] { worldMax.x, worldMax.y, worldMax.z } };
    }
}
