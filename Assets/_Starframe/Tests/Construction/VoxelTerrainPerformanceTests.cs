using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Starframe.Tests
{
    // Isolated EditMode contract tests for the terrain renderer, sparse HP and detached visual lifetime.
    // No global cache clear, BlockModules.Configure, Physics.SyncTransforms, default PhysicsScene
    // simulation or saved asset edits. Non-preview instances keep the original physical contract
    // (one Rigidbody with enabled compound colliders); this fixture never calls Physics.Simulate.
    // IdleCellVisits is the documented contractual constant 0, never evidence of observed cell visits.
    // Managed-allocation assertions run only after a positive control proves the Unity counter moves.
    public sealed class VoxelTerrainPerformanceTests
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // The runtime contract: 4-cube chunks are prewarmed only for hull assets with at least 512 cells.
        const int ChunkEdge = 4;
        const int MinimumChunkCells = 512;
        const string ModuleDatabasePath = "Assets/_Starframe/Settings/Modules/ModuleDatabase.asset";
        // The Unity TestRunner opens an untitled scene, where EditorSceneManager.NewScene(Additive) is
        // unreliable. A saved empty additive fixture keeps creation/destruction isolated and reproducible.
        const string StagingScenePath = "Assets/_Starframe/Tests/Construction/Fixtures/TerrainTestStaging.unity";

        readonly List<VoxelAssetDefinition> sources = new List<VoxelAssetDefinition>();
        Scene previous, staging, preview;
        GameObject root;
        SimulationMode physicsMode;
        Vector3 gravity;
        float fixedStep;
        int originalSceneCount;
        List<int> originalRoots;
        bool originalDirty, stagingOpenedByTest;

        [SetUp] public void Setup()
        {
            Assert.That(Application.isPlaying, Is.False, "EditMode fixture only.");
            previous = SceneManager.GetActiveScene(); originalSceneCount = SceneManager.sceneCount;
            originalRoots = previous.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToList();
            originalDirty = previous.isDirty;
            physicsMode = Physics.simulationMode; gravity = Physics.gravity; fixedStep = Time.fixedDeltaTime;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                Assert.That(System.IO.File.Exists(StagingScenePath), Is.True,
                    "A saved empty staging scene is required: " + StagingScenePath);
                var loadedStaging = SceneManager.GetSceneByPath(StagingScenePath);
                stagingOpenedByTest = !(loadedStaging.IsValid() && loadedStaging.isLoaded);
                staging = stagingOpenedByTest
                    ? EditorSceneManager.OpenScene(StagingScenePath, OpenSceneMode.Additive)
                    : loadedStaging;
                Assert.That(staging.IsValid() && staging.isLoaded, Is.True, "Saved staging scene must be loaded additively.");
                Assert.That(SceneManager.SetActiveScene(staging), Is.True);
                Assert.That(preview.GetPhysicsScene().IsValid(), Is.True);
                Assert.That(preview.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
                root = new GameObject("Isolated terrain performance fixture");
                SceneManager.MoveGameObjectToScene(root, preview);
            }
            catch
            {
                // NUnit does not run TearDown when SetUp throws, so never leak the preview/staging scenes.
                if (root) { DisposeOwnedResources(root); Object.DestroyImmediate(root); }
                if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
                if (stagingOpenedByTest && staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                throw;
            }
        }
        [TearDown] public void Cleanup()
        {
            if (root) { DisposeOwnedResources(root); Object.DestroyImmediate(root); }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (stagingOpenedByTest && staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
            foreach (var source in sources) if (source) Object.DestroyImmediate(source);
            sources.Clear();
            Assert.That(Physics.simulationMode, Is.EqualTo(physicsMode));
            Assert.That(Physics.gravity, Is.EqualTo(gravity)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(previous.handle));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(originalSceneCount));
            Assert.That(previous.isDirty, Is.EqualTo(originalDirty));
            CollectionAssert.AreEqual(originalRoots, previous.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToList());
        }
        // EditMode DestroyImmediate does not dispatch OnDestroy to non-ExecuteAlways components, so the
        // fixture must dispose template/chunk/template-visual leases explicitly before destroying objects.
        static void DisposeOwnedResources(GameObject owner)
        {
            if (!owner) return;
            foreach (var runtime in owner.GetComponentsInChildren<VoxelStructureRuntime>(true)) if (runtime) runtime.DisposeResources();
        }

        VoxelAssetDefinition Source(IEnumerable<Vector3Int> positions, bool hull = false)
        {
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); sources.Add(source);
            foreach (var p in positions) source.cells.Add(new VoxelCell { position = p });
            if (hull)
            {
                source.hullModules = AssetDatabase.LoadAssetAtPath<ModuleDatabase>(ModuleDatabasePath);
                Assert.That(source.hullModules, Is.Not.Null);
            }
            return source;
        }
        static IEnumerable<Vector3Int> Box(int x, int y, int z, int offset = 0)
        {
            for (int i = 0; i < x; i++) for (int j = 0; j < y; j++) for (int k = 0; k < z; k++) yield return new Vector3Int(i + offset, j, k);
        }
        VoxelStructureRuntime Body(VoxelAssetDefinition source, int tiny = 0, float scale = 1)
        {
            var go = new GameObject("Isolated terrain body");
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * scale;
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            runtime.CollectDiagnostics = true;
            runtime.Initialize(source);
            return runtime;
        }
        static object Member(object target, string name)
        {
            if (target == null) return null;
            var t = target.GetType(); var p = t.GetProperty(name, Flags);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
            var f = t.GetField(name, Flags); return f == null ? null : f.GetValue(target);
        }
        static long Number(object target, string name, long fallback = -1) { var v = Member(target, name); return v == null ? fallback : Convert.ToInt64(v); }
        static IReadOnlyList<Mesh> Meshes(VoxelStructureRuntime body) => body.RenderMeshes;
        static List<int> MeshIds(VoxelStructureRuntime body) => body.RenderMeshes.Select(m => m.GetInstanceID()).OrderBy(i => i).ToList();
        static List<int> ColliderIds(VoxelStructureRuntime body)
        {
            return Colliders(body).Select(c => c.GetInstanceID()).OrderBy(i => i).ToList();
        }
        // VoxelTerrainColliders intentionally retains unused components disabled in its pool after
        // compaction. Only collision.Active belongs to the physical volume; pooled components stay out.
        static List<BoxCollider> Colliders(VoxelStructureRuntime body)
        {
            var helper = Member(body, "collision") as VoxelTerrainColliders;
            Assert.That(helper, Is.Not.Null, "The runtime collider helper is required to distinguish active from pooled components.");
            var active = helper.Active.Where(c => c).ToList();
            Assert.That(active.Count, Is.EqualTo(body.ColliderCount),
                "Active compound collider components must equal the runtime's active collider count.");
            foreach (var collider in active)
                Assert.That(collider.enabled, Is.True, "An active compound collider must stay enabled.");
            var all = body.GetComponentsInChildren<BoxCollider>(true)
                .Where(c => c.GetComponentInParent<VoxelStructureRuntime>() == body).ToList();
            var activeIds = new HashSet<int>(active.Select(c => c.GetInstanceID()));
            var pooled = all.Where(c => !activeIds.Contains(c.GetInstanceID())).ToList();
            Assert.That(pooled.All(c => !c.enabled), Is.True,
                "Pooled/inactive collider components must stay disabled and outside the active physical volume.");
            return active;
        }
        static MeshFilter RootFilter(VoxelStructureRuntime body) => body.GetComponent<MeshFilter>();
        static MeshRenderer RootRenderer(VoxelStructureRuntime body) => body.GetComponent<MeshRenderer>();
        static int ChildMeshFilters(VoxelStructureRuntime body)
        {
            return body.GetComponentsInChildren<MeshFilter>(true).Count(f => f.gameObject != body.gameObject);
        }
        static bool ChunksActive(VoxelStructureRuntime body)
        {
            var chunks = Member(body, "chunks");
            return chunks != null && Member(chunks, "HasChunks") is bool active && active;
        }

        static void Require(object target, string name) { if (Member(target, name) == null) Assert.Ignore("New terrain API is not installed: " + name); }
        static void AssertMeshIds(List<int> expected, VoxelStructureRuntime body) { CollectionAssert.AreEqual(expected, MeshIds(body)); }
        static void AssertEditModePhysicsContract(VoxelStructureRuntime body)
        {
            // The original non-preview EditMode contract keeps a real Rigidbody and enabled compound
            // colliders. Nothing here calls Physics.Simulate and the Editor does not step physics.
            Assert.That(body.Body, Is.Not.Null, "Non-preview EditMode terrain must keep its physical Rigidbody.");
            Assert.That(body.GetComponents<Rigidbody>().Length, Is.EqualTo(1));
            Assert.That(body.Body.useGravity, Is.False);
            Assert.That(body.Body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative), "The restored physical contract uses ContinuousSpeculative collision detection.");
            Assert.That(body.Body.isKinematic, Is.EqualTo(body.AliveCount == 0));
            var activeColliders = Colliders(body);
            Assert.That(activeColliders.Count, Is.EqualTo(body.ColliderCount),
                "Active colliders must match ColliderCount; pooled disabled components are not physical colliders.");
        }


        [Test] public void ImmutableIdleMapHasNoPerFrameDamageMaintenanceOrRebuilds()
        {
            var body=Body(Source(Box(16,16,16)));
            int geometry=body.GeometryBuildCount,colliders=body.ColliderBuildCount,revision=body.RenderRevision;
            var meshes=MeshIds(body);var boxes=ColliderIds(body);
            Assert.That(typeof(VoxelStructureRuntime).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic),Is.Null);
            for(int i=0;i<2048;i++){var matrix=body.PhysicsMatrix;Assert.That(matrix.m33,Is.EqualTo(1));}
            Assert.That(body.GeometryBuildCount,Is.EqualTo(geometry));Assert.That(body.ColliderBuildCount,Is.EqualTo(colliders));Assert.That(body.RenderRevision,Is.EqualTo(revision));
            AssertMeshIds(meshes,body);CollectionAssert.AreEqual(boxes,ColliderIds(body));AssertEditModePhysicsContract(body);
        }

        // Render-only admission is available for non-preview runtimes. The capability probe confirms
        // this fixture's EditMode runtime admits it; a configuration that refuses it is reported as
        // skipped instead of asserting a false contract.

        static int TemplateCacheCount()
        {
            var field = typeof(VoxelTemplateCache).GetField("cache", BindingFlags.Static | BindingFlags.NonPublic);
            return field != null && field.GetValue(null) is IDictionary cache ? cache.Count : -1;
        }
        static int SharedChunkTemplateCount()
        {
            var field = typeof(VoxelTerrainRenderChunks).GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
            return field != null && field.GetValue(null) is IDictionary shared ? shared.Count : -1;
        }

        static void AssertActiveRenderersMatch(VoxelStructureRuntime body, Material[] materials)
        {
            var active = body.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.GetComponentInParent<VoxelStructureRuntime>() == body)
                .ToArray();
            Assert.That(active.Length, Is.EqualTo(body.RenderMeshes.Count), "Active renderers must equal the mounted render meshes.");
            foreach (var renderer in active)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                Assert.That(body.RenderMeshes.Contains(filter.sharedMesh), Is.True);
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(materials.Length));
                for (int slot = 0; slot < materials.Length; slot++)
                    Assert.That(renderer.sharedMaterials[slot], Is.SameAs(materials[slot]), "Chunk material/surface slot " + slot + " changed.");
                var local = body.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                    Assert.That(local[row, column], Is.EqualTo(Matrix4x4.identity[row, column]).Within(.00001f), "Chunk vertices must stay in asset-local coordinates.");
            }
        }

        static void AssertExactCellCollision(VoxelStructureRuntime body, ICollection<Vector3Int> expected)
        {
            var boxes = Colliders(body); // collision.Active only: pooled disabled components are not physical.
            Assert.That(boxes.Count, Is.GreaterThan(0));
            Assert.That(boxes.Count, Is.EqualTo(body.ColliderCount), "Active collider count must equal the runtime's active compound count.");
            double volume = 0;
            foreach (var box in boxes)
                volume += box.size.x * box.size.y * box.size.z * Math.Abs(box.transform.localToWorldMatrix.determinant);
            float size = body.asset.cellSize;
            double scale = Math.Abs(body.transform.lossyScale.x * body.transform.lossyScale.y * body.transform.lossyScale.z);
            Assert.That(volume, Is.EqualTo(expected.Count * Math.Pow(size, 3) * scale).Within(Math.Max(.001, expected.Count * .0001)));
            int minX = expected.Min(p => p.x) - 1, maxX = expected.Max(p => p.x) + 1;
            int minY = expected.Min(p => p.y) - 1, maxY = expected.Max(p => p.y) + 1;
            int minZ = expected.Min(p => p.z) - 1, maxZ = expected.Max(p => p.z) + 1;
            for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++) for (int z = minZ; z <= maxZ; z++)
            {
                var p = new Vector3Int(x, y, z); int hits = 0;
                Vector3 world = body.transform.TransformPoint((Vector3)p * size);
                foreach (var box in boxes)
                {
                    var local = box.transform.InverseTransformPoint(world) - box.center;
                    if (Mathf.Abs(local.x) < box.size.x * .5f && Mathf.Abs(local.y) < box.size.y * .5f && Mathf.Abs(local.z) < box.size.z * .5f) hits++;
                }
                Assert.That(hits, Is.EqualTo(expected.Contains(p) ? 1 : 0), "Exact full-cell union at " + p);
            }
        }
        static Dictionary<Mesh, List<string>> TrianglesByMesh(VoxelStructureRuntime body)
        {
            var result = new Dictionary<Mesh, List<string>>();
            foreach (var filter in body.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || filter.GetComponentInParent<VoxelStructureRuntime>() != body) continue;
                var matrix = body.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                result[filter.sharedMesh] = TriangleSignatures(filter.sharedMesh, matrix).ToList();
            }
            return result;
        }
        static List<string> SortedTriangles(VoxelStructureRuntime body)
        {
            var result = new List<string>();
            foreach (var filter in body.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh && filter.GetComponentInParent<VoxelStructureRuntime>() == body)
                    result.AddRange(TriangleSignatures(filter.sharedMesh, body.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix));
            result.Sort(StringComparer.Ordinal); return result;
        }
        static List<string> SortedTriangles(Mesh mesh, Matrix4x4 matrix)
        {
            var result = TriangleSignatures(mesh, matrix).ToList(); result.Sort(StringComparer.Ordinal); return result;
        }
        static IEnumerable<string> TriangleSignatures(Mesh mesh, Matrix4x4 matrix)
        {
            var vertices = mesh.vertices; var normals = mesh.normals;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var corners = new string[3];
                    for (int j = 0; j < 3; j++)
                    {
                        int index = triangles[i + j]; var p = matrix.MultiplyPoint3x4(vertices[index]); var n = matrix.MultiplyVector(normals[index]).normalized;
                        corners[j] = Quantize(p) + "/" + Quantize(n);
                    }
                    // Canonical cyclic order retains winding instead of treating reversed triangles as equivalent.
                    int first = string.CompareOrdinal(corners[0], corners[1]) <= 0 ? 0 : 1;
                    if (string.CompareOrdinal(corners[2], corners[first]) < 0) first = 2;
                    yield return sub + ":" + corners[first] + ";" + corners[(first + 1) % 3] + ";" + corners[(first + 2) % 3];
                }
            }
        }
        static string Quantize(Vector3 p) { return Mathf.RoundToInt(p.x * 10000) + "," + Mathf.RoundToInt(p.y * 10000) + "," + Mathf.RoundToInt(p.z * 10000); }
    }
}
