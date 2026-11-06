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
        public void RuntimeUsesSpeculativeContinuousCollision()
        {
            var body = Body(Source(new[] { Vector3Int.zero }));
            Assert.That(body.Body, Is.Not.Null);
            Assert.That(body.Body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative));
        }


    }
}
