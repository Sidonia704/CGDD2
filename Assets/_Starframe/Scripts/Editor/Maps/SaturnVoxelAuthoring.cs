using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Starframe.Construction;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.EditorTools
{
    /// <summary>Actual native-module assembly. Source surfaces are editor-only alignment references.</summary>
    public static class SaturnVoxelAuthoring
    {
        public const string Root = "Assets/_Starframe/Maps/SaturnRingVoxels";
        const string Original = "Assets/_Starframe/Maps/ReferenceMiningStation";
        const string References = "Assets/_Starframe/Maps/SaturnRingContinuous/Meshes";
        sealed class Frame { public Vector3 origin; public Quaternion rotation = Quaternion.identity; public bool aligned; }
        static readonly Dictionary<string, Frame> frames = new Dictionary<string, Frame>();
        static string[] sources;
        static int next, realigned, batchSize;
        static long cells, triangles, simplifiedDecorativeCells;
        static bool running, finished;
        static string error;
        static double started, ended;
        static readonly List<string> alignmentNames = new List<string>();
        static int rockRefits;
        [Serializable] sealed class AssetRecord
        {
            public string name, operation;
            public int originalCells, cells, simplifiedDecorativeCells;
            public float originalCellSize, cellSize, massPerCell;
            public double originalMass, rebuiltMass;
            public Vector3 localOriginCompensation;
        }
        [Serializable] sealed class BuildReport { public int assets, alignedFacilities, facetedRocks; public long nativeTriangles; public List<AssetRecord> records; }
        static readonly List<AssetRecord> records = new List<AssetRecord>();

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); Folder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        public static object BeginImport(int assetsPerUpdate = 3)
        {
            if (running) throw new InvalidOperationException("Saturn native voxel import already running.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before authoring.");
            sources = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { Original + "/Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (sources.Length != 2345) throw new InvalidDataException("Expected all 2345 original assets, found " + sources.Length);
            Folder(Root + "/Assets"); Folder(Root + "/Meshes"); Folder(Root + "/Layouts"); Folder(Root + "/Backdrop");
            next = realigned = rockRefits = 0; cells = triangles = simplifiedDecorativeCells = 0; frames.Clear(); alignmentNames.Clear(); records.Clear();
            error = null; finished = false; batchSize = Mathf.Clamp(assetsPerUpdate, 1, 20);
            started = EditorApplication.timeSinceStartup; ended = 0; running = true;
            EditorApplication.update += Step;
            return Status();
        }

        public static object Status() => new
        {
            running, finished, completed = next, total = sources?.Length ?? 0, realigned,
            cells, triangles, simplifiedDecorativeCells, rockRefits, error, elapsedSeconds = started > 0 ? (ended > 0 ? ended : EditorApplication.timeSinceStartup) - started : 0,
            layoutPath = Root + "/Layouts/SaturnRingVoxels.asset", alignedAssets = alignmentNames.ToArray()
        };

        static void Step()
        {
            if (!running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                int end = Math.Min(sources.Length, next + batchSize);
                for (; next < end; next++) ImportOne(sources[next]);
                if (next < sources.Length) return;
                CreateLayout(); CreateBackdrop(); AssetDatabase.SaveAssets();
                File.WriteAllText(Root + "/BuildReport.json", JsonUtility.ToJson(new BuildReport { assets = next, alignedFacilities = realigned,
                    facetedRocks = rockRefits, nativeTriangles = triangles, records = records }, true));
                finished = true; Stop();
                Debug.Log("Saturn native assembly ready: " + next + " definitions, " + realigned + " reliably aligned cuboids, " + triangles + " native triangles.");
            }
            catch (Exception failure) { error = failure.ToString(); Stop(); Debug.LogException(failure); }
        }

        static void Stop() { running = false; ended = EditorApplication.timeSinceStartup; EditorApplication.update -= Step; }

        public static object BuildSample()
        {
            if (running) throw new InvalidOperationException("Wait for the active import before sampling.");
            Folder(Root + "/Assets"); Folder(Root + "/Meshes");
            var paths = AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { Original + "/Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var assets = paths.Select(p => new { path = p, asset = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(p) }).ToArray();
            var picks = new[]
            {
                assets.Where(a => a.asset.name.StartsWith("station_", StringComparison.Ordinal)).OrderByDescending(a => a.asset.cells.Count).First(),
                assets.Where(a => a.asset.name.StartsWith("field_", StringComparison.Ordinal)).OrderByDescending(a => a.asset.cells.Count).First(),
                assets.Where(a => a.asset.name.StartsWith("station_left_box_grid_", StringComparison.Ordinal)).OrderByDescending(a => a.asset.cells.Count).First()
            };
            var report = new List<object>();
            foreach (var selected in picks)
            {
                frames.Remove(selected.asset.name);
                long beforeSimplified = simplifiedDecorativeCells;
                ImportOne(selected.path);
                var clone = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(Root + "/Assets/" + selected.asset.name + ".asset");
                var mesh = clone.initialLodMesh;
                report.Add(new { name = clone.name, originalCells = selected.asset.cells.Count, cells = clone.cells.Count,
                    vertices = mesh.vertexCount, triangles = VoxelAssemblyBaker.TriangleCount(mesh),
                    simplifiedDecorativeCells = simplifiedDecorativeCells - beforeSimplified,
                    realigned = frames[clone.name].aligned, massPerCell = clone.massPerCell,
                    rawVertexIndexBytes = (long)mesh.vertexCount * 48 + VoxelAssemblyBaker.TriangleCount(mesh) * 12 });
            }
            return report;
        }

        static void ImportOne(string sourcePath)
        {
            var source = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(sourcePath);
            string key = Path.GetFileNameWithoutExtension(sourcePath);
            string targetPath = Root + "/Assets/" + key + ".asset";
            var clone = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(targetPath);
            if (!clone) { clone = UnityEngine.Object.Instantiate(source); clone.name = key; AssetDatabase.CreateAsset(clone, targetPath); }
            else EditorUtility.CopySerialized(source, clone);
            // Independent seamless scenery primitives; the player's ship library is never modified.
            clone.hullModules = SceneVoxelModules.GetOrCreate();
            int simplified = 0;
            foreach (var cell in clone.cells)
            {
                if (cell.shape != BlockShape.Block5 && cell.shape != BlockShape.Block6) continue;
                cell.shape = BlockShape.Cube; cell.modelOffset = Vector3Int.zero;
                simplifiedDecorativeCells++;
                simplified++;
            }
            var frame = new Frame();
            // Only source station facilities qualify. Curved masses, rubble and rocks retain native cells.
            bool facility = key.StartsWith("station_", StringComparison.Ordinal) || key.StartsWith("leftupper_", StringComparison.Ordinal);
            bool rock = key.StartsWith("field_", StringComparison.Ordinal) || key.StartsWith("scatter_", StringComparison.Ordinal);
            var reference = facility || rock ? AssetDatabase.LoadAssetAtPath<Mesh>(References + "/" + key + ".asset") : null;
            if (rock)
            {
                if (!reference) throw new InvalidOperationException("Missing rock bounds reference: " + key);
                var bounds = reference.bounds;
                var size = bounds.size;
                float targetCell = Mathf.Clamp(Mathf.Min(source.cellSize * .72f, Mathf.Min(size.x, size.y, size.z) / 5f), .25f, 10f);
                int cap = Math.Min(VoxelAssetDefinition.MaximumCells, source.cells.Count * 4);
                List<VoxelCell> replacement = null;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try { replacement = Range115VoxelBuilder.BuildFacetedRock(new Bounds(Vector3.zero, size), targetCell, cap, key); break; }
                    catch (InvalidOperationException) { if (targetCell >= 10f) throw; targetCell = Mathf.Min(10f, targetCell * 1.18f); }
                }
                if (replacement == null) throw new InvalidOperationException("Faceted rock exceeds bounded sampling attempts: " + key);
                clone.cells = replacement; clone.cellSize = targetCell;
                double originalMass = (double)source.cells.Count * source.massPerCell * Math.Pow(source.cellSize, 3);
                clone.massPerCell = (float)(originalMass / (replacement.Count * Math.Pow(targetCell, 3)));
                frame.origin = bounds.center; frame.aligned = true; rockRefits++;
            }
            else if (reference && TryAlignedCuboid(reference, source.cellSize, out var center, out var rotation, out var dimensions))
            {
                var replacement = BoxShell(dimensions);
                // Surface-shell reference facilities stay shells; no solid cavity fill is invented.
                if (replacement.Count > 0 && replacement.Count <= VoxelAssetDefinition.MaximumCells)
                {
                    var generatedCenter = new Vector3((dimensions.x - 1) * .5f - Mathf.Round((dimensions.x - 1) * .5f),
                        (dimensions.y - 1) * .5f - Mathf.Round((dimensions.y - 1) * .5f),
                        (dimensions.z - 1) * .5f - Mathf.Round((dimensions.z - 1) * .5f)) * source.cellSize;
                    frame.origin = center - rotation * generatedCenter;
                    frame.rotation = rotation; frame.aligned = true;
                    clone.cells = replacement;
                    double oldMass = (double)source.cells.Count * source.massPerCell * Math.Pow(source.cellSize, 3);
                    clone.massPerCell = (float)(oldMass / (replacement.Count * Math.Pow(clone.cellSize, 3)));
                    realigned++; alignmentNames.Add(key);
                }
            }
            frames[key] = frame;
            clone.initialLodMesh = null; clone.initialLodSignature = 0; clone.NotifyChanged();
            if (!clone.Validate(out string reason)) throw new InvalidOperationException(key + ": " + reason);
            var baked = VoxelAssemblyBaker.Bake(clone, Root + "/Meshes/" + key + ".asset");
            if (!clone.HasValidInitialLod) throw new InvalidOperationException("Unsigned native assembly: " + key);
            triangles += VoxelAssemblyBaker.TriangleCount(baked); cells += clone.cells.Count;
            records.Add(new AssetRecord { name = key, operation = rock ? "18-plane Cube/Wedge faceted rock from source bounds proportions" : frame.aligned ? "six-plane cuboid local-axis native shell" : "original occupied topology with seamless native modules",
                originalCells = source.cells.Count, cells = clone.cells.Count, originalCellSize = source.cellSize, cellSize = clone.cellSize,
                simplifiedDecorativeCells = simplified, massPerCell = clone.massPerCell,
                originalMass = (double)source.cells.Count * source.massPerCell * Math.Pow(source.cellSize, 3),
                rebuiltMass = (double)clone.cells.Count * clone.massPerCell * Math.Pow(clone.cellSize, 3), localOriginCompensation = frame.origin });
        }

        static List<VoxelCell> BoxShell(Vector3Int size)
        {
            var result = new List<VoxelCell>();
            var anchor = new Vector3Int(Mathf.RoundToInt((size.x - 1) * .5f), Mathf.RoundToInt((size.y - 1) * .5f), Mathf.RoundToInt((size.z - 1) * .5f));
            for (int x = 0; x < size.x; x++) for (int y = 0; y < size.y; y++) for (int z = 0; z < size.z; z++)
            {
                if (x != 0 && x != size.x - 1 && y != 0 && y != size.y - 1 && z != 0 && z != size.z - 1) continue;
                result.Add(new VoxelCell { position = new Vector3Int(x, y, z) - anchor, shape = BlockShape.Cube, surface = 0 });
            }
            return result;
        }

        static bool TryAlignedCuboid(Mesh mesh, float cellSize, out Vector3 center, out Quaternion rotation, out Vector3Int dimensions)
        {
            center = Vector3.zero; rotation = Quaternion.identity; dimensions = Vector3Int.zero;
            var vertices = mesh.vertices; var indices = mesh.triangles;
            if (indices.Length < 12 * 3 || vertices.Length == 0) return false;
            var normals = new List<(Vector3 normal, float area)>();
            float totalArea = 0;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var cross = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]);
                float area = cross.magnitude;
                if (area < 1e-6f) continue;
                normals.Add((cross / area, area)); totalArea += area;
            }
            if (normals.Count < 12) return false;
            var ordered = normals.OrderByDescending(n => n.area).ToArray();
            Vector3 up = ordered[0].normal;
            var perpendicular = ordered.Where(n => Mathf.Abs(Vector3.Dot(n.normal, up)) < .0005f).ToArray();
            if (perpendicular.Length == 0) return false;
            Vector3 right = perpendicular[0].normal;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            right = Vector3.Cross(up, forward).normalized;
            rotation = Quaternion.LookRotation(forward, up);
            var inverse = Quaternion.Inverse(rotation);
            var local = vertices.Select(v => inverse * v).ToArray();
            var bounds = new Bounds(local[0], Vector3.zero);
            foreach (var point in local) bounds.Encapsulate(point);
            float epsilon = Mathf.Max(.0005f, cellSize * .002f);
            if (bounds.size.x < epsilon || bounds.size.y < epsilon || bounds.size.z < epsilon) return false;
            var seen = new bool[6];
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = local[indices[i]]; var b = local[indices[i + 1]]; var c = local[indices[i + 2]];
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-12f) continue;
                var normal = cross.normalized;
                int axis = Mathf.Abs(normal.x) > Mathf.Abs(normal.y) ? 0 : 1;
                if (Mathf.Abs(normal.z) > Mathf.Abs(normal[axis])) axis = 2;
                if (Mathf.Abs(normal[axis]) < .99999f) return false;
                bool low = Mathf.Abs(a[axis] - bounds.min[axis]) < epsilon;
                bool high = Mathf.Abs(a[axis] - bounds.max[axis]) < epsilon;
                if (!low && !high || Mathf.Abs(a[axis] - b[axis]) > epsilon || Mathf.Abs(a[axis] - c[axis]) > epsilon) return false;
                seen[axis * 2 + (high ? 1 : 0)] = true;
            }
            if (seen.Any(s => !s)) return false;
            // Require complete box area; rejects incomplete boxes, apertures and overlapping sheets.
            float expected = 4 * (bounds.size.x * bounds.size.y + bounds.size.x * bounds.size.z + bounds.size.y * bounds.size.z);
            if (Mathf.Abs(totalArea - expected) > expected * .001f) return false;
            dimensions = new Vector3Int(Mathf.Max(1, Mathf.RoundToInt(bounds.size.x / cellSize)), Mathf.Max(1, Mathf.RoundToInt(bounds.size.y / cellSize)), Mathf.Max(1, Mathf.RoundToInt(bounds.size.z / cellSize)));
            if ((long)dimensions.x * dimensions.y * dimensions.z > 2000000) return false;
            center = rotation * bounds.center;
            return true;
        }

        static void CreateLayout()
        {
            var source = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(Original + "/Layouts/ReferenceMiningStation3500.asset");
            string path = Root + "/Layouts/SaturnRingVoxels.asset";
            var clone = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(path);
            if (!clone) { clone = UnityEngine.Object.Instantiate(source); clone.name = "SaturnRingVoxels"; AssetDatabase.CreateAsset(clone, path); }
            else EditorUtility.CopySerialized(source, clone);
            foreach (var instance in clone.instances)
            {
                string key = instance.asset.name;
                var frame = frames[key];
                var oldRotation = Quaternion.Euler(instance.eulerAngles);
                instance.asset = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(Root + "/Assets/" + key + ".asset");
                if (frame.aligned)
                {
                    instance.position += oldRotation * (frame.origin * instance.scale);
                    var newRotation = oldRotation * frame.rotation;
                    instance.angularVelocity = Quaternion.Inverse(newRotation) * (oldRotation * instance.angularVelocity);
                    instance.eulerAngles = newRotation.eulerAngles;
                }
            }
            var byId = clone.instances.ToDictionary(i => i.id);
            foreach (var link in clone.assemblyLinks)
            {
                link.anchorCellA = NearestCell(byId[link.instanceIdA], link.worldAnchor);
                link.anchorCellB = NearestCell(byId[link.instanceIdB], link.worldAnchor);
            }
            if (!clone.Validate(out string reason)) throw new InvalidOperationException(reason);
            EditorUtility.SetDirty(clone); AssetDatabase.SaveAssets();
        }

        static Vector3Int NearestCell(VoxelMapInstance instance, Vector3 worldAnchor)
        {
            var point = instance.Matrix.inverse.MultiplyPoint3x4(worldAnchor) / instance.asset.cellSize;
            float best = float.PositiveInfinity; Vector3Int nearest = default;
            foreach (var cell in instance.asset.cells)
            {
                float distance = ((Vector3)cell.position - point).sqrMagnitude;
                if (distance < best) { best = distance; nearest = cell.position; }
            }
            return nearest;
        }

        static void CreateBackdrop()
        {
            var rocks = sources.Where(p => Path.GetFileName(p).StartsWith("field_", StringComparison.Ordinal))
                .Select(p => AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(Root + "/Assets/" + Path.GetFileName(p)))
                .Where(a => a.cells.Count >= 32 && a.cells.Count <= 260).OrderBy(a => a.cells.Count).ToArray();
            if (rocks.Length < 4) throw new InvalidOperationException("Insufficient native rock prototypes for backdrop.");
            int[] picks = { rocks.Length - 1, rocks.Length * 3 / 4, 0, rocks.Length / 8 };
            for (int i = 0; i < 4; i++)
            {
                var rock = rocks[picks[i]];
                var mesh = UnityEngine.Object.Instantiate(rock.initialLodMesh);
                mesh.name = "VoxelBackdrop" + i;
                var bounds = mesh.bounds;
                float scale = 2 / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                mesh.vertices = mesh.vertices.Select(v => (v - bounds.center) * scale).ToArray();
                // Backdrop uses one material and one instanced submesh; every triangle remains a native hull face.
                var indices = mesh.triangles;
                mesh.subMeshCount = 1; mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
                string path = Root + "/Backdrop/VoxelBackdrop" + i + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                else AssetDatabase.CreateAsset(mesh, path);
            }
        }
    }
}
