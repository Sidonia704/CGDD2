using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Starframe.Tests
{
    public sealed class SpaceMapRuntimeScaleTests
    {
        Scene preview;
        GameObject root;
        VoxelAssetDefinition asset;
        SpaceMapDefinition map;
        Camera camera;
        Mesh coarse;
        VoxelAssetDefinition extraAsset;

        [SetUp] public void Setup()
        {
            preview = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Map scale fixture");
            SceneManager.MoveGameObjectToScene(root, preview);
            var cameraRoot = new GameObject("Map scale camera");
            SceneManager.MoveGameObjectToScene(cameraRoot, preview);
            camera = cameraRoot.AddComponent<Camera>();
            camera.transform.position = Vector3.back * 50;
            asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            asset.cells.Add(new VoxelCell { position = Vector3Int.zero });
            asset.cells.Add(new VoxelCell { position = Vector3Int.right });
            map = ScriptableObject.CreateInstance<SpaceMapDefinition>();
        }
        [TearDown] public void Cleanup()
        {
            if (root)
            {
                var runtime = root.GetComponent<SpaceMapRuntime>();
                if (runtime) runtime.Clear();
            }
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (coarse) Object.DestroyImmediate(coarse);
            if (map) Object.DestroyImmediate(map);
            if (asset) Object.DestroyImmediate(asset);
            if (extraAsset) Object.DestroyImmediate(extraAsset);
        }

        [Test] public void StagedBuildCanCompleteOrCancelWithoutLeavingPartialPhysics()
        {
            for (int i = 0; i < 3; i++) map.instances.Add(new VoxelMapInstance
            {
                asset = asset, position = Vector3.right * i * 10,
                driftVelocity = Vector3.up * (i + 1), angularVelocity = Vector3.forward * 3
            });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map;
            runtime.BeginBuild(1);
            Assert.That(runtime.BuildIsPending, Is.True);
            Assert.That(runtime.StepBuild(1), Is.EqualTo(1));
            Assert.That(runtime.BuiltInstanceCount, Is.EqualTo(1));
            Assert.That(runtime.Structures[0].Body, Is.Not.Null);
            Assert.That(runtime.Structures[0].Body.useGravity, Is.False);
            Assert.That(runtime.Structures[0].ColliderCount, Is.GreaterThan(0));
            runtime.CancelBuild();
            Assert.That(runtime.BuildIsPending || runtime.BuildIsComplete, Is.False);
            Assert.That(runtime.Structures.Count, Is.Zero);
            runtime.BeginBuild(2);
            Assert.That(runtime.StepBuild(2), Is.EqualTo(2));
            Assert.That(runtime.StepBuild(2), Is.EqualTo(1));
            Assert.That(runtime.BuildIsComplete, Is.True);
            Assert.That(runtime.Structures.Count, Is.EqualTo(3));
            Assert.That(runtime.Structures[2].Body.linearVelocity, Is.EqualTo(Vector3.up * 3));
        }



        void MakeHollowCollisionAsset()
        {
            asset.cells.Clear();
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                if (x != 1 || y != 1)
                    asset.cells.Add(new VoxelCell { position = new Vector3Int(x, y, 0) });
            asset.NotifyChanged();
        }

        static int EnabledBoxCount(VoxelStructureRuntime body)
        {
            int count = 0;
            foreach (var box in body.GetComponents<BoxCollider>()) if (box.enabled) count++;
            return count;
        }

        [Test] public void BudgetedBoundsProxyKeepsDriftAndDoesNotTurnEmptySpaceIntoProjectileDamage()
        {
            MakeHollowCollisionAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset, driftVelocity = Vector3.right * 2 });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map;
            runtime.useBudgetedPhysics = true; runtime.activeColliderBudget = 1;
            runtime.Build();
            var body = runtime.Structures[0];
            Assert.That(body.UsesBoundsCollisionProxy, Is.True);
            Assert.That(body.MaterializedColliderCount, Is.Zero);
            Assert.That(body.ColliderCount, Is.GreaterThan(1));
            Assert.That(body.GetComponents<BoxCollider>().Length, Is.EqualTo(1));
            Assert.That(EnabledBoxCount(body), Is.EqualTo(1));
            Assert.That(runtime.ActiveMapColliderCount, Is.EqualTo(1));
            Assert.That(body.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("BudgetedScenery")));
            Assert.That(Physics.GetIgnoreLayerCollision(body.gameObject.layer, body.gameObject.layer), Is.True);
            Assert.That(Physics.GetIgnoreLayerCollision(body.gameObject.layer, 0), Is.False,
                "The dedicated map layer must still collide with the player and other external bodies.");
            Assert.That(body.Body.isKinematic, Is.False);
            Assert.That(body.Body.linearVelocity, Is.EqualTo(Vector3.right * 2));
            Assert.That(body.Body.useGravity, Is.False);
            BoxCollider proxy = null;
            foreach (var box in body.GetComponents<BoxCollider>()) if (box.enabled) proxy = box;
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.center, Is.EqualTo(body.LocalBounds.center));
            Assert.That(proxy.size, Is.EqualTo(body.LocalBounds.size));
            var hits = new List<VoxelContact>();
            var geometry = new CylindricalHitQuery();
            var hitbox = body.GetComponent<VoxelBodyHitbox>();
            hitbox.CollectHits(new Vector3(1, 1, -5), Vector3.forward, .05f, 0, 10, geometry, hits);
            Assert.That(hits, Is.Empty, "The moving bounds proxy may be broadphase only; exact cell DDA must reject its hollow center.");
            hitbox.CollectHits(new Vector3(0, 0, -5), Vector3.forward, .05f, 0, 10, geometry, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].cell.position, Is.EqualTo(Vector3Int.zero));
        }





        [Test] public void MovingMapPromotionStaysWithinThePerFrameCreationBudgetWithoutLosingCollision()
        {
            MakeHollowCollisionAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map;
            runtime.useBudgetedPhysics = true; runtime.activeColliderBudget = 1; runtime.visualCamera = camera;
            runtime.colliderPreparationPerFrame = 2; runtime.colliderPreparationMilliseconds = 0; runtime.Build();
            var body = runtime.Structures[0];
            runtime.activeColliderBudget = body.DetailedColliderCost;
            runtime.RefreshPhysicsNow(camera, true);
            for (int frame = 0; frame < 20 && body.UsesBoundsCollisionProxy; frame++)
            {
                int before = body.MaterializedColliderCount;
                typeof(SpaceMapRuntime).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(runtime, null);
                Assert.That(body.MaterializedColliderCount - before, Is.LessThanOrEqualTo(2));
                Assert.That(EnabledBoxCount(body), Is.GreaterThan(0), "A staged promotion must keep physical contact live throughout.");
            }
            Assert.That(body.UsesBoundsCollisionProxy, Is.False);
            Assert.That(body.MaterializedColliderCount, Is.EqualTo(body.DetailedColliderCost));
        }





        VoxelAssemblyLink Link(string a, string b, Vector3Int cellA, Vector3Int cellB, bool fixedJoint = true)
        {
            return new VoxelAssemblyLink { instanceIdA = a, instanceIdB = b,
                anchorCellA = cellA, anchorCellB = cellB, worldAnchor = Vector3.zero,
                createFixedJoint = fixedJoint, ignoreMutualCollision = true };
        }
        [Test] public void StagedStationBodiesStayFrozenUntilJointExistsAndCancelRemovesPartialBodies()
        {
            var a = new VoxelMapInstance { asset = asset, driftVelocity = Vector3.right * 2 };
            var b = new VoxelMapInstance { asset = asset, driftVelocity = Vector3.left * 2 };
            map.instances.Add(a); map.instances.Add(b);
            map.assemblyLinks.Add(Link(a.id, b.id, Vector3Int.zero, Vector3Int.zero));
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map;
            runtime.BeginBuild(1);
            runtime.StepBuild(1);
            Assert.That(runtime.Structures[0].Body.isKinematic, Is.True);
            runtime.CancelBuild();
            Assert.That(runtime.Structures.Count, Is.Zero);
            runtime.BeginBuild(1);
            runtime.StepBuild(1);
            Assert.That(runtime.Structures[0].Body.isKinematic, Is.True);
            runtime.StepBuild(1);
            Assert.That(runtime.BuildIsComplete, Is.True);
            Assert.That(runtime.ActiveAssemblyContactCount, Is.EqualTo(1));
            Assert.That(runtime.Structures[0].Body.isKinematic, Is.False);
            Assert.That(runtime.Structures[1].Body.isKinematic, Is.False);
            Assert.That(runtime.Structures[0].Body.linearVelocity, Is.EqualTo(runtime.Structures[1].Body.linearVelocity));
            var joint = runtime.Structures[0].GetComponent<FixedJoint>();
            Assert.That(joint, Is.Not.Null);
            Assert.That(joint.connectedBody, Is.SameAs(runtime.Structures[1].Body));
            Assert.That(joint.enableCollision, Is.False);
            Assert.That(runtime.Structures[0].Body.detectCollisions && runtime.Structures[1].Body.detectCollisions, Is.True);
        }





        [Test] public void FreeCollisionFilterEndsWhenQuantizedCellsSeparate()
        {
            var a = new VoxelMapInstance { asset = asset };
            var b = new VoxelMapInstance { asset = asset };
            map.instances.Add(a); map.instances.Add(b);
            map.assemblyLinks.Add(Link(a.id, b.id, Vector3Int.zero, Vector3Int.zero, false));
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.Build();
            var filter = runtime.Structures[0].GetComponent<ConfigurableJoint>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.enableCollision, Is.False);
            Assert.That(filter.xMotion, Is.EqualTo(ConfigurableJointMotion.Free));
            runtime.Structures[1].Body.position = Vector3.right * 20;
            runtime.RefreshAssemblyContactsNow();
            Assert.That(runtime.ActiveAssemblyContactCount, Is.Zero);
            Assert.That(runtime.Structures[0].Body.detectCollisions && runtime.Structures[1].Body.detectCollisions, Is.True);
        }

        Mesh MakeCoarseMesh()
        {
            var cells = new Dictionary<Vector3Int, VoxelCellState>
            {
                { Vector3Int.zero, new VoxelCellState { position = Vector3Int.zero } }
            };
            return VoxelGeometry.Build(cells, 1, asset.cellSize);
        }

        void MakeLazyHullAsset()
        {
            asset.hullModules = AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            Assert.That(asset.hullModules, Is.Not.Null);
            asset.cells.Clear();
            for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                asset.cells.Add(new VoxelCell { position = new Vector3Int(x, y, z) });
            asset.cells.Add(new VoxelCell { position = new Vector3Int(8, 0, 0) });
            asset.surfaceColors[0] = new Color(.4f, .7f, .9f);
            var proxyCell = new Dictionary<Vector3Int, VoxelCellState>
            {
                { Vector3Int.zero, new VoxelCellState { position = Vector3Int.zero } }
            };
            coarse = VoxelGeometry.Build(proxyCell, 8, asset.cellSize * 8, asset.hullModules);
            asset.initialLodMesh = coarse;
            asset.NotifyChanged();
            asset.initialLodSignature = SpaceMapRuntime.ComputeGeometrySignature(asset);
        }









        [Test] public void CoarseIntactModeDoesNotPromoteNearSceneryUntilExplicitlyDisabled()
        {
            MakeLazyHullAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map;
            runtime.lazyFineVisuals = true; runtime.keepCoarseIntactVisuals = true;
            runtime.visualLodDistance = 100;
            runtime.Build();
            var body = runtime.Structures[0];
            runtime.RefreshVisualsNow(camera);
            Assert.That(body.HasLazyInitialVisual, Is.True);
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(coarse));
            Assert.That(body.AliveCount, Is.EqualTo(513));
            Assert.That(body.ColliderCount, Is.GreaterThan(0));
            runtime.keepCoarseIntactVisuals = false;
            runtime.RefreshVisualsNow(camera);
            Assert.That(body.HasLazyInitialVisual, Is.False);
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs(coarse));
            Assert.That(body.AliveCount, Is.EqualTo(513));
        }

        [Test] public void DistinctLazyAssetsShareFrozenModelGenerationUntilLastOwnerReleasesIt()
        {
            MakeLazyHullAsset();
            extraAsset = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            extraAsset.hullModules = asset.hullModules;
            extraAsset.cells.Add(new VoxelCell { position = Vector3Int.zero });
            extraAsset.initialLodMesh = coarse;
            extraAsset.initialLodSignature = SpaceMapRuntime.ComputeGeometrySignature(extraAsset);
            var first = VoxelTemplateCache.Acquire(asset, true);
            var second = VoxelTemplateCache.Acquire(extraAsset, true);
            try
            {
                var generation = typeof(VoxelTemplateCache.Template).GetField("modelGeneration", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(generation, Is.Not.Null);
                Assert.That(generation.GetValue(first), Is.SameAs(generation.GetValue(second)));
                VoxelTemplateCache.Release(first); first = null;
                Assert.That(VoxelTemplateCache.EnsureExactMesh(second).vertexCount, Is.GreaterThan(0));
                Assert.That((bool)coarse, Is.True);
            }
            finally
            {
                if (first != null) VoxelTemplateCache.Release(first);
                VoxelTemplateCache.Release(second);
            }
        }
    }
}
