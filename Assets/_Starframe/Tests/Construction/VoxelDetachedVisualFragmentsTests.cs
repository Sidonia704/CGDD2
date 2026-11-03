using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Maps;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Starframe.Tests
{
    /// <summary>
    /// Isolated EditMode contracts for persistent render-only detached scenery. Every fixture object
    /// lives in a preview scene while an empty saved staging scene stays active, so the open scene,
    /// its default PhysicsScene and its dirty flag are never touched. The staging scene is opened from
    /// a committed empty fixture because the Test Runner starts on an untitled scene that rejects
    /// EditorSceneManager.NewScene(additive). Nothing here calls Physics.Simulate,
    /// Physics.SyncTransforms or a global voxel cache clear, and no asset is saved.
    /// The manager is exercised directly and through real runtime detachments: its public API is the
    /// whole detached-visual contract.
    /// </summary>
    public sealed class VoxelDetachedVisualFragmentsTests
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string StagingScenePath = "Assets/_Starframe/Tests/Construction/Fixtures/TerrainTestStaging.unity";
        readonly List<Object> owned = new List<Object>();
        readonly List<VoxelAssetDefinition> assets = new List<VoxelAssetDefinition>();
        Scene previous, staging, preview;
        GameObject root;
        SimulationMode physicsMode;
        Vector3 gravity;
        float fixedStep;
        int originalSceneCount;
        List<int> originalRoots;
        bool originalDirty;

        [SetUp] public void Setup()
        {
            Assert.That(Application.isPlaying, Is.False, "EditMode fixture only.");
            previous = SceneManager.GetActiveScene();
            originalSceneCount = SceneManager.sceneCount;
            originalRoots = previous.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToList();
            originalDirty = previous.isDirty;
            physicsMode = Physics.simulationMode;
            gravity = Physics.gravity;
            fixedStep = Time.fixedDeltaTime;
            preview = EditorSceneManager.NewPreviewScene();
            try
            {
                Assert.That(System.IO.File.Exists(StagingScenePath), Is.True,
                    "Create the committed empty staging scene fixture at " + StagingScenePath + " before running these tests.");
                // A previous failed setup can leave the staging scene loaded; reuse it instead of failing.
                var loaded = SceneManager.GetSceneByPath(StagingScenePath);
                staging = loaded.IsValid() && loaded.isLoaded
                    ? loaded
                    : EditorSceneManager.OpenScene(StagingScenePath, OpenSceneMode.Additive);
                Assert.That(staging.IsValid() && staging.isLoaded, Is.True);
                Assert.That(SceneManager.SetActiveScene(staging), Is.True);
                Assert.That(preview.GetPhysicsScene().IsValid(), Is.True);
                Assert.That(preview.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene),
                    "Fixture bodies must never live in the default physics scene.");
                root = new GameObject("Detached visual fixture");
                SceneManager.MoveGameObjectToScene(root, preview);
            }
            catch
            {
                // NUnit does not run TearDown when SetUp throws, so never leak the preview/staging scenes.
                if (root) Object.DestroyImmediate(root);
                if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
                if (staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                throw;
            }
        }

        [TearDown] public void Cleanup()
        {
            if (root) Object.DestroyImmediate(root);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            // Assets are destroyed only after their runtimes so the template cache keeps no dead key.
            foreach (var asset in assets) if (asset) Object.DestroyImmediate(asset);
            assets.Clear();
            Assert.That(Physics.simulationMode, Is.EqualTo(physicsMode));
            Assert.That(Physics.gravity, Is.EqualTo(gravity));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(previous.handle));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(originalSceneCount));
            Assert.That(previous.isDirty, Is.EqualTo(originalDirty));
            CollectionAssert.AreEqual(originalRoots,
                previous.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToList());
        }

        GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, preview);
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }

        // A non-planar quad keeps all three bounds extents non-zero for AABB comparisons.
        Mesh Quad(int submeshes = 1)
        {
            var mesh = new Mesh { name = "Detached visual fixture quad" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right * 2, new Vector3(2, 1, 1), Vector3.up };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            var triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.subMeshCount = Mathf.Max(1, submeshes);
            for (int i = 0; i < mesh.subMeshCount; i++) mesh.SetTriangles(triangles, i, false);
            mesh.RecalculateBounds();
            owned.Add(mesh);
            return mesh;
        }

        Material Surface(string name = "Detached source surface")
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project's URP shader must be available to clone scenery materials.");
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(.2f, .4f, .7f, 1));
            owned.Add(material);
            return material;
        }

        VoxelAssetDefinition TintedHullAsset(Material surface, Color tint)
        {
            var asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            assets.Add(asset);
            asset.baseColor = tint;
            asset.surfaces = new[] { surface };
            asset.cells.Add(new VoxelCell { position = Vector3Int.zero, shape = BlockShape.Wedge });
            asset.NotifyChanged();
            return asset;
        }

        VoxelAssetDefinition Asset(IEnumerable<Vector3Int> positions)
        {
            var asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            assets.Add(asset);
            foreach (var position in positions) asset.cells.Add(new VoxelCell { position = position });
            asset.NotifyChanged();
            return asset;
        }

        static IEnumerable<Vector3Int> Box(int x, int y, int z)
        {
            for (int i = 0; i < x; i++) for (int j = 0; j < y; j++) for (int k = 0; k < z; k++)
                yield return new Vector3Int(i, j, k);
        }

        // One-cell bridge at (3,0,0) plus a connected branch; killing the bridge newly detaches the branch.
        static readonly Vector3Int[] Branch =
        {
            new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0),
            new Vector3Int(5, 1, 0), new Vector3Int(4, 0, 1)
        };

        VoxelStructureRuntime Body(VoxelAssetDefinition source, int tiny, float scale, string name, bool preview = false)
        {
            var go = Make(name, root.transform);
            go.transform.localScale = Vector3.one * scale;
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            runtime.tinyFragmentMaxCells = tiny;
            runtime.tinyFragmentMaxWorldExtent = 2f;
            runtime.Initialize(source, preview);
            return runtime;
        }

        static void Add(VoxelDetachedVisualFragments visuals, Mesh mesh, Material material, Matrix4x4 worldPose,
            Vector3 pivot, Vector3 velocity, Vector3 spinRadians, int cells = 1)
            => Add(visuals, mesh, new[] { material }, worldPose, pivot, velocity, spinRadians, cells);

        static void Add(VoxelDetachedVisualFragments visuals, Mesh mesh, Material[] materials, Matrix4x4 worldPose,
            Vector3 pivot, Vector3 velocity, Vector3 spinRadians, int cells = 1)
            => visuals.AddOwnedMesh(mesh, materials, worldPose, pivot, velocity, spinRadians, cells, 0);

        // EditMode never runs LateUpdate: inspect the queued draw list instead of inventing submissions.
        static int DrawCount(VoxelDetachedVisualFragments visuals)
        {
            var field = typeof(VoxelDetachedVisualFragments).GetField("fragments", Flags);
            Assert.That(field, Is.Not.Null);
            int draws = 0;
            foreach (var fragment in (System.Collections.IEnumerable)field.GetValue(visuals))
            {
                var drawsField = fragment.GetType().GetField("draws", Flags);
                Assert.That(drawsField, Is.Not.Null, "Fragment draw storage changed.");
                draws += ((Array)drawsField.GetValue(fragment)).Length;
            }
            return draws;
        }

        static Material[] Clones(VoxelDetachedVisualFragments visuals)
        {
            var field = typeof(VoxelDetachedVisualFragments).GetField("ownedMaterials", Flags);
            Assert.That(field, Is.Not.Null);
            return ((Dictionary<Material, Material>)field.GetValue(visuals)).Values.ToArray();
        }

        // Unity dispatches OnDestroy to a non-ExecuteAlways MonoBehaviour only while playing (and on
        // PlayMode scene unload); EditMode object destruction does not run the callback, even for an
        // enabled component on an active GameObject in an active scene. Production teardown does run
        // it, so these tests invoke the same private cleanup body explicitly before DestroyImmediate
        // and assert the ownership contract itself. Automatic PlayMode dispatch is not exercised here.
        static void InvokeOnDestroy(MonoBehaviour target, Type owner)
        {
            var method = owner.GetMethod("OnDestroy", Flags, null, Type.EmptyTypes, null);
            Assert.That(method, Is.Not.Null, owner.Name + " must keep its OnDestroy cleanup body.");
            method.Invoke(target, null);
        }

        static void AssertMatrix(Matrix4x4 actual, Matrix4x4 expected)
        {
            for (int i = 0; i < 16; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(.0002f), "Matrix component " + i);
        }

        static void AssertVector(Vector3 actual, Vector3 expected, float tolerance = .0003f)
            => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(tolerance),
                "Expected " + expected.ToString("F4") + " but was " + actual.ToString("F4"));

        static Bounds CornerBounds(Bounds local, Matrix4x4 matrix)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(local.min), Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x == 0 ? local.min.x : local.max.x,
                    y == 0 ? local.min.y : local.max.y, z == 0 ? local.min.z : local.max.z)));
            return result;
        }

        static int Roots(Scene scene) => scene.GetRootGameObjects().Length;

        [Test] public void AdmissionCreatesNoGameObjectsPhysicsOrHealthAndSubmitsFromOneManager()
        {
            var owner = Make("Detached visual owner", root.transform);
            var visuals = VoxelDetachedVisualFragments.GetOrCreate(owner.transform);
            Assert.That(owner.GetComponents<Component>().Length, Is.EqualTo(2),
                "The owner needs only its Transform and one render manager.");
            int roots = Roots(preview);

            var material = Surface();
            Add(visuals, Quad(2), new[] { material, material },
                Matrix4x4.TRS(Vector3.one, Quaternion.Euler(0, 30, 0), Vector3.one), Vector3.zero, Vector3.forward, Vector3.up, 3);
            Add(visuals, Quad(), material, Matrix4x4.identity, Vector3.zero, Vector3.right, Vector3.up, 2);
            Add(visuals, Quad(), material, Matrix4x4.identity, Vector3.zero, Vector3.left, Vector3.down, 1);

            Assert.That(Roots(preview), Is.EqualTo(roots), "Fragment admission must never create GameObjects.");
            Assert.That(owner.transform.childCount, Is.Zero);
            Assert.That(owner.GetComponents<Component>().Length, Is.EqualTo(2));
            Assert.That(visuals.transform.childCount, Is.Zero);
            Assert.That(visuals.GetComponentsInChildren<MeshFilter>(true), Is.Empty);
            Assert.That(visuals.GetComponentsInChildren<Renderer>(true), Is.Empty, "No per-fragment MeshRenderer is allowed.");
            Assert.That(visuals.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(visuals.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(visuals.GetComponentsInChildren<VoxelBodyHitbox>(true), Is.Empty);
            Assert.That(visuals.GetComponentsInChildren<VoxelStructureRuntime>(true), Is.Empty,
                "Detached scenery must never become a simulated body with cells or health.");
            Assert.That(visuals.FragmentCount, Is.EqualTo(3));
            Assert.That(visuals.CellCount, Is.EqualTo(6));
            Assert.That(visuals.OwnedMeshCount, Is.EqualTo(3));
            Assert.That(visuals.OwnedMaterialCount, Is.EqualTo(1),
                "One shared clone serves every submesh and every fragment.");

            Assert.That(visuals.SubmittedDrawCount, Is.Zero, "Admission alone must not invent submitted draws.");
            Assert.That(DrawCount(visuals), Is.EqualTo(4),
                "Two submeshes plus two single-submesh fragments queue once each from the one manager.");
            visuals.Clear();
            Assert.That(DrawCount(visuals), Is.Zero);
            Assert.That(visuals.SubmittedDrawCount, Is.Zero);
        }

        [Test] public void BirthPoseIsExactUnderRotatedScaledOwnerAndOffsetPivot()
        {
            var owner = Make("Rotated owner", root.transform);
            owner.transform.SetPositionAndRotation(new Vector3(9, -4, 12), Quaternion.Euler(20, 35, -10));
            owner.transform.localScale = new Vector3(2, 3, .5f);
            var visuals = VoxelDetachedVisualFragments.GetOrCreate(owner.transform);
            var pose = owner.transform.localToWorldMatrix *
                Matrix4x4.TRS(new Vector3(5, -2, 7), Quaternion.Euler(13, 21, 37), new Vector3(1.5f, .75f, 2.25f));
            var mesh = Quad();
            var pivot = new Vector3(1.25f, -.5f, .3f);
            Add(visuals, mesh, Surface(), pose, pivot, new Vector3(.75f, -2, 3), new Vector3(.2f, .3f, .4f), 4);

            AssertMatrix(visuals.GetWorldMatrix(0, 0), pose);
            var expected = CornerBounds(mesh.bounds, pose);
            AssertVector(visuals.GetWorldBounds(0, 0).center, expected.center);
            AssertVector(visuals.GetWorldBounds(0, 0).size, expected.size);
            Assert.That(visuals.FragmentCount, Is.EqualTo(1));
            Assert.That(visuals.CellCount, Is.EqualTo(4));
        }

        [Test] public void SpinIsRadiansPerSecondAndDriftIsAnalyticAcrossFramePartitions()
        {
            var owner = Make("Moving owner", root.transform);
            var visuals = VoxelDetachedVisualFragments.GetOrCreate(owner.transform);
            var pose = Matrix4x4.TRS(new Vector3(10, -3, 5), Quaternion.identity, new Vector3(2, 1, 1.5f));
            var pivot = new Vector3(1, .5f, 0);
            var velocity = new Vector3(3, -1, 2);
            var material = Surface();
            Add(visuals, Quad(), material, pose, pivot, velocity, Vector3.forward * (Mathf.PI * .5f));
            Add(visuals, Quad(), material, pose, pivot, velocity, Vector3.forward * (Mathf.PI * .5f));

            // One second at pi/2 rad/s is exactly a quarter turn, world-space, about the fragment pivot.
            var after = visuals.GetWorldMatrix(0, 1);
            var movedPivot = pose.MultiplyPoint3x4(pivot) + velocity;
            AssertVector(after.MultiplyPoint3x4(pivot), movedPivot);
            AssertVector(after.MultiplyPoint3x4(pivot + Vector3.right), movedPivot + Vector3.up * 2);
            AssertVector(after.MultiplyPoint3x4(pivot + Vector3.up), movedPivot + Vector3.left);

            // Frame subdivision must not change the analytic pose or leak extra fragments.
            var direct = visuals.GetWorldMatrix(1, 3.5);
            double elapsed = 0;
            foreach (double delta in new[] { .5, .25, .75, 1, .5, .5 })
            {
                elapsed += delta;
                visuals.GetWorldMatrix(1, elapsed);
            }
            Assert.That(elapsed, Is.EqualTo(3.5));
            AssertMatrix(visuals.GetWorldMatrix(1, elapsed), direct);
            Assert.That(visuals.FragmentCount, Is.EqualTo(2));
            Assert.That(visuals.CellCount, Is.EqualTo(2));
        }

        [Test] public void OwnerDeltaTranslatesRotatesAndScalesFragmentAndWorldBounds()
        {
            var owner = Make("Animated owner", root.transform);
            owner.transform.SetPositionAndRotation(new Vector3(2, 4, -1), Quaternion.Euler(11, 37, 19));
            owner.transform.localScale = new Vector3(2, .75f, 1.5f);
            var ownerAtBirth = owner.transform.localToWorldMatrix;
            var visuals = VoxelDetachedVisualFragments.GetOrCreate(owner.transform);
            var pose = ownerAtBirth * Matrix4x4.TRS(new Vector3(4, -1, 3), Quaternion.Euler(21, 9, 47), Vector3.one * .8f);
            var mesh = Quad();
            var pivot = new Vector3(1, .5f, 0);
            var worldPivot = pose.MultiplyPoint3x4(pivot);
            var velocity = new Vector3(.25f, -1, 2);
            Add(visuals, mesh, Surface(), pose, pivot, velocity, Vector3.up * (Mathf.PI * .5f));

            owner.transform.SetPositionAndRotation(new Vector3(-7, 6, 3), Quaternion.Euler(32, -15, 41));
            owner.transform.localScale = new Vector3(.5f, 2, 3);
            var motion = Matrix4x4.Translate(worldPivot + velocity * 2) *
                Matrix4x4.Rotate(Quaternion.AngleAxis(180, Vector3.up)) * Matrix4x4.Translate(-worldPivot);
            var expected = owner.transform.localToWorldMatrix * ownerAtBirth.inverse * motion * pose;

            AssertMatrix(visuals.GetWorldMatrix(0, 2), expected);
            var expectedBounds = CornerBounds(mesh.bounds, expected);
            AssertVector(visuals.GetWorldBounds(0, 2).center, expectedBounds.center);
            AssertVector(visuals.GetWorldBounds(0, 2).size, expectedBounds.size);
            Assert.That(visuals.FragmentCount, Is.EqualTo(1), "Changing the owner pose must never drop a fragment.");
        }

        [Test] public void ElapsedAgeAndDistanceNeverDeleteOrFadeFragments()
        {
            var owner = Make("Long lived owner", root.transform);
            var visuals = VoxelDetachedVisualFragments.GetOrCreate(owner.transform);
            var pose = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(5, 15, 25), Vector3.one);
            var pivot = new Vector3(.5f, .25f, -.25f);
            var velocity = new Vector3(.5f, 0, -.25f);
            Add(visuals, Quad(), Surface(), pose, pivot, velocity, new Vector3(0, .25f, 0), 2);
            var origin = visuals.GetWorldMatrix(0, 0).MultiplyPoint3x4(pivot);

            foreach (double elapsed in new[] { 86400d, 864000d })
            {
                var far = visuals.GetWorldMatrix(0, elapsed).MultiplyPoint3x4(pivot);
                AssertVector(far, origin + velocity * (float)elapsed, .05f);
                Assert.That(visuals.FragmentCount, Is.EqualTo(1), "Elapsed time must not expire persistent scenery.");
                Assert.That(visuals.CellCount, Is.EqualTo(2));
                Assert.That(visuals.GetWorldBounds(0, elapsed).size.sqrMagnitude, Is.GreaterThan(0f),
                    "A far fragment keeps non-degenerate world bounds.");
            }

            // Repeated frames are pure submission: no aged-out fragment, no distance culling.
            for (int frame = 0; frame < 8; frame++) visuals.GetWorldMatrix(0, 864000 + frame);
            Assert.That(DrawCount(visuals), Is.EqualTo(1));
            Assert.That(visuals.FragmentCount, Is.EqualTo(1));
            Assert.That(visuals.OwnedMeshCount, Is.EqualTo(1));

            Assert.Throws<ArgumentOutOfRangeException>(() => visuals.GetWorldMatrix(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => visuals.GetWorldMatrix(0, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => visuals.GetWorldMatrix(0, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => visuals.GetWorldMatrix(7, 0));
        }

        [Test] public void MaterialClonesSurviveSourceRuntimeTintDisposal()
        {
            var map = root.AddComponent<SpaceMapRuntime>();
            var source = Make("Tinted hull source", root.transform).AddComponent<VoxelStructureRuntime>();
            var surface = Surface();
            var tint = new Color(.65f, .8f, .95f);
            var asset = TintedHullAsset(surface, tint);
            source.Initialize(asset);
            var visuals = VoxelDetachedVisualFragments.GetOrCreateFor(source);
            Assert.That(visuals.gameObject, Is.SameAs(map.gameObject));

            var renderer = source.GetComponent<MeshRenderer>();
            var tinted = renderer.sharedMaterials;
            Assert.That(tinted.Length, Is.EqualTo(VoxelHullAppearance.ShapeCount));
            var sourceTint = tinted[VoxelHullAppearance.ShapeIndex(BlockShape.Wedge)];
            Assert.That(sourceTint, Is.Not.SameAs(surface), "A colour channel must own a tinted clone.");
            var expected = surface.GetColor("_BaseColor") * tint;
            Assert.That(Vector4.Distance(sourceTint.GetColor("_BaseColor"), expected), Is.LessThan(.00001f));

            // The detached mesh mirrors what TryCreateVisualFragment bakes: hull geometry for the wedge.
            var cells = new Dictionary<Vector3Int, VoxelCellState>
            {
                { Vector3Int.zero, new VoxelCellState { position = Vector3Int.zero, shape = BlockShape.Wedge } }
            };
            var mesh = VoxelGeometry.Build(cells, tinted.Length, asset.cellSize, asset.hullModules);
            owned.Add(mesh);
            Assert.That(mesh.subMeshCount, Is.EqualTo(tinted.Length));
            var pose = Matrix4x4.TRS(new Vector3(3, 2, 1), Quaternion.Euler(0, 40, 0), Vector3.one);
            Add(visuals, mesh, tinted, pose, Vector3.zero, Vector3.zero, Vector3.up * 1.3f, 1);

            var clone = Clones(visuals).Single();
            Assert.That(clone, Is.Not.SameAs(sourceTint));
            Assert.That(visuals.OwnedMaterialCount, Is.EqualTo(1), "Only the non-empty wedge submesh needs a clone.");
            Assert.That(visuals.OwnedMeshCount, Is.EqualTo(1));
            Assert.That(visuals.FragmentCount, Is.EqualTo(1));
            Assert.That(visuals.CellCount, Is.EqualTo(1));
            AssertMatrix(visuals.GetWorldMatrix(0, 0), pose);

            InvokeOnDestroy(source, typeof(VoxelStructureRuntime));
            Assert.That(sourceTint == null, Is.True,
                "The source runtime's own teardown body disposes its tinted materials.");
            Object.DestroyImmediate(source.gameObject);
            Assert.That(clone != null && mesh != null, Is.True,
                "The manager's clone and mesh are independent of the destroyed source runtime.");
            Assert.That(clone.shader, Is.SameAs(surface.shader));
            Assert.That(Vector4.Distance(clone.GetColor("_BaseColor"), expected), Is.LessThan(.00001f),
                "The clone must keep the exact source tint after the source is gone.");
            Assert.That(DrawCount(visuals), Is.EqualTo(1),
                "The fragment keeps a queued draw after the source tint is disposed.");
        }

        [Test] public void ManagerCountsAreManagerWideAcrossIndependentSources()
        {
            var map = root.AddComponent<SpaceMapRuntime>();
            var first = Make("Source one", root.transform).AddComponent<VoxelStructureRuntime>();
            var second = Make("Source two", root.transform).AddComponent<VoxelStructureRuntime>();
            var visuals = VoxelDetachedVisualFragments.GetOrCreateFor(first);
            Assert.That(VoxelDetachedVisualFragments.GetOrCreateFor(second), Is.SameAs(visuals));
            Assert.That(visuals.gameObject, Is.SameAs(map.gameObject));

            var materialA = Surface("Source one surface");
            var materialB = Surface("Source two surface");
            Add(visuals, Quad(), materialA, Matrix4x4.identity, Vector3.zero, Vector3.right, Vector3.up, 3);
            Add(visuals, Quad(), materialB, Matrix4x4.TRS(Vector3.right * 2, Quaternion.identity, Vector3.one),
                Vector3.zero, Vector3.left, Vector3.up, 1);

            Assert.That(visuals.CellCount, Is.EqualTo(4),
                "One manager-wide cell tally, never a per-runtime tally.");
            Assert.That(visuals.FragmentCount, Is.EqualTo(2));
            Assert.That(visuals.OwnedMeshCount, Is.EqualTo(2));
            Assert.That(visuals.OwnedMaterialCount, Is.EqualTo(2),
                "Distinct source materials get distinct clones, so one source cannot tint another.");

            // Destroying one originating body must not change the manager-wide totals.
            Object.DestroyImmediate(first.gameObject);
            Assert.That(visuals.CellCount, Is.EqualTo(4));
            Assert.That(visuals.FragmentCount, Is.EqualTo(2));
            Assert.That(visuals.OwnedMaterialCount, Is.EqualTo(2));
            Assert.That(visuals.GetWorldMatrix(0, 5).MultiplyPoint3x4(Vector3.zero),
                Is.EqualTo(new Vector3(5, 0, 0)));

            map.Clear();
            Assert.That(visuals.CellCount, Is.Zero);
            Assert.That(visuals.FragmentCount, Is.Zero);
            Assert.That(visuals.OwnedMeshCount, Is.Zero);
            Assert.That(visuals.OwnedMaterialCount, Is.Zero);
        }

        [TestCase(3, 1f, 4, true)]
        [TestCase(4, 1f, 4, true)]
        [TestCase(5, 1f, 4, false)]
        [TestCase(1, 2f, 4, true)]
        [TestCase(1, 2.01f, 4, false)]
        [TestCase(1, 1f, 0, false)]
        public void TinyPolicyAdmitsOnlyNewlyDetachedIslandsWithinCellAndWorldLimits(
            int detachedCells, float scale, int tiny, bool renderOnly)
        {
            var positions = Box(3, 3, 3).ToList();
            positions.Add(new Vector3Int(3, 0, 0));
            positions.AddRange(Branch.Take(detachedCells));
            var source = Asset(positions);
            var body = Body(source, tiny, scale, "Policy source body");
            var birth = body.PhysicsMatrix;
            int cells = source.cells.Count;

            Assert.That(body.DetachedVisualCells, Is.Zero, "An attached tendril is never render-only.");
            Assert.That(root.GetComponent<VoxelDetachedVisualFragments>(), Is.Null,
                "No render-only manager may exist before a split.");

            body.ApplyDamage(new Vector3Int(3, 0, 0), 1000); body.FlushMeshChanges();
            var visuals = root.GetComponent<VoxelDetachedVisualFragments>();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            if (renderOnly)
            {
                Assert.That(visuals, Is.Not.Null, "A newly detached small island within both limits must be render-only.");
                Assert.That(visuals.FragmentCount, Is.EqualTo(1));
                Assert.That(visuals.CellCount, Is.EqualTo(detachedCells),
                    "The manager counts exactly the newly detached cells, never an older island.");
                Assert.That(body.DetachedVisualCells, Is.EqualTo(detachedCells));
                Assert.That(body.DetachedVisualFragmentCount, Is.EqualTo(1));
                Assert.That(body.AliveCount, Is.EqualTo(27), "The retained largest group stays fully physical.");
                Assert.That(pieces.Single(), Is.SameAs(body), "Render-only fragments never become a second runtime.");
                Assert.That(body.Body, Is.Not.Null,
                    "Non-preview EditMode scenery keeps its physical Rigidbody; this fixture never steps physics.");
                Assert.That(body.Body.useGravity, Is.False);
                Assert.That(body.ColliderCount, Is.GreaterThan(0), "The retained island keeps enabled physical collision.");
                Assert.That(body.AliveCount + visuals.CellCount, Is.EqualTo(cells - 1),
                    "Source plus fragment conservation: no cell is lost or duplicated.");
                AssertMatrix(visuals.GetWorldMatrix(0, 0), birth);
                Object.DestroyImmediate(body.gameObject);
                Assert.That(visuals.CellCount, Is.EqualTo(detachedCells));
                Assert.That(visuals.FragmentCount, Is.EqualTo(1));
                AssertMatrix(visuals.GetWorldMatrix(0, 0), birth);
            }
            else
            {
                Assert.That(visuals, Is.Null, "A rejected island must never get a render-only manager.");
                Assert.That(body.DetachedVisualCells, Is.Zero);
                Assert.That(body.DetachedVisualFragmentCount, Is.Zero);
                Assert.That(body.AliveCount, Is.EqualTo(27));
                Assert.That(pieces.Length, Is.EqualTo(2), "A rejected island stays a fully physical runtime.");
                Assert.That(pieces.Sum(p => p.AliveCount), Is.EqualTo(cells - 1));
                Assert.That(pieces.All(p => p.Cells.Values.Any(c => c.Alive)), Is.True);
            }
        }

        [Test] public void PreviewOnlyRuntimeNeverAdmitsRenderOnlyFragments()
        {
            var positions = Box(3, 3, 3).ToList();
            positions.Add(new Vector3Int(3, 0, 0)); positions.Add(new Vector3Int(4, 0, 0));
            var source = Asset(positions);
            var body = Body(source, 4, 1f, "Preview source body", true);
            Assert.That(body.DataOnlyPreview, Is.False);

            body.ApplyDamage(new Vector3Int(3, 0, 0), 1000); body.FlushMeshChanges();
            Assert.That(body.DetachedVisualCells, Is.Zero);
            Assert.That(body.DetachedVisualFragmentCount, Is.Zero);
            Assert.That(root.GetComponent<VoxelDetachedVisualFragments>(), Is.Null,
                "previewOnly is an explicit prohibition on render-only admission.");
            Assert.That(root.GetComponentsInChildren<VoxelStructureRuntime>(true).Length, Is.EqualTo(1),
                "Preview splits stay inside the one preview runtime.");
            Assert.That(body.AliveCount, Is.EqualTo(28), "Both preview islands keep every surviving cell.");
        }

        [Test] public void AuthoredDisconnectedIslandStaysPhysicalEvenWhenItWouldFitTheTinyPolicy()
        {
            // The asset already contains a two-cell island; only an island newly disconnected by this
            // deletion may become render-only, so the authored island must stay a physical runtime.
            var positions = Box(3, 3, 3).ToList();
            positions.Add(new Vector3Int(5, 0, 0)); positions.Add(new Vector3Int(6, 0, 0));
            var source = Asset(positions);
            var body = Body(source, 4, 1f, "Authored island source");
            Assert.That(root.GetComponent<VoxelDetachedVisualFragments>(), Is.Null,
                "Initialize never retroactively converts an island.");

            body.ApplyDamage(new Vector3Int(1, 1, 1), 1000); body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            Assert.That(root.GetComponent<VoxelDetachedVisualFragments>(), Is.Null,
                "A previously authored island is never admitted as render-only.");
            Assert.That(body.DetachedVisualCells, Is.Zero);
            Assert.That(body.DetachedVisualFragmentCount, Is.Zero);
            Assert.That(pieces.Length, Is.EqualTo(2), "The authored island stays a physical runtime.");
            Assert.That(pieces.Sum(p => p.AliveCount), Is.EqualTo(source.cells.Count - 1));
            Assert.That(pieces.Where(p => p != body).SelectMany(p => p.Cells.Values).Count(c => c.Alive),
                Is.EqualTo(2), "The authored two-cell island keeps physical cells.");
        }

        [Test] public void ManagerTalliesStayManagerWideAndConserveCellsAcrossSourceBodies()
        {
            var firstSource = Asset(Box(3, 3, 3).Concat(new[]
            {
                new Vector3Int(3, 0, 0), new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0)
            }));
            var secondSource = Asset(Box(3, 3, 3).Concat(new[]
            {
                new Vector3Int(3, 0, 0), new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0)
            }));
            var first = Body(firstSource, 4, 1f, "Manager-wide source A");
            var second = Body(secondSource, 4, 1f, "Manager-wide source B");
            Assert.That(root.GetComponent<VoxelDetachedVisualFragments>(), Is.Null);

            first.ApplyDamage(new Vector3Int(3, 0, 0), 1000); first.FlushMeshChanges();
            var visuals = root.GetComponent<VoxelDetachedVisualFragments>();
            Assert.That(visuals, Is.Not.Null);
            Assert.That(visuals.CellCount, Is.EqualTo(3));
            second.ApplyDamage(new Vector3Int(3, 0, 0), 1000); second.FlushMeshChanges();

            Assert.That(visuals.FragmentCount, Is.EqualTo(2), "Both sources share one manager-wide fragment tally.");
            Assert.That(visuals.CellCount, Is.EqualTo(5),
                "Manager-wide cells are 3 + 2, never one runtime's local counter alone.");
            Assert.That(first.DetachedVisualCells, Is.EqualTo(3));
            Assert.That(second.DetachedVisualCells, Is.EqualTo(2));
            Assert.That(first.AliveCount + second.AliveCount + visuals.CellCount, Is.EqualTo(30 + 29),
                "Source plus fragment conservation holds across every source in the manager.");

            Object.DestroyImmediate(first.gameObject);
            Assert.That(visuals.CellCount, Is.EqualTo(5), "Destroying one source cannot change manager-wide totals.");
            Assert.That(visuals.FragmentCount, Is.EqualTo(2));
            visuals.Clear();
            Assert.That(visuals.CellCount, Is.Zero);
            Assert.That(visuals.FragmentCount, Is.Zero);
        }

        [Test] public void MapClearReleasesOwnedResourcesOnceAndKeepsBorrowedMaterials()
        {
            var map = root.AddComponent<SpaceMapRuntime>();
            var source = Make("Map source body", root.transform).AddComponent<VoxelStructureRuntime>();
            var visuals = VoxelDetachedVisualFragments.GetOrCreateFor(source);
            Assert.That(visuals.gameObject, Is.SameAs(map.gameObject));

            var material = Surface();
            var mesh = Quad();
            Add(visuals, mesh, material, Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero, 4);
            var clone = Clones(visuals).Single();

            map.Clear();
            Assert.That(visuals.FragmentCount, Is.Zero);
            Assert.That(visuals.CellCount, Is.Zero);
            Assert.That(visuals.OwnedMeshCount, Is.Zero);
            Assert.That(visuals.OwnedMaterialCount, Is.Zero);
            Assert.That(visuals.SubmittedDrawCount, Is.Zero);
            Assert.That(mesh == null && clone == null, Is.True, "Owned geometry and clones are released exactly once.");
            Assert.That(material != null, Is.True, "A borrowed source material must never be destroyed.");
            Assert.DoesNotThrow(map.Clear, "Clearing an already released map is idempotent.");

            var nextMesh = Quad();
            Add(visuals, nextMesh, material, Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero);
            var nextClone = Clones(visuals).Single();
            Assert.That(visuals.FragmentCount, Is.EqualTo(1), "A cleared manager stays reusable.");

            InvokeOnDestroy(visuals, typeof(VoxelDetachedVisualFragments));
            Object.DestroyImmediate(map.gameObject);
            Assert.That(nextMesh == null && nextClone == null, Is.True,
                "The owner's teardown body releases whatever it still owns.");
            Assert.That(material != null, Is.True);
        }

        [Test] public void RootSourcesShareOneStableSceneOwnerWithoutPerFragmentObjects()
        {
            var first = Make("Standalone source one").AddComponent<VoxelStructureRuntime>();
            var second = Make("Standalone source two").AddComponent<VoxelStructureRuntime>();
            var visuals = VoxelDetachedVisualFragments.GetOrCreateFor(first);
            Assert.That(VoxelDetachedVisualFragments.GetOrCreateFor(second), Is.SameAs(visuals));
            Assert.That(visuals.gameObject.scene.handle, Is.EqualTo(preview.handle));
            Assert.That(visuals.transform.parent, Is.Null);
            Assert.That(visuals.gameObject, Is.Not.SameAs(first.gameObject));
            Assert.That((visuals.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0, Is.True,
                "The synthetic owner is transient and must never be saved as an asset.");
            Assert.That((visuals.gameObject.hideFlags & HideFlags.DontSaveInBuild) != 0, Is.True);
            Assert.That(visuals.GetComponents<Component>().Length, Is.EqualTo(2));
            int roots = Roots(preview);

            var material = Surface();
            for (int i = 0; i < 5; i++)
                Add(visuals, Quad(), material, Matrix4x4.Translate(Vector3.right * i), Vector3.zero, Vector3.forward, Vector3.up);

            Assert.That(Roots(preview), Is.EqualTo(roots),
                "Admission must never spawn a GameObject, component or physics body per fragment.");
            Assert.That(visuals.transform.childCount, Is.Zero);
            Assert.That(visuals.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(visuals.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(visuals.GetComponentsInChildren<VoxelBodyHitbox>(true), Is.Empty);
            Assert.That(visuals.GetComponentsInChildren<VoxelStructureRuntime>(true), Is.Empty);
            Assert.That(visuals.FragmentCount, Is.EqualTo(5));
            Assert.That(visuals.CellCount, Is.EqualTo(5));
            Assert.That(DrawCount(visuals), Is.EqualTo(5));

            Object.DestroyImmediate(first.gameObject);
            Object.DestroyImmediate(second.gameObject);
            Assert.That(visuals != null, Is.True, "The scene owner must outlive both source bodies.");
            Assert.That(visuals.FragmentCount, Is.EqualTo(5));
            Assert.That(visuals.CellCount, Is.EqualTo(5));
            Assert.That(visuals.GetWorldMatrix(0, 10).MultiplyPoint3x4(Vector3.zero),
                Is.EqualTo(new Vector3(0, 0, 10)), "Detached motion stays analytic after its source dies.");

            var third = Make("Standalone source three").AddComponent<VoxelStructureRuntime>();
            Assert.That(VoxelDetachedVisualFragments.GetOrCreateFor(third), Is.SameAs(visuals),
                "Later sources in the same scene reuse the existing scene owner.");
            Object.DestroyImmediate(third.gameObject);
        }

        [Test] public void InvalidAdmissionIsRejectedWithoutChangingTalliesOrClones()
        {
            var owner = Make("Validation owner", root.transform);
            var visuals = VoxelDetachedVisualFragments.GetOrCreate(owner.transform);
            var material = Surface();
            Add(visuals, Quad(), material, Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero, 2);
            var mesh = Quad();

            Assert.Throws<ArgumentException>(() => visuals.AddOwnedMesh(mesh, Array.Empty<Material>(),
                Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => visuals.AddOwnedMesh(mesh, new[] { material },
                Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => visuals.AddOwnedMesh(mesh, new[] { material },
                Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.zero, 1, 32));
            Assert.Throws<ArgumentException>(() => visuals.AddOwnedMesh(mesh, new[] { material },
                Matrix4x4.identity, Vector3.zero, new Vector3(float.NaN, 0, 0), Vector3.zero, 1, 0));

            Assert.That(visuals.CellCount, Is.EqualTo(2), "A rejected fragment must not change the cell tally.");
            Assert.That(visuals.FragmentCount, Is.EqualTo(1));
            Assert.That(visuals.OwnedMeshCount, Is.EqualTo(1));
            Assert.That(visuals.OwnedMaterialCount, Is.EqualTo(1));
        }
    }
}
