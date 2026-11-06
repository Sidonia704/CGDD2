using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.EditorTools
{
    /// <summary>Clones the established map while preserving collision cells, mass, instance transforms and assembly IDs.</summary>
    public static class SaturnSurfaceAuthoring
    {
        public const string Root = "Assets/_Starframe/Maps/SaturnRingContinuous";
        const string Original = "Assets/_Starframe/Maps/ReferenceMiningStation";
        [Serializable] public sealed class Surface { public string assetKey; public float[] vertices; public int[] triangles; }
        [Serializable] public sealed class Record { public string assetKey, path; public int triangles; }
        [Serializable] public sealed class Manifest { public Record[] assets; }
        static bool importing;
        static string importManifest, importError;
        static int importNext, importTotal, importBatchSize;
        static long importTriangles;
        static double importStarted;

        public static object BeginImport(string manifestPath, int batchSize = 20)
        {
            if (importing) throw new InvalidOperationException("Continuous surface import already running.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before authoring map assets.");
            importTotal = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath)).assets.Length;
            importManifest = manifestPath; importNext = 0; importError = null; importTriangles = 0;
            importBatchSize = Mathf.Clamp(batchSize, 1, 100); importStarted = EditorApplication.timeSinceStartup;
            importing = true; EditorApplication.update += ImportNextUpdate;
            return Status();
        }

        public static object Status() => new
        {
            running = importing, completed = importNext, total = importTotal,
            triangles = importTriangles, error = importError,
            elapsedSeconds = importStarted > 0 ? EditorApplication.timeSinceStartup - importStarted : 0,
            layoutPath = Root + "/Layouts/SaturnRingContinuous.asset"
        };

        static void ImportNextUpdate()
        {
            if (!importing || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (importNext < importTotal)
                {
                    int end = Math.Min(importTotal, importNext + importBatchSize);
                    ImportBatch(importManifest, importNext, end - importNext);
                    var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(importManifest));
                    for (int i = importNext; i < end; i++) importTriangles += manifest.assets[i].triangles;
                    importNext = end;
                    return;
                }
                CreateLayout(); CreateBackdropMeshes();
                importing = false; EditorApplication.update -= ImportNextUpdate;
                Debug.Log("Saturn continuous surfaces ready: " + importTotal + " assets, " + importTriangles + " triangles.");
            }
            catch (Exception error)
            {
                importing = false; importError = error.ToString(); EditorApplication.update -= ImportNextUpdate;
                Debug.LogException(error);
            }
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            Folder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }

        static Dictionary<string, string> Originals() => AssetDatabase.FindAssets("t:VoxelAssetDefinition", new[] { Original + "/Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).ToDictionary(Path.GetFileNameWithoutExtension);

        /// <summary>Run in bounded batches after offline Prepare.py. Only writes into the clone root.</summary>
        public static object ImportBatch(string manifestPath, int start, int count)
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            var originals = Originals();
            Folder(Root + "/Assets"); Folder(Root + "/Meshes");
            int end = Math.Min(manifest.assets.Length, start + count);
            int changed = 0; long triangles = 0;
            for (int index = start; index < end; index++)
            {
                var record = manifest.assets[index];
                var source = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(originals[record.assetKey]);
                var surface = JsonUtility.FromJson<Surface>(File.ReadAllText(record.path));
                if (surface.assetKey != record.assetKey || surface.vertices.Length % 3 != 0 || surface.triangles.Length % 3 != 0)
                    throw new InvalidDataException("Invalid surface: " + record.assetKey);
                var vertices = new Vector3[surface.vertices.Length / 3];
                for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(surface.vertices[i * 3], surface.vertices[i * 3 + 1], surface.vertices[i * 3 + 2]);
                int[] indices = surface.triangles;
                // Hard manufactured surfaces retain their original planes; rocks share normals.
                bool structure = source.category != "Reference rock" && !record.assetKey.StartsWith("dome") && !record.assetKey.StartsWith("scatter_");
                if (structure)
                {
                    vertices = indices.Select(i => vertices[i]).ToArray();
                    indices = Enumerable.Range(0, vertices.Length).ToArray();
                }
                var mesh = new Mesh { name = record.assetKey + " continuous source surface", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = vertices; mesh.subMeshCount = source.RenderMaterialCount;
                mesh.SetTriangles(indices, 0);
                for (int sub = 1; sub < mesh.subMeshCount; sub++) mesh.SetTriangles(Array.Empty<int>(), sub);
                mesh.RecalculateNormals();
                if (structure) mesh.normals = CreaseNormals(vertices);
                mesh.RecalculateBounds();
                mesh.uv = vertices.Select(v => new Vector2(v.x, v.z) * .025f).ToArray();
                string meshPath = Root + "/Meshes/" + record.assetKey + ".asset";
                var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (savedMesh) { EditorUtility.CopySerialized(mesh, savedMesh); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(savedMesh); }
                else { savedMesh = mesh; AssetDatabase.CreateAsset(savedMesh, meshPath); }
                string assetPath = Root + "/Assets/" + record.assetKey + ".asset";
                var clone = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(assetPath);
                if (!clone) { clone = UnityEngine.Object.Instantiate(source); clone.name = source.name; AssetDatabase.CreateAsset(clone, assetPath); }
                else EditorUtility.CopySerialized(source, clone);
                clone.initialLodMesh = savedMesh;
                clone.initialLodSignature = SpaceMapRuntime.ComputeGeometrySignature(clone);
                clone.NotifyChanged(); EditorUtility.SetDirty(clone);
                if (!clone.Validate(out string error) || !clone.HasValidInitialLod) throw new InvalidOperationException(record.assetKey + ": " + error);
                changed++; triangles += indices.Length / 3;
            }
            AssetDatabase.SaveAssets();
            return new { start, end, total = manifest.assets.Length, changed, triangles };
        }

        static Vector3[] CreaseNormals(Vector3[] vertices)
        {
            var adjacent = new Dictionary<Vector3, List<Vector3>>();
            var faces = new Vector3[vertices.Length / 3];
            for (int triangle = 0; triangle < faces.Length; triangle++)
            {
                int start = triangle * 3;
                var normal = Vector3.Cross(vertices[start + 1] - vertices[start], vertices[start + 2] - vertices[start]).normalized;
                faces[triangle] = normal;
                for (int corner = 0; corner < 3; corner++)
                {
                    var key = vertices[start + corner];
                    if (!adjacent.TryGetValue(key, out var normals)) adjacent.Add(key, normals = new List<Vector3>());
                    if (!normals.Contains(normal)) normals.Add(normal);
                }
            }
            var result = new Vector3[vertices.Length];
            float threshold = Mathf.Cos(45f * Mathf.Deg2Rad);
            for (int i = 0; i < result.Length; i++)
            {
                Vector3 sum = Vector3.zero, face = faces[i / 3];
                foreach (var normal in adjacent[vertices[i]]) if (Vector3.Dot(face, normal) > threshold) sum += normal;
                result[i] = sum.normalized;
            }
            return result;
        }

        public static object CreateLayout()
        {
            Folder(Root + "/Layouts");
            var source = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(Original + "/Layouts/ReferenceMiningStation3500.asset");
            string path = Root + "/Layouts/SaturnRingContinuous.asset";
            var clone = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(path);
            if (!clone) { clone = UnityEngine.Object.Instantiate(source); clone.name = "SaturnRingContinuous"; AssetDatabase.CreateAsset(clone, path); }
            else EditorUtility.CopySerialized(source, clone);
            foreach (var instance in clone.instances)
            {
                if (!instance.asset) continue;
                var replacement = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(Root + "/Assets/" + instance.asset.name + ".asset");
                if (!replacement || !replacement.HasValidInitialLod) throw new InvalidOperationException("Missing continuous surface: " + instance.asset.name);
                instance.asset = replacement;
            }
            if (!clone.Validate(out string error)) throw new InvalidOperationException(error);
            EditorUtility.SetDirty(clone); AssetDatabase.SaveAssets();
            return new { path, instances = clone.instances.Count, assets = clone.instances.Select(i => i.asset).Distinct().Count(), cells = clone.instances.Sum(i => (long)i.asset.cells.Count) };
        }

        /// <summary>Four shared smooth rock meshes, preserving existing instancing and culling budgets.</summary>
        public static object CreateBackdropMeshes()
        {
            Folder(Root + "/Backdrop");
            var paths = new List<string>();
            for (int shape = 0; shape < 4; shape++)
            {
                int longitude = shape < 2 ? 16 : 8, latitude = shape < 2 ? 10 : 5;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                // Unique poles avoid degenerate triangles and unstable normals.
                vertices.Add(Vector3.up * .9f);
                for (int y = 1; y < latitude; y++)
                {
                    float phi = Mathf.PI * y / latitude;
                    for (int x = 0; x < longitude; x++)
                    {
                        float theta = 2 * Mathf.PI * x / longitude;
                        var direction = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                        float r = .86f + .08f * Mathf.Sin(direction.x * 4.1f + shape) * Mathf.Cos(direction.z * 3.3f - direction.y * 2.7f + shape * .7f);
                        vertices.Add(direction * r);
                    }
                }
                int bottom = vertices.Count; vertices.Add(Vector3.down * .88f);
                for (int x = 0; x < longitude; x++)
                {
                    int next = (x + 1) % longitude;
                    triangles.AddRange(new[] { 0, 1 + next, 1 + x });
                    for (int y = 0; y < latitude - 2; y++)
                    {
                        int a = 1 + y * longitude + x, b = 1 + y * longitude + next, c = a + longitude, d = b + longitude;
                        triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
                    int last = 1 + (latitude - 2) * longitude;
                    triangles.AddRange(new[] { last + x, last + next, bottom });
                }
                var mesh = new Mesh { name = "ContinuousBackdrop" + shape };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                mesh.uv = vertices.Select(v => new Vector2(v.x, v.z)).ToArray();
                string path = Root + "/Backdrop/" + mesh.name + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                else AssetDatabase.CreateAsset(mesh, path);
                paths.Add(path);
            }
            AssetDatabase.SaveAssets(); return paths;
        }
    }
}
