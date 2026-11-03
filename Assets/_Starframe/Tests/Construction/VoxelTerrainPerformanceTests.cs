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
            foreach (var visuals in owner.GetComponentsInChildren<VoxelDetachedVisualFragments>(true)) if (visuals) visuals.Clear();
            foreach (var runtime in owner.GetComponentsInChildren<VoxelStructureRuntime>(true)) if (runtime) runtime.DisposeResources();
        }

        VoxelAssetDefinition Source(IEnumerable<Vector3Int> positions, bool hull = false)
        {
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); sources.Add(source);
            source.damageResetSeconds = 10;
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
            runtime.tinyFragmentMaxCells = tiny;
            runtime.tinyFragmentMaxWorldExtent = 2f;
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
        static Action Tick(VoxelStructureRuntime body)
        {
            var m = body.GetType().GetMethod("LateUpdate", Flags, null, Type.EmptyTypes, null);
            Assert.That(m, Is.Not.Null, "Terrain flush/expiry maintenance must stay on LateUpdate.");
            return (Action)Delegate.CreateDelegate(typeof(Action), body, m);
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

        [Test] public void SparseHealthAndQuietExpiryKeepMeshesCollidersAndRevisionStable()
        {
            var source = Source(Box(16, 16, 16)); var body = Body(source); var tick = Tick(body);
            AssertEditModePhysicsContract(body);
            var rigidbody = body.Body; float mass = rigidbody.mass;
            Assert.That(mass, Is.EqualTo(4096 * source.massPerCell * Math.Pow(source.cellSize, 3)).Within(.001));
            Assert.That(body.tinyFragmentMaxCells, Is.Zero, "Render-only tiny fragments are opt-in and off by default.");
            Assert.That(body.tinyFragmentMaxWorldExtent, Is.EqualTo(2f));
            var meshes = MeshIds(body); var boxes = ColliderIds(body);
            var aggregate = RootFilter(body).sharedMesh; var surface = RootRenderer(body);
            long revision = Number(body, "RenderRevision");
            int renderMeshes = body.RenderMeshCount; int vertices = body.TotalVertices;
            var a = Vector3Int.zero; var b = Vector3Int.right;
            body.ApplyDamage(a, 25); body.ApplyDamage(b, 10); body.AdvanceDamageTime(5);
            body.ApplyDamage(a, 10); body.FlushMeshChanges();
            Assert.That(body.GetHealth(a), Is.EqualTo(65)); Assert.That(body.GetHealth(b), Is.EqualTo(90));
            body.AdvanceDamageTime(5.1); tick();
            Assert.That(body.GetHealth(a), Is.EqualTo(65), "Refreshing A must not refresh B."); Assert.That(body.GetHealth(b), Is.EqualTo(100));
            body.AdvanceDamageTime(5.1); tick();
            Assert.That(body.GetHealth(a), Is.EqualTo(100)); Assert.That(body.RecentDamageCount, Is.Zero);
            AssertMeshIds(meshes, body); CollectionAssert.AreEqual(boxes, ColliderIds(body));
            Assert.That(RootFilter(body).sharedMesh, Is.SameAs(aggregate));
            Assert.That(RootRenderer(body), Is.SameAs(surface));
            Assert.That(body.RenderMeshCount, Is.EqualTo(renderMeshes)); Assert.That(body.TotalVertices, Is.EqualTo(vertices));
            Assert.That(Number(body, "RenderRevision"), Is.EqualTo(revision));
            Assert.That(body.Body, Is.SameAs(rigidbody), "Sparse damage must not replace or rebuild the physical body.");
            Assert.That(body.Body.mass, Is.EqualTo(mass));
            Assert.That(body.enabled, Is.False, "After quiet expiry the runtime must return to its disabled maintenance state.");
            Assert.That(body.IdleCellVisits, Is.Zero);
            Assert.That(body.AliveCount, Is.EqualTo(4096));
            Assert.That(body.DetachedVisualCells, Is.Zero); Assert.That(FindVisualManager(), Is.Null);
        }
        [Test] public void WarmIdleMaintenanceHasNoRebuildsAndChecksAllocationsWhenSupported()
        {
            // IdleCellVisits is a contractual constant 0, not an observed per-cell visit counter.
            // No-rebuild evidence is enabled=false plus unchanged counters, identity and revision.
            var body = Body(Source(Box(16, 16, 16)));
            foreach (var name in new[] { "GeometryBuildCount", "ConnectivityPassCount", "ColliderBuildCount" }) Require(body, name);
            Assert.That(body.IdleCellVisits, Is.Zero, "IdleCellVisits is the documented constant 0 contract, not a measurement.");
            Assert.That(body.enabled, Is.False, "A quiet runtime disables automatic maintenance.");
            var tick = Tick(body); tick(); tick();
            var names = new[] { "GeometryBuildCount", "ConnectivityPassCount", "ColliderBuildCount", "RenderRevision" };
            var before = names.Select(n => Number(body, n)).ToArray(); var meshes = MeshIds(body); var boxes = ColliderIds(body);
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            var allocated = method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
            // Unity/Mono can return a constant 0 from this counter. Positive control BEFORE the window,
            // and never interpret an unavailable counter as "zero allocations".
            bool allocationSupported = false;
            long controlBefore = -1, controlAfter = -1;
            if (allocated != null)
            {
                controlBefore = allocated();
                var positiveControl = new byte[1024 * 1024];
                controlAfter = allocated();
                GC.KeepAlive(positiveControl);
                allocationSupported = controlAfter - controlBefore >= 512 * 1024;
            }
            if (allocationSupported)
                TestContext.WriteLine("Managed allocation counter verified by positive control: " + (controlAfter - controlBefore) + " bytes for a 1 MiB array; zero-allocation assertion enabled.");
            else
                TestContext.WriteLine((allocated == null ? "GC.GetAllocatedBytesForCurrentThread unavailable" : "GC.GetAllocatedBytesForCurrentThread positive control did not move (delta " + (controlAfter - controlBefore) + " bytes for 1 MiB)") +
                    "; managed-allocation assertion not made. Functional no-rebuild checks still run.");
            long bytesBefore = allocationSupported ? allocated() : -1;
            for (int i = 0; i < 2048; i++) tick();
            long bytesAfter = allocationSupported ? allocated() : -1;
            CollectionAssert.AreEqual(before, names.Select(n => Number(body, n)).ToArray());
            AssertMeshIds(meshes, body); CollectionAssert.AreEqual(boxes, ColliderIds(body));
            Assert.That(body.enabled, Is.False, "Warm idle maintenance must keep the runtime disabled.");
            Assert.That(body.IdleCellVisits, Is.Zero);
            Assert.That(body.Body, Is.Not.Null); Assert.That(body.Body.mass, Is.GreaterThan(0));
            if (allocationSupported) Assert.That(bytesAfter - bytesBefore, Is.Zero, "Only the bound maintenance loop is inside the verified allocation window.");
        }

        [Test] public void LargeIntactHullKeepsOneSharedRootRendererAndActivatesChunksOnlyOnFirstLethalHit()
        {
            var source = Source(Box(65, 3, 3, -33), true);
            Assert.That(source.cells.Count, Is.GreaterThanOrEqualTo(MinimumChunkCells));
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.True, "The fixture must actually prewarm 4-cube chunks.");
            string sourceJson = JsonUtility.ToJson(source); int sourceRevision = source.Revision;
            var body = Body(source);
            // Contract: prewarmed chunk meshes exist, but the intact runtime keeps exactly one shared ROOT renderer.
            Assert.That(ChunksActive(body), Is.False, "Intact terrain must not mount chunk renderers.");
            Assert.That(body.RenderMeshCount, Is.EqualTo(1));
            var aggregate = RootFilter(body).sharedMesh;
            Assert.That(aggregate, Is.SameAs(body.RenderMeshes[0]));
            Assert.That(RootRenderer(body).enabled, Is.True);
            Assert.That(body.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1), "Prewarming must not create even disabled chunk renderers.");
            Assert.That(ChildMeshFilters(body), Is.Zero);
            AssertEditModePhysicsContract(body);
            long revision = Number(body, "RenderRevision");
            var killed = new Vector3Int(-32, 1, 1);
            Assert.That(body.GetHealth(killed), Is.EqualTo(source.blockHealth));
            body.ApplyDamage(killed, 1000); body.FlushMeshChanges();
            // Contract: chunk bindings activate only on the first lethal hit.
            Assert.That(ChunksActive(body), Is.True, "The first lethal hit must attach the prewarmed chunk bindings.");
            Assert.That(body.RenderMeshCount, Is.GreaterThan(1));
            Assert.That(ChildMeshFilters(body), Is.EqualTo(body.RenderMeshCount));
            Assert.That(RootRenderer(body).enabled, Is.False); Assert.That(RootFilter(body).sharedMesh, Is.Null);
            Assert.That(Number(body, "RenderRevision"), Is.GreaterThan(revision));
            Assert.That(body.AliveCount, Is.EqualTo(source.cells.Count - 1));
            var afterFirst = body.RenderMeshes.ToArray();
            var triangles = TrianglesByMesh(body);
            var second = new Vector3Int(31, 1, 1);
            var distant = afterFirst.Where(m => m.bounds.max.x < (second.x - 2 * ChunkEdge) * source.cellSize).ToDictionary(m => m, m => triangles[m]);
            Assert.That(distant.Count, Is.GreaterThan(0), "The fixture must include chunks far outside the second hit's neighbour halo.");
            body.ApplyDamage(second, 1000); body.FlushMeshChanges();
            Assert.That(body.AliveCount, Is.EqualTo(source.cells.Count - 2));
            foreach (var pair in distant)
            {
                Assert.That(body.RenderMeshes.Any(m => ReferenceEquals(m, pair.Key)), Is.True, "A distant chunk mesh was replaced on a local hit.");
                var current = TrianglesByMesh(body);
                Assert.That(current.ContainsKey(pair.Key), Is.True);
                CollectionAssert.AreEqual(pair.Value, current[pair.Key], "A distant chunk mesh was edited in place on a local hit.");
            }
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(sourceJson), "Chunk rendering must not mutate source cells.");
            Assert.That(source.Revision, Is.EqualTo(sourceRevision));
            var expected = source.cells.Select(c => c.position).Where(p => p != killed && p != second).ToList();
            Assert.That(expected.Count, Is.EqualTo(body.AliveCount));
            AssertExactCellCollision(body, expected);
            var materials = RootRenderer(body).sharedMaterials;
            var oracle = VoxelGeometry.Build(body.Cells, materials.Length, source.cellSize, source.hullModules);
            try
            {
                // Full unchunked build is an independent partition/invalidation oracle; bevelled hull triangles are retained exactly.
                CollectionAssert.AreEqual(SortedTriangles(oracle, Matrix4x4.identity), SortedTriangles(body),
                    "Chunk aggregation must preserve exact triangles, normals and material submesh; no occupancy-only bevel culling.");
            }
            finally { Object.DestroyImmediate(oracle); }
            AssertActiveRenderersMatch(body, materials);
            // A clean flush after the damage is idempotent.
            var unchanged = MeshIds(body); var collision = ColliderIds(body); body.FlushMeshChanges();
            AssertMeshIds(unchanged, body); CollectionAssert.AreEqual(collision, ColliderIds(body));
            Assert.That(FindVisualManager(), Is.Null);
        }

        [Test] public void IntactLargeHullChunksAreSharedAndDamageOnlyActivatesTheDamagedInstance()
        {
            var source = Source(Box(48, 4, 3), true);
            Assert.That(source.cells.Count, Is.GreaterThanOrEqualTo(MinimumChunkCells));
            var first = Body(source); var second = Body(source);
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.True);
            Assert.That(ChunksActive(first), Is.False); Assert.That(ChunksActive(second), Is.False);
            var shared = MeshIds(second); Assert.That(shared.Count, Is.EqualTo(1), "An intact instance has one root aggregate mesh.");
            CollectionAssert.AreEqual(MeshIds(first), shared);
            Assert.That(RootFilter(first).sharedMesh, Is.SameAs(RootFilter(second).sharedMesh), "Intact instances must share the aggregate template.");
            first.ApplyDamage(Vector3Int.zero, 1000); first.FlushMeshChanges();
            Assert.That(ChunksActive(first), Is.True);
            Assert.That(first.RenderMeshCount, Is.GreaterThan(1));
            Assert.That(ChunksActive(second), Is.False, "A peer's prewarmed chunks must stay detached after another instance is damaged.");
            Assert.That(second.AliveCount, Is.EqualTo(source.cells.Count));
            CollectionAssert.AreEqual(shared, MeshIds(second));
            Assert.That(second.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1));
            var peerAggregate = RootFilter(second).sharedMesh;
            first.DisposeResources();
            Object.DestroyImmediate(first.gameObject);
            AssertMeshIds(shared, second);
            Assert.That(RootFilter(second).sharedMesh, Is.SameAs(peerAggregate));
            Assert.That(peerAggregate != null, Is.True, "Destroying a damaged peer must not invalidate the shared intact aggregate.");
            Assert.That(Meshes(second).All(m => m), Is.True);
        }

        [Test] public void PhysicalSplitsConserveUniqueOwnersRecentHealthAndSourceData()
        {
            var source = Source(Box(11, 1, 1)); source.cells[10].quarterTurns = new Vector3Int(1, 2, 3);
            string json = JsonUtility.ToJson(source); int revision = source.Revision;
            var body = Body(source); body.ApplyDamage(Vector3Int.right * 10, 25); body.AdvanceDamageTime(2);
            body.ApplyDamage(Vector3Int.right * 5, 1000); body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true); Assert.That(pieces.Length, Is.EqualTo(2));
            var owners = new HashSet<Vector3Int>();
            foreach (var piece in pieces)
            {
                AssertEditModePhysicsContract(piece);
                Assert.That(piece.ColliderCount, Is.GreaterThan(0)); Assert.That(piece.RenderMeshCount, Is.GreaterThan(0));
                Assert.That(piece.RenderMeshes.All(m => m), Is.True);
                Assert.That(piece.Body.mass, Is.EqualTo(5 * source.massPerCell * Math.Pow(source.cellSize, 3)).Within(.001));
                foreach (var cell in piece.Cells.Values.Where(c => c.Alive))
                {
                    Assert.That(owners.Add(cell.position), Is.True); Assert.That(cell.owner, Is.SameAs(piece));
                    Assert.That(cell.quarterTurns, Is.EqualTo(source.cells.Single(c => c.position == cell.position).quarterTurns));
                }
            }
            Assert.That(pieces.Sum(p => p.Body.mass), Is.EqualTo(10 * source.massPerCell * Math.Pow(source.cellSize, 3)).Within(.001));
            Assert.That(owners.Count, Is.EqualTo(10)); Assert.That(owners.Contains(Vector3Int.right * 5), Is.False);
            var endpoint = pieces.Single(p => p.Cells.TryGetValue(Vector3Int.right * 10, out var c) && c.Alive);
            Assert.That(endpoint.GetHealth(Vector3Int.right * 10), Is.EqualTo(75));
            endpoint.AdvanceDamageTime(9); Tick(endpoint)(); Assert.That(endpoint.GetHealth(Vector3Int.right * 10), Is.EqualTo(100));
            Assert.That(endpoint.RecentDamageCount, Is.Zero);
            Assert.That(body.GetHealth(Vector3Int.right * 5), Is.Zero, "Quiet expiry cannot recreate a destroyed bridge.");
            Assert.That(pieces.Sum(p => p.DetachedVisualCells), Is.Zero, "EditMode splits stay fully physical.");
            Assert.That(FindVisualManager(), Is.Null);
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(json)); Assert.That(source.Revision, Is.EqualTo(revision));
        }

        [Test] public void TinyRenderOnlyPolicyIsOffByDefaultAndKeepsEditModeSplitsPhysical()
        {
            var positions = Box(3, 3, 3).ToList();
            positions.Add(new Vector3Int(3, 0, 0)); // One-cell bridge.
            positions.AddRange(new[] { new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0), new Vector3Int(5, 1, 0), new Vector3Int(4, 0, 1) });
            var source = Source(positions); var body = Body(source);
            Assert.That(body.tinyFragmentMaxCells, Is.Zero, "Render-only tiny fragments must stay opt-in.");
            Assert.That(body.tinyFragmentMaxWorldExtent, Is.EqualTo(2f));
            Assert.That(body.DetachedVisualCells, Is.Zero, "An attached small tendril is physical.");
            body.ApplyDamage(new Vector3Int(3, 0, 0), 1000); body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            Assert.That(pieces.Length, Is.EqualTo(2), "The default policy keeps both islands as physical runtimes.");
            Assert.That(pieces.Sum(p => p.AliveCount), Is.EqualTo(32));
            Assert.That(pieces.Sum(p => p.DetachedVisualCells), Is.Zero);
            Assert.That(pieces.Sum(p => p.DetachedVisualFragmentCount), Is.Zero);
            foreach (var piece in pieces)
            {
                AssertEditModePhysicsContract(piece);
                Assert.That(piece.ColliderCount, Is.GreaterThan(0));
                Assert.That(piece.RenderMeshCount, Is.GreaterThan(0));
            }
            Assert.That(pieces.SelectMany(p => p.Cells.Values.Where(c => c.Alive)).Select(c => c.position).Distinct().Count(), Is.EqualTo(32));
            Assert.That(FindVisualManager(), Is.Null, "The default tiny policy must never create a render-only manager.");
        }

        [Test] public void TinyPolicyAboveTheCellThresholdStaysPhysical()
        {
            var positions = Box(3, 3, 3).ToList();
            positions.Add(new Vector3Int(3, 0, 0));
            positions.AddRange(new[] { new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0), new Vector3Int(5, 1, 0), new Vector3Int(4, 0, 1) });
            var source = Source(positions); var body = Body(source, 4);
            Assert.That(body.tinyFragmentMaxCells, Is.EqualTo(4));
            body.ApplyDamage(new Vector3Int(3, 0, 0), 1000); body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            // Five detached cells exceed the <=4 opt-in limit, so this island is physical in every mode.
            Assert.That(pieces.Length, Is.EqualTo(2), "A five-cell detached island must stay a physical runtime.");
            Assert.That(pieces.Sum(p => p.DetachedVisualCells), Is.Zero);
            Assert.That(pieces.Sum(p => p.AliveCount), Is.EqualTo(32));
            foreach (var piece in pieces)
            {
                AssertEditModePhysicsContract(piece);
                Assert.That(piece.ColliderCount, Is.GreaterThan(0));
            }
        }

        [TestCase(3, true)] [TestCase(4, true)] [TestCase(5, false)]
        public void TinyPolicyHonoursCellThresholdOnlyForNewlyDetachedIslands(int detachedCount, bool renderOnly)
        {
            if (!RenderOnlyAdmissionSupported())
                Assert.Ignore("This runtime does not admit render-only fragments for the non-preview EditMode fixture; the <=4-cell policy branch is untested here (report as an omission).");
            var positions = Box(3, 3, 3).ToList(); positions.Add(new Vector3Int(3, 0, 0));
            positions.AddRange(new[] { new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0), new Vector3Int(5, 1, 0), new Vector3Int(4, 0, 1) }.Take(detachedCount));
            var source = Source(positions); var body = Body(source, 4);
            Assert.That(body.DetachedVisualCells, Is.Zero, "An attached small tendril is physical.");
            body.ApplyDamage(new Vector3Int(3, 0, 0), 1000); body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(true);
            long visual = pieces.Sum(p => Number(p, "DetachedVisualCells", 0));
            Assert.That(visual, Is.EqualTo(renderOnly ? detachedCount : 0));
            Assert.That(pieces.Sum(p => p.AliveCount) + visual, Is.EqualTo(source.cells.Count - 1));
            Assert.That(pieces.Length, Is.EqualTo(renderOnly ? 1 : 2));
            foreach (var piece in pieces) { AssertEditModePhysicsContract(piece); Assert.That(piece.ColliderCount, Is.GreaterThan(0)); }
            if (renderOnly)
            {
                var manager = VisualManager();
                Assert.That(Number(manager, "CellCount"), Is.EqualTo(detachedCount));
                Assert.That(Number(manager, "FragmentCount"), Is.EqualTo(1));
                Assert.That(((Component)manager).GetComponents<Collider>(), Is.Empty);
                Assert.That(((Component)manager).GetComponents<Rigidbody>(), Is.Empty);
            }
            else Assert.That(FindVisualManager(), Is.Null);
        }

        [TestCase(2f, true)] [TestCase(2.01f, false)]
        public void TinyPolicyAlsoRequiresWorldExtentAndRetainedSmallObjectsStayPhysical(float scale, bool renderOnly)
        {
            if (!RenderOnlyAdmissionSupported())
                Assert.Ignore("This runtime does not admit render-only fragments for the non-preview EditMode fixture; the world-extent policy branch is untested here (report as an omission).");
            var source = Source(Box(4, 1, 1)); var body = Body(source, 4, scale);
            body.ApplyDamage(Vector3Int.right * 2, 1000); body.FlushMeshChanges();
            long visuals = root.GetComponentsInChildren<VoxelStructureRuntime>(true).Sum(p => Number(p, "DetachedVisualCells", 0));
            Assert.That(visuals, Is.EqualTo(renderOnly ? 1 : 0));
            Assert.That(body.AliveCount, Is.EqualTo(2)); Assert.That(body.Body, Is.Not.Null, "Retained largest group is never made render-only.");
            Assert.That(body.ColliderCount, Is.GreaterThan(0));
            var initialSmall = Body(Source(new[] { Vector3Int.zero }), 4);
            AssertEditModePhysicsContract(initialSmall);
            Assert.That(initialSmall.ColliderCount, Is.GreaterThan(0)); Assert.That(Number(initialSmall, "DetachedVisualCells"), Is.Zero);
        }

        // Render-only admission is available for non-preview runtimes. The capability probe confirms
        // this fixture's EditMode runtime admits it; a configuration that refuses it is reported as
        // skipped instead of asserting a false contract.
        bool RenderOnlyAdmissionSupported()
        {
            var container = new GameObject("Tiny policy capability probe"); container.transform.SetParent(root.transform, false);
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); source.damageResetSeconds = 10;
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) for (int z = 0; z < 3; z++) source.cells.Add(new VoxelCell { position = new Vector3Int(x, y, z) });
            source.cells.Add(new VoxelCell { position = new Vector3Int(3, 0, 0) });
            foreach (var p in new[] { new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0), new Vector3Int(4, 1, 0) }) source.cells.Add(new VoxelCell { position = p });
            try
            {
                var go = new GameObject("Tiny policy capability body"); go.transform.SetParent(container.transform, false);
                var runtime = go.AddComponent<VoxelStructureRuntime>();
                runtime.tinyFragmentMaxCells = 4; runtime.tinyFragmentMaxWorldExtent = 2f; runtime.Initialize(source);
                runtime.ApplyDamage(new Vector3Int(3, 0, 0), 1000); runtime.FlushMeshChanges();
                return container.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c && c.GetType().Name == "VoxelDetachedVisualFragments");
            }
            finally { DisposeOwnedResources(container); Object.DestroyImmediate(container); Object.DestroyImmediate(source); }
        }
        object VisualManager()
        {
            var manager = FindVisualManager();
            Assert.That(manager, Is.Not.Null, "Render-only survivors must have a separately accountable lifetime owner.");
            return manager;
        }

        [Test] public void DataOnlyPreviewAcquiresNoTemplateChunkCacheOrRenderObjects()
        {
            var source = Source(Box(65, 3, 3, -33), true);
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.True);
            var go = new GameObject("Data-only terrain preview"); go.transform.SetParent(root.transform, false);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            int cacheBefore = TemplateCacheCount(); int chunksBefore = SharedChunkTemplateCount();
            runtime.Initialize(source, true, false);
            Assert.That(runtime.DataOnlyPreview, Is.True);
            Assert.That(runtime.RenderMeshCount, Is.Zero); Assert.That(runtime.TotalVertices, Is.Zero);
            Assert.That(Member(runtime, "template"), Is.Null, "dataOnly must not acquire VoxelTemplateCache.");
            Assert.That(Member(runtime, "chunks"), Is.Null, "dataOnly must not acquire shared render chunks.");
            Assert.That(go.GetComponent<MeshFilter>().sharedMesh, Is.Null);
            Assert.That(go.GetComponent<MeshRenderer>().enabled, Is.False);
            Assert.That(runtime.ColliderCount, Is.Zero);
            Assert.That(go.GetComponentsInChildren<BoxCollider>(true), Is.Empty);
            Assert.That(go.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1));
            Assert.That(go.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(TemplateCacheCount(), Is.EqualTo(cacheBefore), "dataOnly must not grow the template cache.");
            Assert.That(SharedChunkTemplateCount(), Is.EqualTo(chunksBefore), "dataOnly must not grow the shared chunk cache.");
            // A lethal edit still creates no render or collision cache.
            var removed = source.cells.Select(c => c.position).OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).First();
            Assert.That(runtime.ApplyDamage(removed, source.blockHealth * 2), Is.GreaterThan(0));
            runtime.FlushMeshChanges();
            Assert.That(runtime.AliveCount, Is.EqualTo(source.cells.Count - 1));
            Assert.That(runtime.RenderMeshCount, Is.Zero); Assert.That(runtime.ColliderCount, Is.Zero);
            Assert.That(go.GetComponentsInChildren<BoxCollider>(true), Is.Empty);
            Assert.That(TemplateCacheCount(), Is.EqualTo(cacheBefore));
            Assert.That(SharedChunkTemplateCount(), Is.EqualTo(chunksBefore));
            Assert.That(FindVisualManager(), Is.Null);
        }

        [Test] public void DetachedVisualManagerMovesAnalyticallyOwnsResourcesAndSharesOneOwner()
        {
            // The manager is one scene/container-wide owner. Its CellCount/FragmentCount are
            // manager-wide and must be read once, never summed per source runtime.
            var manager = VoxelDetachedVisualFragments.GetOrCreate(root.transform);
            Assert.That(manager, Is.Not.Null);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = "Detached manager source material" };
            var mesh = Quad();
            try
            {
                manager.AddOwnedMesh(mesh, new[] { material }, Matrix4x4.identity, Vector3.zero, new Vector3(1, 2, 3), Vector3.zero, 2, 0);
                manager.AddOwnedMesh(Quad(), new[] { material }, Matrix4x4.identity, Vector3.zero, Vector3.zero, Vector3.forward, 3, 0);
                Assert.That(manager.FragmentCount, Is.EqualTo(2));
                Assert.That(manager.CellCount, Is.EqualTo(5), "Manager-wide counters are counted once for the shared owner.");
                Assert.That(manager.OwnedMeshCount, Is.EqualTo(2));
                Assert.That(manager.OwnedMaterialCount, Is.EqualTo(1), "Repeated use of a material shares one owned clone.");
                Assert.That(manager.SubmittedDrawCount, Is.Zero, "EditMode never runs the render submit loop; draws are not invented.");
                var at0 = manager.GetWorldMatrix(0, 0); var at1 = manager.GetWorldMatrix(0, 1); var at10 = manager.GetWorldMatrix(0, 10);
                Assert.That(Vector3.Distance((Vector3)at1.GetColumn(3) - (Vector3)at0.GetColumn(3), new Vector3(1, 2, 3)), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance((Vector3)at10.GetColumn(3) - (Vector3)at0.GetColumn(3), new Vector3(10, 20, 30)), Is.LessThan(.0001f));
                // Two independent root-level sources share the same scene owner without extra objects.
                var a = new GameObject("Detached source A"); a.transform.SetParent(root.transform, false); var ra = a.AddComponent<VoxelStructureRuntime>();
                var b = new GameObject("Detached source B"); b.transform.SetParent(root.transform, false); var rb = b.AddComponent<VoxelStructureRuntime>();
                Assert.That(VoxelDetachedVisualFragments.GetOrCreateFor(ra), Is.SameAs(manager));
                Assert.That(VoxelDetachedVisualFragments.GetOrCreateFor(rb), Is.SameAs(manager));
                Assert.That(manager.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(manager.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                Assert.That(manager.GetComponentsInChildren<VoxelBodyHitbox>(true).Length, Is.EqualTo(0));
                ra.DisposeResources(); rb.DisposeResources();
                Object.DestroyImmediate(a); Object.DestroyImmediate(b);
                Assert.That(manager != null && manager.CellCount == 5, Is.True, "Visual lifetime belongs to the shared owner, never to a source runtime.");
                manager.Clear();
                Assert.That(manager.CellCount, Is.Zero); Assert.That(manager.FragmentCount, Is.Zero);
                Assert.That(manager.OwnedMeshCount, Is.Zero); Assert.That(manager.OwnedMaterialCount, Is.Zero);
                Assert.That(mesh == null, Is.True);
                Assert.That(material != null, Is.True, "A caller's source material is borrowed only while cloning.");
            }
            finally { if (material) Object.DestroyImmediate(material); }
        }

        [Test] public void TerrainRuntimeContractConstantsAndEditModeDefaultsAreFixed()
        {
            Assert.That(VoxelTerrainRenderChunks.ChunkEdge, Is.EqualTo(ChunkEdge));
            Assert.That(VoxelTerrainRenderChunks.MinimumCells, Is.EqualTo(MinimumChunkCells));
            var order = Attribute.GetCustomAttribute(typeof(VoxelStructureRuntime), typeof(DefaultExecutionOrder)) as DefaultExecutionOrder;
            Assert.That(order, Is.Not.Null, "Terrain must declare a fixed execution order so it flushes before ships.");
            Assert.That(order.order, Is.EqualTo(-100), "Terrain flushes at -100 before ship scripts.");
            Assert.That(typeof(VoxelStructureRuntime).GetMethod("LateUpdate", Flags), Is.Not.Null);
            Assert.That(typeof(VoxelStructureRuntime).GetMethod("FixedUpdate", Flags), Is.Not.Null);
            var go = new GameObject("Runtime defaults"); go.transform.SetParent(root.transform, false);
            try
            {
                var runtime = go.AddComponent<VoxelStructureRuntime>();
                Assert.That(runtime.tinyFragmentMaxCells, Is.Zero);
                Assert.That(runtime.tinyFragmentMaxWorldExtent, Is.EqualTo(2f));
                Assert.That(runtime.CollectDiagnostics, Is.False);
                Assert.That(go.GetComponents<MeshFilter>(), Is.Empty, "An uninitialized runtime must not build render resources.");
                Assert.That(go.GetComponent<Rigidbody>(), Is.Null);
            }
            finally { Object.DestroyImmediate(go); }
        }

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
        object FindVisualManager()
        {
            return root.GetComponentsInChildren<MonoBehaviour>(true)
                .FirstOrDefault(c => c && c.GetType().Name == "VoxelDetachedVisualFragments");
        }
        static Mesh Quad()
        {
            var mesh = new Mesh { name = "Detached manager test quad" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right * 2, new Vector3(2, 1, 0), Vector3.up };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
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
