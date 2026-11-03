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
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
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

        [Test] public void DistantLodAndCullLeaveDamageAndCollisionLive()
        {
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
            runtime.Build();
            var body = runtime.Structures[0];
            var exact = body.GetComponent<MeshRenderer>();
            Assert.That(body.MaterializedColliderCount, Is.EqualTo(body.ColliderCount),
                "Maps without the opt-in physics budget retain eager exact colliders.");
            Assert.That(body.UsesBoundsCollisionProxy, Is.False);
            coarse = MakeCoarseMesh();
            runtime.visualLodDistance = 10;
            runtime.visualLods.Add(new SpaceMapVisualLodEntry
            {
                asset = asset, mesh = coarse, geometrySignature = SpaceMapRuntime.ComputeGeometrySignature(asset)
            });
            runtime.RefreshVisualsNow(camera);
            var proxy = body.transform.Find("Distant scenery visual").GetComponent<MeshRenderer>();
            Assert.That(exact.forceRenderingOff, Is.True);
            Assert.That(proxy.enabled, Is.True);
            Assert.That(body.Body.detectCollisions, Is.True);
            Assert.That(body.ColliderCount, Is.GreaterThan(0));
            runtime.visualCullDistance = 20;
            runtime.RefreshVisualsNow(camera);
            Assert.That(exact.forceRenderingOff, Is.True);
            Assert.That(proxy.enabled, Is.False);
            Assert.That(body.ApplyDamage(Vector3Int.zero, 1000), Is.GreaterThan(0));
            body.FlushMeshChanges();
            runtime.visualCullDistance = 0;
            runtime.RefreshVisualsNow(camera);
            Assert.That(exact.forceRenderingOff, Is.False);
            Assert.That(proxy.enabled, Is.False);
            Assert.That(body.AliveCount, Is.EqualTo(1));
            Assert.That(body.GetHealth(Vector3Int.zero), Is.Zero);
            Assert.That(body.ColliderCount, Is.GreaterThan(0));
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
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
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

        [Test] public void PhysicsBudgetSelectsNearDetailedBoxesAndDamagePreservesTheSelectedMode()
        {
            MakeHollowCollisionAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            map.instances.Add(new VoxelMapInstance { asset = asset, position = Vector3.right * 100 });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
            runtime.useBudgetedPhysics = true; runtime.activeColliderBudget = 2;
            runtime.detailedCollisionDistance = 30; runtime.visualCamera = camera;
            runtime.Build();
            var near = runtime.Structures[0]; var far = runtime.Structures[1];
            Assert.That(near.ColliderCount, Is.GreaterThan(1));
            Assert.That(near.MaterializedColliderCount + far.MaterializedColliderCount, Is.Zero);
            Assert.That(near.UsesBoundsCollisionProxy && far.UsesBoundsCollisionProxy, Is.True);
            runtime.activeColliderBudget = near.ColliderCount + 1;
            camera.transform.position = Vector3.back * 10;
            runtime.RefreshPhysicsNow(camera);
            Assert.That(near.UsesBoundsCollisionProxy, Is.False);
            Assert.That(near.MaterializedColliderCount, Is.EqualTo(near.ColliderCount));
            Assert.That(EnabledBoxCount(near), Is.EqualTo(near.ColliderCount));
            Assert.That(far.UsesBoundsCollisionProxy, Is.True);
            Assert.That(runtime.ActiveMapColliderCount, Is.LessThanOrEqualTo(runtime.activeColliderBudget));
            camera.transform.position = Vector3.back * 32;
            runtime.RefreshPhysicsNow(camera);
            Assert.That(near.UsesBoundsCollisionProxy, Is.False, "Detailed collision should survive a small camera move past the enter distance.");
            camera.transform.position = Vector3.back * 10;
            runtime.RefreshPhysicsNow(camera);
            near.ApplyDamage(Vector3Int.zero, 1000); near.FlushMeshChanges();
            Assert.That(near.UsesBoundsCollisionProxy, Is.False);
            Assert.That(EnabledBoxCount(near), Is.EqualTo(near.ColliderCount));
            camera.transform.position = Vector3.right * 500;
            runtime.RefreshPhysicsNow(camera);
            Assert.That(near.UsesBoundsCollisionProxy && far.UsesBoundsCollisionProxy, Is.True);
            near.ApplyDamage(new Vector3Int(2, 2, 0), 1000); near.FlushMeshChanges();
            Assert.That(near.UsesBoundsCollisionProxy, Is.True);
            Assert.That(EnabledBoxCount(near), Is.EqualTo(1));
            Assert.That(runtime.ActiveMapColliderCount, Is.EqualTo(2));
            Assert.That(near.Body.detectCollisions && far.Body.detectCollisions, Is.True);
        }

        [Test] public void DeferredDamageUpdatesExactBoxesBeforeTheyAreMaterialized()
        {
            MakeHollowCollisionAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
            runtime.useBudgetedPhysics = true; runtime.activeColliderBudget = 1;
            runtime.visualCamera = camera; runtime.Build();
            var body = runtime.Structures[0];
            body.ApplyDamage(Vector3Int.zero, 1000); body.FlushMeshChanges();
            Assert.That(body.MaterializedColliderCount, Is.Zero);
            Assert.That(body.UsesBoundsCollisionProxy, Is.True);
            Assert.That(body.ColliderCount, Is.GreaterThan(0));
            asset.cells.Clear(); asset.NotifyChanged();
            runtime.activeColliderBudget = body.DetailedColliderCost;
            camera.transform.position = Vector3.back * 10;
            runtime.RefreshPhysicsNow(camera);
            Assert.That(body.MaterializedColliderCount, Is.EqualTo(body.ColliderCount));
            Assert.That(body.UsesBoundsCollisionProxy, Is.False);
            foreach (var box in body.GetComponents<BoxCollider>())
                if (box.enabled) Assert.That(new Bounds(box.center, box.size).Contains(Vector3.zero), Is.False,
                    "The delayed boxes must come from live cells, not the edited source asset or the old intact partition.");
        }

        [Test] public void MovingMapPromotionStaysWithinThePerFrameCreationBudgetWithoutLosingCollision()
        {
            MakeHollowCollisionAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
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

        [Test] public void PartialColliderPreparationKeepsProxyAndSurvivesDamageBeforeActivation()
        {
            MakeHollowCollisionAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
            runtime.useBudgetedPhysics = true; runtime.activeColliderBudget = 1; runtime.Build();
            var body = runtime.Structures[0];
            int prepared = body.PrepareDetailedCollision(1);
            Assert.That(prepared, Is.EqualTo(1));
            Assert.That(body.MaterializedColliderCount, Is.EqualTo(1));
            Assert.That(body.UsesBoundsCollisionProxy, Is.True);
            Assert.That(EnabledBoxCount(body), Is.EqualTo(1), "Incomplete exact hulls must not replace the live proxy.");
            body.ApplyDamage(Vector3Int.zero, 1000); body.FlushMeshChanges();
            body.SetDetailedCollisionEnabled(true);
            Assert.That(body.MaterializedColliderCount, Is.EqualTo(body.DetailedColliderCost));
            Assert.That(body.UsesBoundsCollisionProxy, Is.False);
            foreach (var box in body.GetComponents<BoxCollider>())
                if (box.enabled) Assert.That(new Bounds(box.center, box.size).Contains(Vector3.zero), Is.False);
        }

        [Test] public void DeferredCollisionPolicyFollowsPhysicalFragments()
        {
            asset.cells.Add(new VoxelCell { position = Vector3Int.right * 2 });
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
            runtime.useBudgetedPhysics = true; runtime.activeColliderBudget = 1;
            runtime.Build();
            var body = runtime.Structures[0];
            Assert.That(body.MaterializedColliderCount, Is.Zero);
            body.ApplyDamage(Vector3Int.right, 1000); body.FlushMeshChanges();
            Assert.That(runtime.Structures.Count, Is.EqualTo(2));
            foreach (var piece in runtime.Structures)
            {
                Assert.That(piece.ColliderCount, Is.EqualTo(1));
                Assert.That(piece.MaterializedColliderCount, Is.Zero);
                Assert.That(piece.GetComponents<BoxCollider>().Length, Is.EqualTo(1));
                Assert.That(piece.UsesBoundsCollisionProxy, Is.True);
                Assert.That(piece.Body.useGravity, Is.False);
            }
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
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
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

        [Test] public void DestroyingAnAnchorReleasesOnlyItsAuthoredContact()
        {
            var a = new VoxelMapInstance { asset = asset };
            var b = new VoxelMapInstance { asset = asset };
            map.instances.Add(a); map.instances.Add(b);
            map.assemblyLinks.Add(Link(a.id, b.id, Vector3Int.zero, Vector3Int.zero));
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true; runtime.Build();
            var first = runtime.Structures[0];
            Assert.That(runtime.ActiveAssemblyContactCount, Is.EqualTo(1));
            first.ApplyDamage(Vector3Int.zero, 1000);
            Assert.That(runtime.ActiveAssemblyContactCount, Is.Zero);
            first.FlushMeshChanges();
            Assert.That(first.AliveCount, Is.EqualTo(1));
            Assert.That(first.ColliderCount, Is.GreaterThan(0));
            Assert.That(first.Body.detectCollisions, Is.True);
        }

        [Test] public void FragmentOwnershipTransfersAStillAliveContactAnchor()
        {
            asset.cells.Add(new VoxelCell { position = Vector3Int.right * 2 });
            asset.cells.Add(new VoxelCell { position = Vector3Int.up });
            extraAsset = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            extraAsset.cells.Add(new VoxelCell { position = Vector3Int.zero });
            var a = new VoxelMapInstance { asset = asset };
            var b = new VoxelMapInstance { asset = extraAsset, position = Vector3.right * 2 };
            map.instances.Add(a); map.instances.Add(b);
            var link = Link(a.id, b.id, Vector3Int.right * 2, Vector3Int.zero);
            link.worldAnchor = Vector3.right * 2;
            map.assemblyLinks.Add(link);
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true; runtime.Build();
            runtime.Structures[0].ApplyDamage(Vector3Int.right, 1000);
            runtime.Structures[0].FlushMeshChanges();
            Assert.That(runtime.ActiveAssemblyContactCount, Is.EqualTo(1));
            Assert.That(runtime.Structures.Count, Is.EqualTo(3));
            var anchorOwner = runtime.Structures[2];
            Assert.That(anchorOwner, Is.Not.SameAs(runtime.Structures[0]));
            Assert.That(anchorOwner.Cells[Vector3Int.right * 2].Alive, Is.True);
            var joint = anchorOwner.GetComponent<FixedJoint>();
            Assert.That(joint, Is.Not.Null);
            Assert.That(joint.connectedBody, Is.SameAs(runtime.Structures[1].Body));
        }

        [Test] public void FreeCollisionFilterEndsWhenQuantizedCellsSeparate()
        {
            var a = new VoxelMapInstance { asset = asset };
            var b = new VoxelMapInstance { asset = asset };
            map.instances.Add(a); map.instances.Add(b);
            map.assemblyLinks.Add(Link(a.id, b.id, Vector3Int.zero, Vector3Int.zero, false));
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true; runtime.Build();
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

        [Test] public void BudgetedLargeHullPrewarmsHiddenExteriorAndKeepsDamageAndOriginalSourceGeneration()
        {
            MakeLazyHullAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
            runtime.lazyFineVisuals = true; runtime.useBudgetedPhysics = true;
            runtime.keepCoarseIntactVisuals = true;
            runtime.lightweightDamageThreshold = 512; runtime.activeColliderBudget = 1; runtime.Build();
            var body = runtime.Structures[0];
            Assert.That(body.UsesLightweightDamageVisuals, Is.True);
            Assert.That(body.HasLazyInitialVisual, Is.True);
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(coarse));
            var children = body.GetComponentsInChildren<MeshRenderer>();
            Assert.That(children.Length, Is.GreaterThan(1));
            foreach(var child in children) if(child.gameObject != body.gameObject) Assert.That(child.enabled, Is.False);
            asset.cells.Clear(); asset.NotifyChanged();
            body.ApplyDamage(new Vector3Int(3,3,3), 1000); body.FlushMeshChanges();
            Assert.That(body.AliveCount, Is.EqualTo(512));
            Assert.That(body.GetHealth(new Vector3Int(3,3,3)), Is.Zero);
            Assert.That(body.HasLazyInitialVisual, Is.False);
            Assert.That(body.RenderMeshCount, Is.GreaterThan(0));
            Assert.That(body.UsesBoundsCollisionProxy, Is.True);
            Assert.That(body.Cells[new Vector3Int(8,0,0)].Alive, Is.True);
            Assert.That((bool)coarse, Is.True);
        }

        [Test] public void BudgetedFineModelMapsDoNotOptIntoExteriorOnlyDamage()
        {
            MakeLazyHullAsset(); map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
            runtime.lazyFineVisuals = true; runtime.useBudgetedPhysics = true; runtime.activeColliderBudget = 1;
            runtime.keepCoarseIntactVisuals = false; runtime.lightweightDamageThreshold = 128; runtime.Build();
            Assert.That(runtime.Structures[0].UsesLightweightDamageVisuals, Is.False,
                "Maps that promote to authored hull models must retain their exact damaged-model path.");
            Assert.That(runtime.Structures[0].GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(1));
        }

        [Test] public void LazyColdBuildUsesBorrowedCoarseButFirstDamageKeepsPhysicalCells()
        {
            MakeLazyHullAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true; runtime.lazyFineVisuals = true;
            runtime.Build();
            var body = runtime.Structures[0];
            Assert.That(body.HasLazyInitialVisual, Is.True);
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(coarse));
            Assert.That(body.AliveCount, Is.EqualTo(513));
            Assert.That(body.ColliderCount, Is.GreaterThan(0));
            Assert.That(body.Body.useGravity, Is.False);
            var originalTint = body.GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_BaseColor");
            asset.cells.RemoveAt(asset.cells.Count - 1);
            asset.surfaceColors[0] = Color.red;
            asset.NotifyChanged();
            body.ApplyDamage(new Vector3Int(2, 2, 2), 1000);
            body.FlushMeshChanges();
            Assert.That(body.HasLazyInitialVisual, Is.False);
            Assert.That(body.AliveCount, Is.EqualTo(512));
            Assert.That(body.GetHealth(new Vector3Int(2, 2, 2)), Is.Zero);
            Assert.That(body.Cells[new Vector3Int(8, 0, 0)].Alive, Is.True);
            Assert.That(body.ColliderCount, Is.GreaterThan(0));
            Assert.That(body.RenderMeshCount, Is.GreaterThan(0));
            float furthestRenderedX = float.NegativeInfinity;
            foreach (var rendered in body.RenderMeshes) furthestRenderedX = Mathf.Max(furthestRenderedX, rendered.bounds.max.x);
            Assert.That(furthestRenderedX, Is.GreaterThan(8f), "Delayed chunks must include the original protrusion removed from the source asset.");
            Assert.That(body.GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_BaseColor"), Is.EqualTo(originalTint));
            Assert.That((bool)coarse, Is.True, "The imported coarse mesh is borrowed, never destroyed by runtime promotion.");
        }

        [Test] public void LazyFineGenerationUsesOriginalCellsAndTintAfterSourceEdit()
        {
            MakeLazyHullAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true; runtime.lazyFineVisuals = true;
            runtime.Build();
            var body = runtime.Structures[0];
            var originalTint = body.GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_BaseColor");
            asset.cells.RemoveAt(asset.cells.Count - 1);
            asset.surfaceColors[0] = Color.red;
            asset.NotifyChanged();
            body.EnsureExactVisuals();
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh.bounds.max.x, Is.GreaterThan(8f));
            Assert.That(body.GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_BaseColor"), Is.EqualTo(originalTint));
            Assert.That(body.AliveCount, Is.EqualTo(513));
            body.ApplyDamage(new Vector3Int(2, 2, 2), 1000);
            body.FlushMeshChanges();
            Assert.That(body.Cells[new Vector3Int(8, 0, 0)].Alive, Is.True);
            Assert.That(body.GetHealth(new Vector3Int(2, 2, 2)), Is.Zero);
            Assert.That(body.RenderMeshCount, Is.GreaterThan(0));
            Assert.That(body.GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_BaseColor"), Is.EqualTo(originalTint));
        }

        [Test] public void CoarseIntactModeDoesNotPromoteNearSceneryUntilExplicitlyDisabled()
        {
            MakeLazyHullAsset();
            map.instances.Add(new VoxelMapInstance { asset = asset });
            var runtime = root.AddComponent<SpaceMapRuntime>(); runtime.definition = map; runtime.allowDestruction = true;
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
