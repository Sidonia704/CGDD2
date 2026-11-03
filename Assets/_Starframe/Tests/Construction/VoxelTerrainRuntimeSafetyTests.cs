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
    // Focused EditMode safety contract for terrain flush retry, tiny-policy admission, empty chunk bindings,
    // retained-body continuity and committed-piece preservation. Physics is isolated in a preview scene,
    // the preview scene is never made active, and no global cache, Physics or source setting is touched.
    public sealed class VoxelTerrainRuntimeSafetyTests
    {
        const string StagingScenePath = "Assets/_Starframe/Tests/Construction/Fixtures/TerrainTestStaging.unity";
        const string ModuleDatabasePath = "Assets/_Starframe/Settings/Modules/ModuleDatabase.asset";

        readonly List<VoxelAssetDefinition> sources = new List<VoxelAssetDefinition>();
        Scene previous, staging, preview;
        GameObject root;
        bool originalDirty;
        int originalSceneCount;
        List<int> originalRoots;

        [SetUp]
        public void Setup()
        {
            Assert.That(Application.isPlaying, Is.False, "EditMode fixture only.");
            previous = SceneManager.GetActiveScene();
            originalSceneCount = SceneManager.sceneCount;
            originalRoots = previous.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToList();
            originalDirty = previous.isDirty;
            preview = EditorSceneManager.NewPreviewScene();
            Assert.That(preview.IsValid() && preview.GetPhysicsScene().IsValid(), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(StagingScenePath), Is.Not.Null, "Missing terrain staging fixture: " + StagingScenePath);
            staging = EditorSceneManager.OpenScene(StagingScenePath, OpenSceneMode.Additive);
            Assert.That(staging.IsValid() && staging.isLoaded, Is.True);
            Assert.That(SceneManager.SetActiveScene(staging), Is.True);
            root = new GameObject("Terrain runtime safety fixture");
            SceneManager.MoveGameObjectToScene(root, preview);
        }

        [TearDown]
        public void Cleanup()
        {
            if (root) Object.DestroyImmediate(root);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
            foreach (var source in sources) if (source) Object.DestroyImmediate(source);
            sources.Clear();
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(previous.handle));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(originalSceneCount));
            Assert.That(previous.isDirty, Is.EqualTo(originalDirty));
            CollectionAssert.AreEqual(originalRoots, previous.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToList());
        }

        VoxelAssetDefinition Source(IEnumerable<Vector3Int> positions, bool hull = false)
        {
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); sources.Add(source);
            source.damageResetSeconds = 10;
            foreach (var position in positions) source.cells.Add(new VoxelCell { position = position });
            if (hull)
            {
                source.hullModules = AssetDatabase.LoadAssetAtPath<ModuleDatabase>(ModuleDatabasePath);
                Assert.That(source.hullModules, Is.Not.Null);
            }
            return source;
        }
        static IEnumerable<Vector3Int> Box(int x, int y, int z)
        {
            for (int i = 0; i < x; i++) for (int j = 0; j < y; j++) for (int k = 0; k < z; k++) yield return new Vector3Int(i, j, k);
        }
        VoxelStructureRuntime Body(VoxelAssetDefinition source, int tiny = 0, Transform parent = null)
        {
            var go = new GameObject("Terrain safety body");
            if (go.scene != root.scene) SceneManager.MoveGameObjectToScene(go, root.scene);
            go.transform.SetParent(parent ? parent : root.transform, false);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            runtime.tinyFragmentMaxCells = tiny; runtime.tinyFragmentMaxWorldExtent = 2f;
            runtime.Initialize(source);
            return runtime;
        }
        GameObject Holder(string name)
        {
            var holder = new GameObject(name);
            if (holder.scene != root.scene) SceneManager.MoveGameObjectToScene(holder, root.scene);
            holder.transform.SetParent(root.transform, false);
            return holder;
        }
        object FindVisualManager() => root.GetComponentsInChildren<MonoBehaviour>(true)
            .FirstOrDefault(component => component && component.GetType().Name == "VoxelDetachedVisualFragments");

        [Test]
        public void FailedFlushRetriesAsFullRebuildAndNeverLeavesADestroyedCell()
        {
            var positions = new List<Vector3Int>(Box(3, 3, 3)); positions.Add(new Vector3Int(3, 0, 0));
            positions.AddRange(new[] { new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0) });
            var source = Source(positions);
            var body = Body(source, 4);
            Assert.That(body.AliveCount, Is.EqualTo(31));
            var field = typeof(VoxelStructureRuntime).GetField("materials", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var palette = (Material[])field.GetValue(body);
            try
            {
                // A visible render-only submesh without a material fails the tiny admission after the island mesh is
                // built, which is exactly the staging failure the retry contract must survive.
                field.SetValue(body, new Material[] { null });
                Assert.Throws<ArgumentException>(() => { body.ApplyDamage(new Vector3Int(3, 0, 0), 1000); body.FlushMeshChanges(); },
                    "A failed staging pass must surface instead of silently dropping the deletion.");
                Assert.That(body.AliveCount, Is.EqualTo(30), "The failed pass must leave the island owned by the donor.");
                Assert.That(body.DetachedVisualCells, Is.Zero);
                Assert.That(root.GetComponentsInChildren<VoxelStructureRuntime>(true).Length, Is.EqualTo(1), "A failed staging pass must not leave a runtime behind.");
            }
            finally { field.SetValue(body, palette); }
            body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            Assert.That(pieces.Length, Is.EqualTo(2), "The retried pass must complete the split instead of being marked clean.");
            Assert.That(body.AliveCount, Is.EqualTo(27));
            Assert.That(pieces.Sum(piece => piece.AliveCount), Is.EqualTo(30));
            Assert.That(body.ColliderCount, Is.EqualTo(1), "The retry must rebuild collision without the destroyed bridge cell.");
            Assert.That(body.DetachedVisualCells, Is.Zero, "A retried split is conservative: physical, not render-only.");
            foreach (var piece in pieces) Assert.That(piece.ColliderCount, Is.GreaterThan(0));
            var manager = FindVisualManager();
            Assert.That(manager == null || ((VoxelDetachedVisualFragments)manager).CellCount == 0, Is.True, "The failed attempt must not keep a visual fragment.");
        }

        [Test]
        public void DataOnlyPreviewStillPreparesTheHullMaterialPalette()
        {
            // VoxelPreviewInstances indexes the runtime root MeshRenderer palette by surface/shape slot, so a
            // data-only preview must keep the exact hull palette even though it owns no aggregate geometry.
            var source = Source(new[] { Vector3Int.zero }, true);
            var go = new GameObject("Terrain data-only palette body");
            if (go.scene != root.scene) SceneManager.MoveGameObjectToScene(go, root.scene);
            go.transform.SetParent(root.transform, false);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            runtime.Initialize(source, true, false);
            Assert.That(runtime.DataOnlyPreview, Is.True);
            var renderer = go.GetComponent<MeshRenderer>();
            var palette = renderer.sharedMaterials;
            Assert.That(palette.Length, Is.EqualTo(source.SurfaceCount * VoxelHullAppearance.ShapeCount));
            Assert.That(palette.All(material => material), Is.True);
            for (int surface = 0; surface < source.SurfaceCount; surface++)
                for (int shape = 0; shape < VoxelHullAppearance.ShapeCount; shape++)
                {
                    int slot = surface * VoxelHullAppearance.ShapeCount + shape;
                    var moduleMaterial = source.hullModules.Get(VoxelHullAppearance.Shapes[shape]).material;
                    if (moduleMaterial) Assert.That(palette[slot], Is.SameAs(moduleMaterial), "Data-only preview must expose the editor preview's material identity.");
                }
            Assert.That(renderer.enabled, Is.False);
            Assert.That(go.GetComponent<MeshFilter>().sharedMesh, Is.Null);
            Assert.That(runtime.RenderMeshCount, Is.Zero); Assert.That(runtime.TotalVertices, Is.Zero);
            Assert.That(runtime.ColliderCount, Is.Zero); Assert.That(go.GetComponentsInChildren<BoxCollider>(true), Is.Empty);
            Assert.That(go.GetComponent<Rigidbody>(), Is.Null);
        }

        [Test]
        public void FailedSharedPreviewBindingReleasesOnlyItsOwnGeometryLease()
        {
            var source = Source(new[] { Vector3Int.zero }, true);
            var go = new GameObject("Terrain shared preview lease body");
            if (go.scene != root.scene) SceneManager.MoveGameObjectToScene(go, root.scene);
            go.transform.SetParent(root.transform, false);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            runtime.Initialize(source, true, false);
            var renderer = go.GetComponent<MeshRenderer>();
            var palette = renderer.sharedMaterials;
            var previewType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Starframe.EditorTools.VoxelPreviewInstances", false))
                .FirstOrDefault(type => type != null);
            Assert.That(previewType, Is.Not.Null, "The editor preview implementation must be loaded for this EditMode regression.");
            var constructor = previewType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(VoxelStructureRuntime) }, null);
            Assert.That(constructor, Is.Not.Null);
            var cacheField = previewType.GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
            var cache = (IDictionary)cacheField.GetValue(null);
            var disposeMethod = previewType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var releaseMethod = previewType.GetMethod("ReleaseGeometry", BindingFlags.Static | BindingFlags.NonPublic);
            object firstPreview = null;
            try
            {
                firstPreview = constructor.Invoke(new object[] { runtime });
                object geometry = cache[source];
                var usersField = geometry.GetType().GetField("users", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.That(usersField.GetValue(geometry), Is.EqualTo(1));
                int childCount = runtime.transform.childCount;

                // A cache hit acquires a lease before child binding. An empty palette forces that binding to fail,
                // which must release only the failed instance's lease and remove its partially-created view.
                renderer.sharedMaterials = Array.Empty<Material>();
                Assert.Throws<TargetInvocationException>(() => constructor.Invoke(new object[] { runtime }));
                Assert.That(usersField.GetValue(geometry), Is.EqualTo(1));
                Assert.That(runtime.transform.childCount, Is.EqualTo(childCount));
                Assert.That((bool)previewType.GetProperty("Valid").GetValue(firstPreview), Is.True,
                    "The failed second binding must leave the first preview's shared mesh alive.");
            }
            finally
            {
                renderer.sharedMaterials = palette;
                if (firstPreview != null) disposeMethod.Invoke(firstPreview, null);
                // Also scrub the leaked lease this assertion detects when running against a broken implementation,
                // so a failing regression cannot contaminate later tests with a pinned static cache entry.
                int safety = 0;
                while (cache.Contains(source) && safety++ < 4) releaseMethod.Invoke(null, new[] { cache[source] });
            }
        }

        [Test]
        public void InitiallyDisconnectedIslandsStayPhysicalWhenTinyPolicyIsOptedIn()
        {
            var source = Source(new[] { Vector3Int.zero, Vector3Int.right, new Vector3Int(10, 0, 0), new Vector3Int(11, 0, 0) });
            var body = Body(source, 4);
            Assert.That(body.AliveCount, Is.EqualTo(4));
            body.ApplyDamage(Vector3Int.right, 1000); body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            Assert.That(pieces.Length, Is.EqualTo(2), "The two authored islands must separate physically.");
            Assert.That(pieces.Sum(piece => piece.AliveCount), Is.EqualTo(3));
            Assert.That(pieces.Sum(piece => piece.DetachedVisualCells), Is.Zero, "An island that was never connected must not become render-only.");
            Assert.That(pieces.Sum(piece => piece.DetachedVisualFragmentCount), Is.Zero);
            Assert.That(FindVisualManager(), Is.Null);
            foreach (var piece in pieces) Assert.That(piece.ColliderCount, Is.GreaterThan(0));
        }

        [Test]
        public void FullyEnclosedInteriorChunksDoNotKeepChildRenderBindings()
        {
            var source = Source(Box(16, 16, 16), true);
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.True);
            var body = Body(source);
            body.ApplyDamage(Vector3Int.zero, 1000); body.FlushMeshChanges();
            Assert.That(body.RenderMeshCount, Is.GreaterThan(1), "The fixture must activate chunk rendering.");
            var children = body.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.gameObject != body.gameObject).ToArray();
            Assert.That(children.Length, Is.EqualTo(body.RenderMeshCount), "A fully enclosed chunk must not mount a child render binding.");
            foreach (var filter in children)
                Assert.That(filter.sharedMesh && filter.sharedMesh.vertexCount > 0, Is.True, "Child bindings must own real geometry.");
        }

        [Test]
        public void SplitKeepsRetainedBodyCentreOfMassPointVelocityContinuous()
        {
            var source = Source(Enumerable.Range(0, 5).Select(index => Vector3Int.right * index));
            var body = Body(source);
            Assert.That(body.Body, Is.Not.Null);
            body.Body.linearVelocity = new Vector3(3, 2, 4); body.Body.angularVelocity = Vector3.up;
            var velocity = body.Body.linearVelocity; var spin = body.Body.angularVelocity;
            var before = body.Body.worldCenterOfMass;
            body.ApplyDamage(Vector3Int.right * 2, 1000); body.FlushMeshChanges();
            Assert.That(root.GetComponentsInChildren<VoxelStructureRuntime>(true).Length, Is.EqualTo(2));
            var expected = velocity + Vector3.Cross(spin, body.Body.worldCenterOfMass - before);
            Assert.That(Vector3.Distance(body.Body.linearVelocity, expected), Is.LessThan(.001f),
                "The retained body must keep its centre-of-mass point velocity after the split.");
        }

        [Test]
        public void InitialConnectivityCertificationIsCachedPerGenerationAndSeedsExactCellCopies()
        {
            // Gen1 is a connected authored line: its certification is made once in the template (cold cost).
            var source = Source(new[] { Vector3Int.zero, Vector3Int.right, Vector3Int.right * 2 });
            var firstHolder = Holder("Terrain connectivity gen1");
            var first = Body(source, 0, firstHolder.transform);
            Assert.That(first.ConnectivityPassCount, Is.Zero, "A complete template cell copy must seed the certification, not re-run the BFS.");

            // Gen2 mutates the same ephemeral asset into two authored islands; the geometry revision is invalidated.
            source.cells.Clear();
            source.cells.Add(new VoxelCell { position = Vector3Int.zero });
            source.cells.Add(new VoxelCell { position = Vector3Int.right });
            source.cells.Add(new VoxelCell { position = new Vector3Int(10, 0, 0) });
            source.cells.Add(new VoxelCell { position = new Vector3Int(11, 0, 0) });
            source.NotifyChanged();
            var secondHolder = Holder("Terrain connectivity gen2");
            var second = Body(source, 0, secondHolder.transform);
            Assert.That(second.ConnectivityPassCount, Is.Zero, "The new generation's certification must be built once and seeded too.");
            second.ApplyDamage(Vector3Int.zero, 1000); second.FlushMeshChanges();
            var pieces = secondHolder.GetComponentsInChildren<VoxelStructureRuntime>(true);
            Assert.That(pieces.Length, Is.EqualTo(2), "A disconnected generation must not inherit the previous generation's connected certification.");
            Assert.That(second.AliveCount, Is.EqualTo(2));
            Assert.That(pieces.Sum(piece => piece.AliveCount), Is.EqualTo(3));
            Object.DestroyImmediate(firstHolder); // gen1 was held while gen2 was acquired; release it after the check.
        }

        [Test]
        public void RuntimeUsesSpeculativeContinuousCollision()
        {
            var body = Body(Source(new[] { Vector3Int.zero }));
            Assert.That(body.Body, Is.Not.Null);
            Assert.That(body.Body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative));
        }

        [Test]
        public void ThrowingFragmentSubscriberKeepsCommittedPiecesAndLeavesNoPendingRetry()
        {
            var source = Source(new[] { Vector3Int.zero, Vector3Int.right, Vector3Int.right * 2 });
            var body = Body(source);
            int notifications = 0;
            body.FragmentCreated += _ => { notifications++; throw new InvalidOperationException("subscriber failure"); };
            Assert.Throws<InvalidOperationException>(() => { body.ApplyDamage(Vector3Int.right, 1000); body.FlushMeshChanges(); });
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(pieces.Length, Is.EqualTo(2));
            Assert.That(pieces.Sum(piece => piece.AliveCount), Is.EqualTo(2));
            foreach (var piece in pieces)
            {
                Assert.That(piece.ColliderCount, Is.GreaterThan(0));
                Assert.That(piece.RenderMeshCount, Is.GreaterThan(0));
                Assert.That(piece.RenderMeshes.All(mesh => mesh), Is.True);
            }
            var meshes = pieces.Select(piece => piece.RenderMeshes.Select(mesh => mesh.GetInstanceID()).OrderBy(id => id).ToArray()).ToArray();
            var revisions = pieces.Select(piece => piece.RenderRevision).ToArray();
            foreach (var piece in pieces) piece.FlushMeshChanges();
            for (int i = 0; i < pieces.Length; i++)
            {
                CollectionAssert.AreEqual(meshes[i], pieces[i].RenderMeshes.Select(mesh => mesh.GetInstanceID()).OrderBy(id => id).ToArray());
                Assert.That(pieces[i].RenderRevision, Is.EqualTo(revisions[i]), "A committed flush must not be replayed after a throwing subscriber.");
            }
        }
    }
}
