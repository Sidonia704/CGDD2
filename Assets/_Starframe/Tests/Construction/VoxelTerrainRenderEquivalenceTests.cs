using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Starframe.Tests
{
    // EditMode contract tests for the large-hull chunked renderer.
    //
    // Contract under test: a large hull (>= 512 cells) renders its intact state through ONE shared aggregate
    // mesh and prewarms 4x4x4-cell chunk meshes; chunk renderers only appear on the first lethal hit, and a
    // later lethal hit may only rebuild the chunks touching that cell and its six face neighbours. Rendered
    // triangles must carry the authored positions, normals, UVs, tangents and material slot of
    // VoxelGeometry.Build for the same cells, including negative cells, quarter-turn rotations and a complete
    // 8-cell Block8 partition; authored bevel seams are never culled. A lease on an old source generation (and
    // the source runtime that created it) must survive a newer generation.
    //
    // Compare rendered triangles and the authored vertex streams, never vertex/index ordering or an
    // aggregate vertex count. Never repair/recalculate normals or tangents in a test or an oracle here.
    //
    // Isolation: the fixture preview scene stays non-active while a saved empty staging scene is active, so
    // the open scene, its dirty flag and the default PhysicsScene are untouched. No BlockModules.Configure,
    // no VoxelTemplateCache/global cache clear, no Physics.SyncTransforms/Simulate, no default physics changes
    // and no asset save.
    public sealed class VoxelTerrainRenderEquivalenceTests
    {
        const int ChunkEdge = 4;
        const int MinimumChunkCells = 512;
        // Saved empty scene used as the active staging scene: the test runner does not allow NewScene.
        const string StagingScenePath = "Assets/_Starframe/Tests/Construction/Fixtures/TerrainTestStaging.unity";
        const double Quantization = 100000d;
        readonly List<Object> owned = new List<Object>();
        Scene previous, staging, preview;
        SimulationMode physicsMode;
        Vector3 gravity;
        float fixedStep;
        GameObject root;
        ModuleDatabase database;

        [SetUp]
        public void Setup()
        {
            Assert.That(Application.isPlaying, Is.False, "EditMode fixture only.");
            database = AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            Assert.That(database, Is.Not.Null, "Use the real module fixture, including its bevels and Block8 footprint.");
            Assert.That(VoxelTerrainRenderChunks.ChunkEdge, Is.EqualTo(ChunkEdge), "Terrain render chunks are 4x4x4 cells.");
            Assert.That(VoxelTerrainRenderChunks.MinimumCells, Is.EqualTo(MinimumChunkCells), "Chunk rendering starts at 512 cells.");
            physicsMode = Physics.simulationMode;
            gravity = Physics.gravity;
            fixedStep = Time.fixedDeltaTime;
            previous = SceneManager.GetActiveScene();
            preview = EditorSceneManager.NewPreviewScene();
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(StagingScenePath), Is.Not.Null,
                "The shared empty staging scene fixture is missing: " + StagingScenePath);
            staging = EditorSceneManager.OpenScene(StagingScenePath, OpenSceneMode.Additive);
            Assert.That(staging.IsValid(), Is.True, "The shared empty staging scene must load: " + StagingScenePath);
            Assert.That(SceneManager.SetActiveScene(staging), Is.True);
            Assert.That(preview.GetPhysicsScene().IsValid(), Is.True);
            Assert.That(preview.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene),
                "Fixture bodies must never live in the default physics scene.");
            root = new GameObject("Terrain render equivalence fixture");
            SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = Vector3.up * 40000;
        }

        [TearDown]
        public void Cleanup()
        {
            if (root)
            {
                foreach (var runtime in root.GetComponentsInChildren<VoxelStructureRuntime>(true)) if (runtime) runtime.DisposeResources();
                Object.DestroyImmediate(root);
            }
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (previous.IsValid() && previous.isLoaded) Assert.That(SceneManager.SetActiveScene(previous), Is.True);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
            Assert.That(Physics.simulationMode, Is.EqualTo(physicsMode), "Terrain rendering must not change default physics.");
            Assert.That(Physics.gravity, Is.EqualTo(gravity));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(previous.handle));
        }

        [Test]
        public void IntactLargeInstancesShareOnlyTheOriginalAggregateRenderer()
        {
            var source = Grid(0, false);
            string sourceBefore = JsonUtility.ToJson(source);
            var a = Body(source);
            long revision = RenderRevision(a);
            var aggregate = a.GetComponent<MeshFilter>().sharedMesh;
            var b = Body(source);
            Assert.That(b.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(aggregate));
            Assert.That(RenderRevision(a), Is.EqualTo(revision), "Creating a sharing instance must not invalidate the first one.");
            foreach (var runtime in new[] { a, b })
            {
                var meshes = RenderMeshes(runtime);
                Assert.That(meshes.Count, Is.EqualTo(1));
                Assert.That(meshes[0], Is.SameAs(aggregate));
                Assert.That(runtime.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1),
                    "Prewarming chunk meshes must not create even disabled chunk renderers on an intact instance.");
                Assert.That(runtime.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(1),
                    "An intact instance must not own chunk views before the first lethal hit.");
                AssertEquivalentToOracle(runtime);
            }
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(sourceBefore), "Initialization must not modify the source asset.");
        }

        [TestCase(0, -1)] [TestCase(0, 0)] [TestCase(0, 1)]
        [TestCase(1, -1)] [TestCase(1, 0)] [TestCase(1, 1)]
        [TestCase(2, -1)] [TestCase(2, 0)] [TestCase(2, 1)]
        public void BoundaryDeletionPreservesExactTrianglesNormalsUvTangentsAndMaterialSlots(int axis, int boundaryChunk)
        {
            var source = Grid(axis, true);
            int boundary = boundaryChunk * ChunkEdge;
            var turns = new Vector3Int(axis + 1, boundaryChunk + 2, axis + boundaryChunk + 2);
            var piece = InsertBlock8AcrossBoundary(source, axis, boundary, turns);
            var removed = piece.First(cell => cell.position[axis] == boundary).position;
            Assert.That(PositiveMod(removed[axis], ChunkEdge), Is.Zero,
                "The deleted Block8 cell must sit on a chunk boundary so partition caps cross chunk edges.");
            Assert.That(piece.Count(cell => cell.position[axis] == removed[axis]), Is.EqualTo(4));
            string sourceBefore = JsonUtility.ToJson(source);
            var runtime = Body(source);
            AssertEquivalentToOracle(runtime);
            long revision = RenderRevision(runtime);

            Kill(runtime, removed);

            Assert.That(root.GetComponentsInChildren<VoxelStructureRuntime>().Length, Is.EqualTo(1), "A dense grid must not fragment after one deletion.");
            var meshes = RenderMeshes(runtime);
            Assert.That(meshes.Count, Is.GreaterThan(1), "The fixture must actually activate chunk rendering.");
            AssertChunkBounds(meshes, source.cellSize);
            Assert.That(RenderRevision(runtime), Is.GreaterThan(revision));
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(sourceBefore), "Runtime chunking must not modify source cells or offsets.");
            // Every one of the complete eight Block8 offsets must survive with its own rotation and model offset.
            foreach (var cell in piece)
            {
                Assert.That(runtime.Cells.TryGetValue(cell.position, out var state), Is.True);
                Assert.That(state.Alive, Is.EqualTo(cell.position != removed));
                Assert.That(state.shape, Is.EqualTo(BlockShape.Block8));
                Assert.That(state.quarterTurns, Is.EqualTo(cell.quarterTurns));
                Assert.That(state.modelOffset, Is.EqualTo(cell.modelOffset));
            }
            AssertEquivalentToOracle(runtime, oracle => AssertExposedBlock8Cap(oracle, runtime, piece, removed));
        }

        [Test]
        public void SecondLethalHitKeepsDistantChunkMeshReferencesAndContentsUnchanged()
        {
            var source = Grid(0, true);
            string sourceBefore = JsonUtility.ToJson(source);
            var runtime = Body(source);
            Kill(runtime, new Vector3Int(-ChunkEdge, 1, 1));
            var before = RenderMeshes(runtime).ToArray();
            Assert.That(before.Length, Is.GreaterThan(1));
            AssertChunkBounds(before, source.cellSize);
            var second = new Vector3Int(ChunkEdge, 1, 1);
            var secondChunk = ChunkMeshFor(before, second, source.cellSize);
            var materials = RootMaterials(runtime);
            var distant = before
                .Where(mesh => mesh.bounds.max.x < (second.x - 2 * ChunkEdge) * source.cellSize)
                .ToDictionary(mesh => mesh, mesh => Triangles(new[] { mesh }, materials));
            Assert.That(distant.Count, Is.GreaterThan(0), "The fixture must include chunks beyond the second hit's neighbour halo.");
            long revision = RenderRevision(runtime);

            Kill(runtime, second);

            Assert.That(RenderRevision(runtime), Is.GreaterThan(revision));
            Assert.That(RenderMeshes(runtime).Count, Is.EqualTo(before.Length), "A local hit must not create or drop whole chunks.");
            Assert.That(RenderMeshes(runtime).Any(mesh => ReferenceEquals(mesh, secondChunk)), Is.False, "The hit chunk must be rebuilt.");
            foreach (var pair in distant)
            {
                Assert.That(RenderMeshes(runtime).Any(mesh => ReferenceEquals(mesh, pair.Key)), Is.True, "A distant mesh was replaced.");
                AssertTrianglesEqual(pair.Value, Triangles(new[] { pair.Key }, materials), "A distant mesh was edited in place.");
            }
            Assert.That(runtime.AliveCount, Is.EqualTo(source.cells.Count - 2));
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(sourceBefore), "Runtime chunk rebuilds must not modify the source asset.");
            AssertEquivalentToOracle(runtime);
        }

        [Test]
        public void SharedInitialChunksAreCopyOnWriteAcrossDamagedRuntimes()
        {
            var source = Grid(0, true);
            var a = Body(source);
            var b = Body(source);
            var aggregate = a.GetComponent<MeshFilter>().sharedMesh;
            var aggregateBefore = Triangles(new[] { aggregate }, RootMaterials(a));
            var first = new Vector3Int(-ChunkEdge, 1, 1);
            Kill(a, first);
            Kill(b, first);
            var aMeshes = RenderMeshes(a).ToArray();
            var shared = aMeshes.Where(mesh => RenderMeshes(b).Any(other => ReferenceEquals(mesh, other))).ToArray();
            Assert.That(shared.Length, Is.GreaterThan(0), "Unchanged chunks must reuse the shared initial template, not be independently rebuilt.");
            Assert.That(aMeshes.Length - shared.Length, Is.LessThanOrEqualTo(4),
                "One lethal hit may only replace the hit chunk and the chunks touching its six face neighbours.");
            Assert.That(shared.All(mesh => (bool)mesh), Is.True);
            AssertChunkBounds(shared, source.cellSize);
            var second = new Vector3Int(ChunkEdge, 1, 1);
            var affectedShared = shared.Where(mesh => mesh.bounds.Contains((Vector3)second * source.cellSize)).ToArray();
            Assert.That(affectedShared.Length, Is.GreaterThan(0), "The second hit must target a still-shared chunk.");
            var bMeshes = RenderMeshes(b).ToArray();
            var bBefore = Triangles(bMeshes, RootMaterials(b));
            long bRevision = RenderRevision(b);

            Kill(a, second);

            Assert.That(a.AliveCount, Is.EqualTo(source.cells.Count - 2));
            Assert.That(b.AliveCount, Is.EqualTo(source.cells.Count - 1));
            Assert.That(b.GetHealth(second), Is.EqualTo(source.blockHealth));
            Assert.That(RenderRevision(b), Is.EqualTo(bRevision));
            AssertSameMeshes(bMeshes, RenderMeshes(b));
            AssertTrianglesEqual(bBefore, Triangles(RenderMeshes(b), RootMaterials(b)), "A hit on one runtime changed its peer.");
            Assert.That(RenderMeshes(a).Count, Is.EqualTo(aMeshes.Length), "The dirty runtime must keep the same chunk partitioning.");
            foreach (var mesh in affectedShared)
            {
                Assert.That(mesh, Is.Not.Null);
                Assert.That(RenderMeshes(a).Any(current => ReferenceEquals(current, mesh)), Is.False, "The dirty runtime must release its shared version.");
            }
            Assert.That(RenderMeshes(a).Any(current => ReferenceEquals(current, aggregate)), Is.False, "The intact aggregate must not become a chunk.");
            AssertTrianglesEqual(aggregateBefore, Triangles(new[] { aggregate }, RootMaterials(a)), "The shared intact aggregate was mutated.");
            AssertEquivalentToOracle(a);
            AssertEquivalentToOracle(b);

            a.DisposeResources();
            Object.DestroyImmediate(a.gameObject);
            AssertSameMeshes(bMeshes, RenderMeshes(b));
            AssertTrianglesEqual(bBefore, Triangles(RenderMeshes(b), RootMaterials(b)), "Disposing one owner invalidated another owner's chunks.");
        }

        [Test]
        public void LocalLethalHitRebuildsNoChunkOutsideTheHitChunkAndItsFaceNeighbours()
        {
            var source = CubeBlock(8);
            var runtime = Body(source);
            var materials = RootMaterials(runtime);
            Kill(runtime, new Vector3Int(-ChunkEdge, -ChunkEdge, -ChunkEdge));
            var before = RenderMeshes(runtime).ToArray();
            Assert.That(before.Length, Is.EqualTo(8), "An 8x8x8 hull must prewarm exactly eight non-empty 4x4x4 chunks.");
            AssertChunkBounds(before, source.cellSize);
            var hit = new Vector3Int(0, 0, 0);
            var hitChunk = ChunkMeshFor(before, hit, source.cellSize);
            var localCells = new[] { hit, hit + Vector3Int.left, hit + Vector3Int.down, hit + Vector3Int.back };
            var local = new[] { hitChunk }.Concat(localCells.Select(cell => ChunkMeshFor(before, cell, source.cellSize))).Distinct().ToArray();
            Assert.That(local.Length, Is.EqualTo(4), "A cell at a chunk corner touches its own chunk plus three face-neighbour chunks.");
            var outside = before.Where(mesh => !local.Contains(mesh)).ToArray();
            Assert.That(outside.Length, Is.EqualTo(4));
            var outsideBefore = outside.ToDictionary(mesh => mesh, mesh => Triangles(new[] { mesh }, materials));
            long revision = RenderRevision(runtime);
            int alive = runtime.AliveCount;

            Kill(runtime, hit);

            var after = RenderMeshes(runtime).ToArray();
            Assert.That(RenderRevision(runtime), Is.GreaterThan(revision));
            Assert.That(runtime.AliveCount, Is.EqualTo(alive - 1));
            Assert.That(after.Length, Is.EqualTo(before.Length), "A local removal must not create or drop whole chunks.");
            var replaced = before.Where(mesh => after.All(current => !ReferenceEquals(current, mesh))).ToArray();
            Assert.That(replaced.Length, Is.GreaterThan(0), "The damaged chunk must be rebuilt.");
            Assert.That(replaced.Length, Is.LessThanOrEqualTo(local.Length));
            foreach (var mesh in replaced) Assert.That(local.Contains(mesh), Is.True,
                "Only the hit chunk and chunks containing a face neighbour of the removed cell may be rebuilt.");
            Assert.That(after.Any(current => ReferenceEquals(current, hitChunk)), Is.False, "The damaged chunk kept its old mesh.");
            foreach (var pair in outsideBefore)
            {
                Assert.That(after.Any(current => ReferenceEquals(current, pair.Key)), Is.True, "A chunk outside the local halo was replaced.");
                AssertTrianglesEqual(pair.Value, Triangles(new[] { pair.Key }, materials), "A chunk outside the local halo was edited in place.");
            }
            AssertEquivalentToOracle(runtime);
        }

        [TestCase(false)] [TestCase(true)]
        public void NonlethalDamageAndExpiryDoNotChangeRenderRevisionOrMeshIdentity(bool activateChunks)
        {
            var source = Grid(0, true);
            var runtime = Body(source);
            if (activateChunks) Kill(runtime, new Vector3Int(-ChunkEdge, 1, 1));
            var meshes = RenderMeshes(runtime).ToArray();
            int renderers = runtime.GetComponentsInChildren<MeshRenderer>(true).Length;
            int filters = runtime.GetComponentsInChildren<MeshFilter>(true).Length;
            var before = Triangles(meshes, RootMaterials(runtime));
            long revision = RenderRevision(runtime);
            long geometryBuilds = runtime.GeometryBuildCount;
            var position = new Vector3Int(1, 1, 1);
            Assert.That(runtime.GetHealth(position), Is.EqualTo(source.blockHealth));

            runtime.ApplyDamage(position, source.blockHealth * .25f);
            runtime.FlushMeshChanges();
            Assert.That(runtime.GetHealth(position), Is.EqualTo(source.blockHealth * .75f));
            Assert.That(runtime.RecentDamageCount, Is.EqualTo(1));
            Assert.That(RenderRevision(runtime), Is.EqualTo(revision));
            Assert.That(runtime.GeometryBuildCount, Is.EqualTo(geometryBuilds), "Nonlethal damage must not build geometry.");
            Assert.That(runtime.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(renderers));
            Assert.That(runtime.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(filters),
                "Nonlethal damage must not activate or create chunk views.");
            AssertSameMeshes(meshes, RenderMeshes(runtime));
            if (!activateChunks)
            {
                Assert.That(meshes.Length, Is.EqualTo(1), "An intact instance keeps its single aggregate mesh.");
                Assert.That(renderers, Is.EqualTo(1), "Chunk activation belongs to the first lethal hit only.");
            }

            runtime.AdvanceDamageTime(source.damageResetSeconds + 1);
            Assert.That(runtime.GetHealth(position), Is.EqualTo(source.blockHealth));
            Assert.That(runtime.RecentDamageCount, Is.Zero);
            runtime.FlushMeshChanges();
            Assert.That(RenderRevision(runtime), Is.EqualTo(revision));
            Assert.That(runtime.GeometryBuildCount, Is.EqualTo(geometryBuilds));
            AssertSameMeshes(meshes, RenderMeshes(runtime));
            AssertTrianglesEqual(before, Triangles(RenderMeshes(runtime), RootMaterials(runtime)), "Temporary health expiry must not trigger rendering work.");
            AssertEquivalentToOracle(runtime);
        }

        [Test]
        public void SourceAndFragmentDisposalReleaseOwnedResourcesWithoutInvalidatingTheSurvivor()
        {
            var source = SplitGrid();
            source.baseColor = new Color(.8f, .9f, .7f);
            string sourceBefore = JsonUtility.ToJson(source);
            var original = Body(source);
            var aggregate = original.GetComponent<MeshFilter>().sharedMesh;
            var generated = new HashSet<Mesh> { aggregate };
            var tintedMaterials = new HashSet<Material>(RootMaterials(original));
            var shipMesh = database.Get(BlockShape.Cube).mesh;
            var shipMaterial = database.Get(BlockShape.Cube).material;
            Kill(original, Vector3Int.zero);
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>();
            Assert.That(pieces.Length, Is.EqualTo(2));
            Assert.That(pieces.All(piece => piece.AliveCount >= MinimumChunkCells), Is.True,
                "Both the source island and fragment must exercise chunk ownership, not the small-fragment path.");
            var fragment = pieces.Single(piece => piece != original);
            foreach (var piece in pieces)
            {
                Assert.That(RenderMeshes(piece).Count, Is.GreaterThan(1));
                AssertChunkBounds(RenderMeshes(piece), source.cellSize);
                generated.UnionWith(RenderMeshes(piece));
                tintedMaterials.UnionWith(RootMaterials(piece));
                AssertEquivalentToOracle(piece);
            }
            var survivorMeshes = RenderMeshes(fragment).ToArray();
            var survivorBefore = Triangles(survivorMeshes, RootMaterials(fragment));
            long survivorRevision = RenderRevision(fragment);
            int survivorAlive = fragment.AliveCount;

            original.DisposeResources();
            Object.DestroyImmediate(original.gameObject);

            Assert.That(survivorMeshes.All(mesh => (bool)mesh), Is.True, "Destroying the source owner must not release live fragment meshes.");
            AssertSameMeshes(survivorMeshes, RenderMeshes(fragment));
            Assert.That(RenderRevision(fragment), Is.EqualTo(survivorRevision));
            Assert.That(fragment.AliveCount, Is.EqualTo(survivorAlive));
            AssertTrianglesEqual(survivorBefore, Triangles(RenderMeshes(fragment), RootMaterials(fragment)), "The remaining fragment's native resources changed.");
            AssertEquivalentToOracle(fragment);

            fragment.DisposeResources();
            Object.DestroyImmediate(fragment.gameObject);

            Assert.That(generated.All(mesh => !mesh), Is.True, "The final source/fragment owner must release aggregate, private and shared chunk meshes.");
            Assert.That(tintedMaterials.All(material => !material), Is.True, "Runtime tint materials must not leak after their owners are destroyed.");
            Assert.That((bool)shipMesh, Is.True, "Runtime disposal must not destroy source module meshes.");
            Assert.That((bool)shipMaterial, Is.True, "Runtime disposal must not destroy source module materials.");
            Assert.That(source.Validate(out var error), Is.True, error);
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(sourceBefore), "Runtime disposal must not modify the source asset.");
        }

        [Test]
        public void SourceGenerationChangeKeepsLeasedOldGenerationChunksAndFragmentsAlive()
        {
            var source = SplitGrid();
            source.baseColor = new Color(.8f, .9f, .7f);
            int generation = source.GeometryRevision;
            var original = Body(source);
            var peer = Body(source);
            var aggregate = peer.GetComponent<MeshFilter>().sharedMesh;
            var aggregateBefore = Triangles(new[] { aggregate }, RootMaterials(peer));
            var peerMaterials = new HashSet<Material>(RootMaterials(peer));
            Kill(original, Vector3Int.zero);
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>();
            Assert.That(pieces.Length, Is.EqualTo(3), "Source island, physical fragment and intact peer must coexist.");
            var fragment = pieces.Single(piece => piece != original && piece != peer);
            var originalMeshes = RenderMeshes(original).ToArray();
            var fragmentMeshes = RenderMeshes(fragment).ToArray();
            Assert.That(originalMeshes.Length, Is.GreaterThan(1));
            Assert.That(fragmentMeshes.Length, Is.GreaterThan(1));
            var originalBefore = Triangles(originalMeshes, RootMaterials(original));
            var fragmentBefore = Triangles(fragmentMeshes, RootMaterials(fragment));
            var originalMaterials = new HashSet<Material>(RootMaterials(original));
            var fragmentMaterials = new HashSet<Material>(RootMaterials(fragment));

            // Lease-breaking source edit: mutate geometry and publish a new generation, then create a new instance.
            source.cells[0].surface = PositiveMod(source.cells[0].surface + 1, source.surfaceColors.Count);
            source.NotifyChanged();
            Assert.That(source.GeometryRevision, Is.Not.EqualTo(generation), "The source edit must create a new geometry generation.");
            var newer = Body(source);
            var newerMeshes = RenderMeshes(newer).ToArray();
            var newerMaterials = new HashSet<Material>(RootMaterials(newer));
            Assert.That(newer.AliveCount, Is.EqualTo(source.cells.Count));
            Assert.That(newerMeshes.Length, Is.EqualTo(1), "A fresh large instance is intact until its first lethal hit.");
            Assert.That(newerMeshes[0], Is.Not.SameAs(aggregate), "A new source generation must get its own aggregate mesh.");
            Assert.That(newerMeshes[0].vertexCount, Is.GreaterThan(0));

            // The old generation is still leased, so nothing it owns may be destroyed or silently re-generated.
            Assert.That((bool)aggregate, Is.True, "A newer generation must not release the intact peer's aggregate mesh.");
            AssertSameMeshes(originalMeshes, RenderMeshes(original));
            AssertSameMeshes(fragmentMeshes, RenderMeshes(fragment));
            AssertTrianglesEqual(originalBefore, Triangles(RenderMeshes(original), RootMaterials(original)), "The old-generation source island changed.");
            AssertTrianglesEqual(fragmentBefore, Triangles(RenderMeshes(fragment), RootMaterials(fragment)), "The old-generation fragment changed.");
            AssertTrianglesEqual(aggregateBefore, Triangles(new[] { aggregate }, RootMaterials(peer)), "The old-generation aggregate changed.");
            AssertEquivalentToOracle(original);
            AssertEquivalentToOracle(fragment);
            AssertEquivalentToOracle(peer);

            original.DisposeResources();
            Object.DestroyImmediate(original.gameObject);
            Assert.That((bool)aggregate, Is.True, "Disposing the damaged source owner must not release the peer's old-generation aggregate.");
            AssertSameMeshes(fragmentMeshes, RenderMeshes(fragment));
            AssertEquivalentToOracle(fragment);

            peer.DisposeResources();
            Object.DestroyImmediate(peer.gameObject);
            Assert.That((bool)aggregate, Is.True, "The fragment still leases the old generation, so its aggregate must survive.");
            AssertSameMeshes(fragmentMeshes, RenderMeshes(fragment));

            newer.DisposeResources();
            Object.DestroyImmediate(newer.gameObject);
            AssertEquivalentToOracle(fragment);

            fragment.DisposeResources();
            Object.DestroyImmediate(fragment.gameObject);

            Assert.That(originalMeshes.Concat(fragmentMeshes).Concat(newerMeshes).Concat(new[] { aggregate }).All(mesh => !mesh), Is.True,
                "The final old-generation and new-generation owners must release every aggregate, damaged and shared chunk mesh.");
            Assert.That(originalMaterials.Union(fragmentMaterials).Union(peerMaterials).Union(newerMaterials).All(material => !material), Is.True,
                "Runtime tint materials must not leak across generations.");
            Assert.That((bool)database.Get(BlockShape.Cube).mesh, Is.True, "Generation churn must not destroy source module meshes.");
            Assert.That((bool)database.Get(BlockShape.Cube).material, Is.True, "Generation churn must not destroy source module materials.");
        }

        [Test]
        public void OldGenerationCanSplitAfterSourceShrinksBelowChunkThreshold()
        {
            var source = SplitGrid();
            source.baseColor = new Color(.8f, .9f, .7f);
            var original = Body(source);
            int originalGeneration = source.GeometryRevision;
            var originalPalette = RootMaterials(original).ToArray();
            var originalColor = originalPalette[0].GetColor("_BaseColor");

            // The live body still owns its 1025-cell generation; authoring a smaller source must not
            // make its future fragments use the new source's chunk eligibility or reject the old lease.
            source.cells.RemoveRange(0, source.cells.Count - 1);
            source.baseColor = new Color(.2f, .3f, .4f);
            source.NotifyChanged();
            Assert.That(source.GeometryRevision, Is.Not.EqualTo(originalGeneration));
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.False);

            Kill(original, Vector3Int.zero);

            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>();
            Assert.That(pieces.Length, Is.EqualTo(2));
            Assert.That(pieces.Sum(piece => piece.AliveCount), Is.EqualTo(1024));
            var fragment = pieces.Single(piece => piece != original);
            var fragmentPalette = RootMaterials(fragment);
            Assert.That(fragmentPalette[0], Is.Not.SameAs(originalPalette[0]),
                "A tinted fragment must own its old-generation material independently of the donor.");
            Assert.That(fragmentPalette[0].GetColor("_BaseColor"), Is.EqualTo(originalColor),
                "Editing the source tint must not recolor an old body when it fractures.");
            foreach (var piece in pieces)
            {
                Assert.That(piece.AliveCount, Is.EqualTo(MinimumChunkCells));
                Assert.That(piece.Body.isKinematic, Is.False);
                Assert.That(piece.Body.useGravity, Is.False);
                Assert.That(piece.ColliderCount, Is.GreaterThan(0));
                Assert.That(RenderMeshes(piece).Count, Is.GreaterThan(1),
                    "Both live islands must keep the retained old-generation chunk path.");
                AssertEquivalentToOracle(piece);
            }
            original.DisposeResources();
            Object.DestroyImmediate(original.gameObject);
            Assert.That((bool)fragmentPalette[0], Is.True, "The fragment's palette must survive donor disposal.");
            AssertEquivalentToOracle(fragment);
        }

        [Test]
        public void AuthoredBevelledCubeTrianglesSurviveIntactAndChunkedRendering()
        {
            var source = CubeBlock(8);
            var cube = database.Get(BlockShape.Cube).mesh;
            Assert.That((bool)cube, Is.True, "The real module database must provide the authored cube model.");
            var extent = cube.bounds.extents;
            Assert.That(extent.x, Is.LessThan(.5f), "The authored hull cube is bevelled inside its cell, so no face sits on a cell boundary.");
            Assert.That(extent.y, Is.LessThan(.5f));
            Assert.That(extent.z, Is.LessThan(.5f));
            var runtime = Body(source);
            Assert.That(RenderMeshes(runtime).Count, Is.EqualTo(1));

            AssertCubeCellsMatchAuthoredModels(runtime, source.cellSize,
                "The intact aggregate dropped authored bevelled cube triangles (seam cull).");
            AssertEquivalentToOracle(runtime);

            Kill(runtime, new Vector3Int(-ChunkEdge, -ChunkEdge, -ChunkEdge));

            Assert.That(RenderMeshes(runtime).Count, Is.GreaterThan(1), "The large cube hull must render through chunks after its first lethal hit.");
            AssertChunkBounds(RenderMeshes(runtime), source.cellSize);
            AssertCubeCellsMatchAuthoredModels(runtime, source.cellSize,
                "Chunked rendering dropped authored bevelled cube triangles (seam cull).");
            AssertEquivalentToOracle(runtime);
        }

        [TestCase(511, false)] [TestCase(512, true)]
        public void MinimumCellCountSeparatesChunkedAndSingleMeshRendering(int cells, bool chunked)
        {
            var source = CubeBlock(8);
            if (cells < source.cells.Count) source.cells.RemoveAll(cell => cell.position == new Vector3Int(-ChunkEdge, -ChunkEdge, -ChunkEdge));
            Assert.That(source.cells.Count, Is.EqualTo(cells));
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.EqualTo(chunked),
                "The 512-cell support threshold is part of the large-hull contract.");
            var runtime = Body(source);
            var meshes = RenderMeshes(runtime);
            Assert.That(meshes.Count, Is.EqualTo(1), "An intact instance always shows one aggregate mesh.");
            var aggregate = meshes[0];

            Kill(runtime, new Vector3Int(-ChunkEdge + 1, -ChunkEdge + 1, -ChunkEdge + 1));

            meshes = RenderMeshes(runtime);
            if (chunked)
            {
                Assert.That(meshes.Count, Is.GreaterThan(1), "A >=512 cell hull must activate prewarmed chunks on the first lethal hit.");
                AssertChunkBounds(meshes, source.cellSize);
            }
            else
            {
                Assert.That(meshes.Count, Is.EqualTo(1), "Below 512 cells the runtime must stay on the single rebuilt mesh path.");
                Assert.That(meshes[0], Is.Not.SameAs(aggregate), "The damaged single mesh is rebuilt, not shared.");
            }
            AssertEquivalentToOracle(runtime);
        }

        VoxelAssetDefinition NewSource()
        {
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
            owned.Add(source);
            source.hullModules = database;
            source.fragmentSeparationSpeed = 0;
            source.cellSize = 1.25f;
            source.surfaceColors.Add(new Color(.7f, .9f, 1));
            source.surfaceColors.Add(new Color(1, .7f, .8f));
            return source;
        }

        VoxelAssetDefinition Grid(int longAxis, bool mixed)
        {
            var source = NewSource();
            // 20 x 8 x 8 = 1280 cells: negative chunks, positive chunks, and a distant-hit control.
            for (int x = -10; x < 10; x++) for (int y = -4; y < 4; y++) for (int z = -4; z < 4; z++)
                {
                    var position = new Vector3Int(x, y, z);
                    if (longAxis == 1) position = new Vector3Int(y, x, z);
                    if (longAxis == 2) position = new Vector3Int(z, y, x);
                    int variant = PositiveMod(x + 3 * y + 7 * z, 3);
                    source.cells.Add(new VoxelCell
                    {
                        position = position,
                        shape = !mixed || variant == 0 ? BlockShape.Cube : variant == 1 ? BlockShape.Wedge : BlockShape.Slab,
                        quarterTurns = new Vector3Int(PositiveMod(x, 4), PositiveMod(y, 4), PositiveMod(z, 4)),
                        surface = PositiveMod(x + y + z, source.surfaceColors.Count)
                    });
                }
            Assert.That(source.cells.Count, Is.InRange(MinimumChunkCells, 4096));
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.True);
            return source;
        }

        VoxelAssetDefinition SplitGrid()
        {
            var source = NewSource();
            // Two 512-cell lobes joined ONLY by the cell at zero.
            for (int x = -8; x <= 8; x++)
            {
                if (x == 0) continue;
                for (int y = -4; y < 4; y++) for (int z = -4; z < 4; z++)
                    source.cells.Add(new VoxelCell { position = new Vector3Int(x, y, z), surface = PositiveMod(x + y + z, 3) });
            }
            source.cells.Add(new VoxelCell());
            Assert.That(source.cells.Count, Is.EqualTo(1025));
            Assert.That(VoxelTerrainRenderChunks.IsSupported(source), Is.True);
            return source;
        }

        VoxelAssetDefinition CubeBlock(int size)
        {
            var source = NewSource();
            source.fragmentSeparationSpeed = 0;
            int half = size / 2;
            var turns = new Vector3Int(1, 2, 3);
            for (int x = -half; x < size - half; x++)
                for (int y = -half; y < size - half; y++)
                    for (int z = -half; z < size - half; z++)
                        source.cells.Add(new VoxelCell
                        {
                            position = new Vector3Int(x, y, z),
                            shape = BlockShape.Cube,
                            quarterTurns = turns,
                            surface = PositiveMod(x + y + z, source.surfaceColors.Count)
                        });
            Assert.That(source.cells.Count, Is.EqualTo(size * size * size));
            return source;
        }

        List<VoxelCell> InsertBlock8AcrossBoundary(VoxelAssetDefinition source, int axis, int boundary, Vector3Int turns)
        {
            Assert.That(database.Get(BlockShape.Block8).occupiedCells.Length, Is.EqualTo(8), "The real Block8 fixture must occupy a complete 2x2x2 volume.");
            var piece = VoxelAuthoring.HullPiece(BlockShape.Block8, Vector3Int.zero, turns, 2, database);
            Assert.That(piece.Count, Is.EqualTo(8), "Block8 occupies all eight cells of a 2x2x2 volume, not a 2x1x2 footprint.");
            Assert.That(piece.Select(cell => cell.modelOffset).Distinct().Count(), Is.EqualTo(8));
            CollectionAssert.AreEquivalent(
                from x in Enumerable.Range(0, 2) from y in Enumerable.Range(0, 2) from z in Enumerable.Range(0, 2) select new Vector3Int(x, y, z),
                piece.Select(cell => cell.modelOffset));
            var shift = Vector3Int.zero;
            for (int component = 0; component < 3; component++)
                shift[component] = (component == axis ? boundary - 1 : -1) - piece.Min(cell => cell.position[component]);
            foreach (var cell in piece) cell.position += shift;
            var positions = new HashSet<Vector3Int>(piece.Select(cell => cell.position));
            Assert.That(source.cells.Count(cell => positions.Contains(cell.position)), Is.EqualTo(8));
            source.cells.RemoveAll(cell => positions.Contains(cell.position));
            source.cells.AddRange(piece);
            source.NotifyChanged();
            return piece;
        }

        VoxelStructureRuntime Body(VoxelAssetDefinition source)
        {
            Assert.That(source.Validate(out var error), Is.True, error);
            var go = new GameObject("Terrain render body");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.forward * (root.transform.childCount * 100);
            var runtime = go.AddComponent<VoxelStructureRuntime>();
            runtime.Initialize(source);
            Assert.That(runtime.AliveCount, Is.EqualTo(source.cells.Count));
            return runtime;
        }

        static int PositiveMod(int value, int modulus) => (value % modulus + modulus) % modulus;

        static void Kill(VoxelStructureRuntime runtime, Vector3Int position)
        {
            Assert.That(runtime.GetHealth(position), Is.GreaterThan(0), "The requested deletion must hit an alive occupied cell.");
            runtime.ApplyDamage(position, runtime.asset.blockHealth * runtime.healthMultiplier * 2);
            runtime.FlushMeshChanges();
            Assert.That(runtime.GetHealth(position), Is.Zero);
        }

        // Reflection keeps this test file compilable while the runtime diagnostics land.
        // Required contract: RenderMeshes : IReadOnlyList<Mesh>, RenderRevision, RenderMeshCount, TotalVertices.
        static object RenderDiagnostic(VoxelStructureRuntime runtime, string name)
        {
            var property = typeof(VoxelStructureRuntime).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, "VoxelStructureRuntime must expose public " + name + ".");
            return property.GetValue(runtime, null);
        }

        static IReadOnlyList<Mesh> RenderMeshes(VoxelStructureRuntime runtime)
        {
            var meshes = RenderDiagnostic(runtime, "RenderMeshes") as IReadOnlyList<Mesh>;
            Assert.That(meshes, Is.Not.Null, "RenderMeshes must implement IReadOnlyList<Mesh> and list only active geometry.");
            Assert.That(meshes.All(mesh => mesh), Is.True, "RenderMeshes contains a destroyed mesh.");
            Assert.That(Convert.ToInt64(RenderDiagnostic(runtime, "RenderMeshCount")), Is.EqualTo((long)meshes.Count));
            Assert.That(Convert.ToInt64(RenderDiagnostic(runtime, "TotalVertices")), Is.EqualTo(meshes.Sum(mesh => (long)mesh.vertexCount)));
            return meshes;
        }

        static long RenderRevision(VoxelStructureRuntime runtime) => Convert.ToInt64(RenderDiagnostic(runtime, "RenderRevision"));
        static Material[] RootMaterials(VoxelStructureRuntime runtime) => runtime.GetComponent<MeshRenderer>().sharedMaterials;

        static void AssertSameMeshes(IReadOnlyList<Mesh> expected, IReadOnlyList<Mesh> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int i = 0; i < expected.Count; i++) Assert.That(actual[i], Is.SameAs(expected[i]), "Render mesh identity/order changed at " + i + ".");
        }

        static Mesh ChunkMeshFor(IReadOnlyList<Mesh> meshes, Vector3Int cell, float size)
        {
            var point = (Vector3)cell * size;
            Mesh found = null;
            foreach (var mesh in meshes)
                if (mesh && mesh.bounds.Contains(point))
                {
                    Assert.That(found, Is.Null, "Two render chunks claim cell " + cell + ".");
                    found = mesh;
                }
            Assert.That(found, Is.Not.Null, "No active render chunk contains cell " + cell + ".");
            return found;
        }

        static void AssertChunkBounds(IReadOnlyList<Mesh> meshes, float size)
        {
            float limit = ChunkEdge * size + .0001f;
            foreach (var mesh in meshes)
            {
                var bounds = mesh.bounds;
                Assert.That(bounds.size.x, Is.GreaterThan(0f), "A render chunk must span at least one authored cell.");
                Assert.That(bounds.size.y, Is.GreaterThan(0f));
                Assert.That(bounds.size.z, Is.GreaterThan(0f));
                Assert.That(bounds.size.x, Is.LessThanOrEqualTo(limit), "A render chunk must stay inside four cells on every axis.");
                Assert.That(bounds.size.y, Is.LessThanOrEqualTo(limit));
                Assert.That(bounds.size.z, Is.LessThanOrEqualTo(limit));
            }
        }

        // Independent oracle: every authored triangle of every cell must be present with its rotated normal,
        // authored UV/tangent and material slot. The real hull models are bevelled inside their cells, so no
        // authored triangle lies on a cell boundary and none may be culled by occupancy.
        void AssertCubeCellsMatchAuthoredModels(VoxelStructureRuntime runtime, float size, string message)
        {
            var materials = RootMaterials(runtime);
            var model = database.Get(BlockShape.Cube).mesh;
            Assert.That((bool)model, Is.True, "The real module database must provide the authored cube model.");
            var expected = new Dictionary<TriangleKey, int>();
            foreach (var cell in runtime.Cells.Values.Where(state => state.Alive))
            {
                Assert.That(cell.shape, Is.EqualTo(BlockShape.Cube), "This independent oracle only covers single-cell cube models.");
                int slot = Mathf.Clamp(cell.surface, 0, materials.Length / VoxelHullAppearance.ShapeCount - 1) * VoxelHullAppearance.ShapeCount + VoxelHullAppearance.ShapeIndex(cell.shape);
                AddModelTriangles(expected, model, cell.position, cell.Rotation, size, slot, materials);
            }
            AssertTrianglesEqual(expected, Triangles(RenderMeshes(runtime), materials), message);
        }

        static void AddModelTriangles(Dictionary<TriangleKey, int> target, Mesh model, Vector3Int position, Quaternion rotation, float size, int slot, Material[] materials)
        {
            var vertices = model.vertices;
            var normals = model.normals;
            var uv = model.uv;
            var tangents = model.tangents;
            var indices = model.triangles;
            var corners = new VertexKey[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var tangent = i < tangents.Length ? tangents[i] : new Vector4(1, 0, 0, 1);
                var rotated = rotation * new Vector3(tangent.x, tangent.y, tangent.z);
                corners[i] = new VertexKey(
                    ((Vector3)position + rotation * vertices[i]) * size,
                    rotation * (i < normals.Length ? normals[i] : Vector3.up),
                    i < uv.Length ? uv[i] : Vector2.zero,
                    new Vector4(rotated.x, rotated.y, rotated.z, tangent.w));
            }
            int materialId = materials[slot] ? materials[slot].GetInstanceID() : 0;
            for (int triangle = 0; triangle < indices.Length; triangle += 3)
            {
                var key = new TriangleKey(slot, materialId, corners[indices[triangle]], corners[indices[triangle + 1]], corners[indices[triangle + 2]]);
                target.TryGetValue(key, out int count);
                target[key] = count + 1; // A multiset: accidental duplicate triangles must fail too.
            }
        }

        void AssertEquivalentToOracle(VoxelStructureRuntime runtime, Action<Mesh> inspectOracle = null)
        {
            var meshes = RenderMeshes(runtime);
            var materials = RootMaterials(runtime);
            // Keep the original, whole-state Build as the independent geometric oracle. No chunk helper calls here.
            var oracle = VoxelGeometry.Build(runtime.Cells, materials.Length, runtime.asset.cellSize, runtime.asset.hullModules);
            try
            {
                var active = runtime.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.GetComponentInParent<VoxelStructureRuntime>() == runtime)
                    .ToArray();
                Assert.That(active.Length, Is.EqualTo(meshes.Count), "Inactive aggregate geometry must not be counted or double-rendered.");
                foreach (var renderer in active)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    Assert.That(filter, Is.Not.Null);
                    Assert.That(meshes.Count(mesh => ReferenceEquals(mesh, filter.sharedMesh)), Is.EqualTo(1));
                    var rendererMaterials = renderer.sharedMaterials;
                    Assert.That(rendererMaterials.Length, Is.EqualTo(materials.Length));
                    for (int slot = 0; slot < materials.Length; slot++) Assert.That(rendererMaterials[slot], Is.SameAs(materials[slot]), "Chunk material/surface slot " + slot + " changed.");
                    var local = runtime.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                            Assert.That(local[row, column], Is.EqualTo(Matrix4x4.identity[row, column]).Within(.00001f), "Chunk vertices must stay in asset-local coordinates.");
                }
                AssertTrianglesEqual(Triangles(new[] { oracle }, materials), Triangles(meshes, materials), "Active chunk aggregate differs from fresh full Build.");
                inspectOracle?.Invoke(oracle);
            }
            finally { Object.DestroyImmediate(oracle); }
        }

        static void AssertExposedBlock8Cap(Mesh oracle, VoxelStructureRuntime runtime, List<VoxelCell> piece, Vector3Int removed)
        {
            int surface = piece[0].surface;
            int slot = surface * VoxelHullAppearance.ShapeCount + VoxelHullAppearance.ShapeIndex(BlockShape.Block8);
            var vertices = oracle.vertices;
            var normals = oracle.normals;
            var indices = oracle.GetTriangles(slot);
            Assert.That(indices.Length, Is.GreaterThan(0), "The exposed Block8 partition cap must exist in the Block8 material slot.");
            int faces = 0;
            foreach (var survivor in piece.Where(cell => cell.position != removed && (cell.position - removed).sqrMagnitude == 1))
            {
                faces++;
                var direction = (Vector3)(removed - survivor.position);
                float plane = Vector3.Dot(((Vector3)survivor.position + direction * .5f) * runtime.asset.cellSize, direction);
                int caps = 0;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    bool cap = true;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int index = indices[i + corner];
                        cap &= Mathf.Abs(Vector3.Dot(vertices[index], direction) - plane) < .0001f && Vector3.Dot(normals[index], direction) > .99f;
                    }
                    if (cap) caps++;
                }
                Assert.That(caps, Is.GreaterThan(0), "The removed Block8 cell must expose a partition cap toward its surviving face neighbour " + survivor.position + ".");
            }
            Assert.That(faces, Is.EqualTo(3), "Every cell of a 2x2x2 Block8 is a corner, so exactly three surviving face neighbours must expose caps.");
        }

        static Dictionary<TriangleKey, int> Triangles(IEnumerable<Mesh> meshes, Material[] materials)
        {
            var result = new Dictionary<TriangleKey, int>();
            foreach (var mesh in meshes)
            {
                Assert.That((bool)mesh, Is.True);
                var positions = mesh.vertices;
                var normals = mesh.normals;
                var uv = mesh.uv;
                var tangents = mesh.tangents;
                Assert.That(normals.Length, Is.EqualTo(positions.Length), "Authored normals must be present, not regenerated.");
                Assert.That(uv.Length, Is.EqualTo(positions.Length));
                Assert.That(tangents.Length, Is.EqualTo(positions.Length), "Authored/partition tangents must be retained.");
                Assert.That(mesh.subMeshCount, Is.EqualTo(materials.Length), "Every chunk must preserve the original surface/shape slot mapping.");
                var corners = new VertexKey[positions.Length];
                for (int i = 0; i < corners.Length; i++) corners[i] = new VertexKey(positions[i], normals[i], uv[i], tangents[i]);
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    var indices = mesh.GetTriangles(slot);
                    Assert.That(indices.Length % 3, Is.Zero);
                    int materialId = materials[slot] ? materials[slot].GetInstanceID() : 0;
                    for (int triangle = 0; triangle < indices.Length; triangle += 3)
                    {
                        var key = new TriangleKey(slot, materialId, corners[indices[triangle]], corners[indices[triangle + 1]], corners[indices[triangle + 2]]);
                        result.TryGetValue(key, out int count);
                        result[key] = count + 1; // A multiset: accidental duplicate triangles must fail too.
                    }
                }
            }
            return result;
        }

        static void AssertTrianglesEqual(Dictionary<TriangleKey, int> expected, Dictionary<TriangleKey, int> actual, string message)
        {
            Assert.That(actual.Values.Sum(), Is.EqualTo(expected.Values.Sum()), message + " Triangle multiplicity differs.");
            Assert.That(actual.Count, Is.EqualTo(expected.Count), message + " Unique triangle count differs.");
            foreach (var pair in expected)
                if (!actual.TryGetValue(pair.Key, out int count) || count != pair.Value)
                    Assert.Fail(message + " Missing/changed oriented triangle in material slot " + pair.Key.Slot + "; expected multiplicity " + pair.Value + ", actual " + count + ".");
        }

        static int Q(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) Assert.Fail("Mesh attributes must be finite.");
            return checked((int)Math.Round(value * Quantization, MidpointRounding.AwayFromZero));
        }

        readonly struct VertexKey : IEquatable<VertexKey>, IComparable<VertexKey>
        {
            readonly Vector3Int position, normal;
            readonly Vector2Int uv;
            readonly int tx, ty, tz, tw;
            public VertexKey(Vector3 p, Vector3 n, Vector2 u, Vector4 t)
            {
                position = new Vector3Int(Q(p.x), Q(p.y), Q(p.z));
                normal = new Vector3Int(Q(n.x), Q(n.y), Q(n.z));
                uv = new Vector2Int(Q(u.x), Q(u.y));
                tx = Q(t.x); ty = Q(t.y); tz = Q(t.z); tw = Q(t.w);
            }
            public int CompareTo(VertexKey other)
            {
                int compare = position.x.CompareTo(other.position.x); if (compare != 0) return compare;
                compare = position.y.CompareTo(other.position.y); if (compare != 0) return compare;
                compare = position.z.CompareTo(other.position.z); if (compare != 0) return compare;
                compare = normal.x.CompareTo(other.normal.x); if (compare != 0) return compare;
                compare = normal.y.CompareTo(other.normal.y); if (compare != 0) return compare;
                compare = normal.z.CompareTo(other.normal.z); if (compare != 0) return compare;
                compare = uv.x.CompareTo(other.uv.x); if (compare != 0) return compare;
                compare = uv.y.CompareTo(other.uv.y); if (compare != 0) return compare;
                compare = tx.CompareTo(other.tx); if (compare != 0) return compare;
                compare = ty.CompareTo(other.ty); if (compare != 0) return compare;
                compare = tz.CompareTo(other.tz); return compare != 0 ? compare : tw.CompareTo(other.tw);
            }
            public bool Equals(VertexKey other) => CompareTo(other) == 0;
            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = position.GetHashCode(); hash = hash * 397 ^ normal.GetHashCode(); hash = hash * 397 ^ uv.GetHashCode();
                    hash = hash * 397 ^ tx; hash = hash * 397 ^ ty; hash = hash * 397 ^ tz; return hash * 397 ^ tw;
                }
            }
        }

        readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            public readonly int Slot;
            readonly int materialId;
            readonly VertexKey a, b, c;
            public TriangleKey(int slot, int material, VertexKey x, VertexKey y, VertexKey z)
            {
                Slot = slot; materialId = material;
                a = x; b = y; c = z;
                // Only cyclic permutations are equivalent. Reversed winding is deliberately NOT equivalent.
                if (Compare(y, z, x, a, b, c) < 0) { a = y; b = z; c = x; }
                if (Compare(z, x, y, a, b, c) < 0) { a = z; b = x; c = y; }
            }
            static int Compare(VertexKey a, VertexKey b, VertexKey c, VertexKey x, VertexKey y, VertexKey z)
            {
                int compare = a.CompareTo(x); if (compare != 0) return compare;
                compare = b.CompareTo(y); return compare != 0 ? compare : c.CompareTo(z);
            }
            public bool Equals(TriangleKey other) => Slot == other.Slot && materialId == other.materialId && a.Equals(other.a) && b.Equals(other.b) && c.Equals(other.c);
            public override bool Equals(object obj) => obj is TriangleKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { int hash = Slot * 397 ^ materialId; hash = hash * 397 ^ a.GetHashCode(); hash = hash * 397 ^ b.GetHashCode(); return hash * 397 ^ c.GetHashCode(); }
            }
        }
    }
}
