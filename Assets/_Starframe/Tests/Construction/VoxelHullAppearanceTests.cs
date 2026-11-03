using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class VoxelHullAppearanceTests
    {
        readonly List<Object> owned = new List<Object>();
        GameObject root;
        ModuleDatabase Database => AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        [SetUp] public void Setup() { root = new GameObject("Hull scenery fixture"); root.transform.position = Vector3.up * 30000; }
        [TearDown] public void Cleanup()
        {
            foreach (var body in root.GetComponentsInChildren<VoxelStructureRuntime>()) body.DisposeResources();
            Object.DestroyImmediate(root); VoxelTemplateCache.Clear();
            foreach (var item in owned) if (item) Object.DestroyImmediate(item); owned.Clear();
        }
        VoxelAssetDefinition Source(params VoxelCell[] cells)
        {
            var a = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); owned.Add(a); a.hullModules = Database; a.cells.AddRange(cells); return a;
        }
        VoxelStructureRuntime Body(VoxelAssetDefinition a)
        {
            var go = new GameObject("Hull scenery"); go.transform.SetParent(root.transform, false);
            var body = go.AddComponent<VoxelStructureRuntime>(); body.Initialize(a); return body;
        }
        [TestCase(BlockShape.Cube)] [TestCase(BlockShape.Wedge)] [TestCase(BlockShape.Slab)] [TestCase(BlockShape.Pyramid)]
        public void IsolatedHullCellUsesTheExactShipModelNormalsAndMaterial(BlockShape shape)
        {
            var cell = new VoxelCell { position = new Vector3Int(3, 2, -1), shape = shape, quarterTurns = new Vector3Int(1, 2, 1) };
            var source = Source(cell); var body = Body(source); var ship = Database.Get(shape); var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(ship.mesh.vertexCount));
            var vertices = ship.mesh.vertices.Select(p => (Vector3)cell.position + cell.Rotation * p).ToArray();
            CollectionAssert.AreEquivalent(vertices, mesh.vertices);
            CollectionAssert.AreEquivalent(ship.mesh.normals.Select(n => cell.Rotation * n).ToArray(), mesh.normals);
            Assert.That(body.GetComponent<MeshRenderer>().sharedMaterials[VoxelHullAppearance.ShapeIndex(shape)], Is.SameAs(ship.material));
            Assert.That(body.GetComponent<BoxCollider>().size, Is.EqualTo(Vector3.one));
            Assert.That(body.GetComponent<BoxCollider>().center, Is.EqualTo((Vector3)cell.position));
        }
        [Test] public void ColourChannelsCloneOnlyTintedMaterialsAndNeverChangeShipMaterials()
        {
            var source = Source(new VoxelCell()); source.surfaceColors.Add(Color.white);
            var original = Database.Get(BlockShape.Cube).material; var color = original.GetColor("_BaseColor");
            source.baseColor = new Color(.5f, .8f, .9f); source.surfaceColors[1] = new Color(.7f, .6f, .5f);
            var materials = new List<Material>(); var result = VoxelHullAppearance.Materials(source, materials); owned.AddRange(materials);
            Assert.That(result[0], Is.Not.SameAs(original)); Assert.That(result[0].shader, Is.SameAs(original.shader));
            Assert.That(Vector4.Distance(result[VoxelHullAppearance.ShapeCount].GetColor("_BaseColor"), color * source.baseColor * source.surfaceColors[1]), Is.LessThan(.000001f));
            Assert.That(result[0].GetFloat("_Smoothness"), Is.EqualTo(original.GetFloat("_Smoothness")));
            Assert.That(result[0].GetFloat("_ClearCoatMask"), Is.EqualTo(original.GetFloat("_ClearCoatMask")));
            Assert.That(original.GetColor("_BaseColor"), Is.EqualTo(color));
        }
        [Test] public void MixedShapesKeepOneCompleteCellCollisionAndOneBodyPerAsset()
        {
            var source = Source(VoxelHullAppearance.Shapes.Select((shape, i) => new VoxelCell { shape = shape, position = Vector3Int.right * i, quarterTurns = new Vector3Int(i, i, 0) }).ToArray());
            var body = Body(source); Assert.That(body.ColliderCount, Is.EqualTo(1)); Assert.That(body.Body.mass, Is.EqualTo(100 * VoxelHullAppearance.ShapeCount));
            Assert.That(body.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(VoxelHullAppearance.ShapeCount, 1, 1)));
            Assert.That(body.GetComponents<Rigidbody>().Length, Is.EqualTo(1)); Assert.That(body.Body.useGravity, Is.False);
        }
        [Test] public void HalfHeightTopIsStillVisibleWhenTheCellAboveContainsACube()
        {
            var body = Body(Source(new VoxelCell { shape = BlockShape.Slab }, new VoxelCell { position = Vector3Int.up }));
            var mesh = body.GetComponent<MeshFilter>().sharedMesh; var vertices = mesh.vertices; var normals = mesh.normals;
            var top = mesh.GetTriangles(2).Where(i => normals[i].y > .99f && Mathf.Abs(vertices[i].y) < .05f).ToArray();
            Assert.That(top.Length, Is.GreaterThanOrEqualTo(3), "The slab's internal-height top must not be culled by a neighbouring cube.");
        }
        [Test] public void FractureRetainsEachSurvivorsShapeOrientationAndTint()
        {
            var source = Source(new VoxelCell { position = Vector3Int.left, shape = BlockShape.Wedge, quarterTurns = new Vector3Int(1, 0, 2) }, new VoxelCell(),
                new VoxelCell { position = Vector3Int.right, shape = BlockShape.Pyramid, quarterTurns = new Vector3Int(2, 1, 0) });
            source.baseColor = new Color(.8f, .9f, 1); var body = Body(source); body.ApplyDamage(Vector3Int.zero, 1000); body.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(); Assert.That(pieces.Length, Is.EqualTo(2));
            foreach (var item in pieces)
            {
                var state = item.Cells.Values.Single(c => c.Alive); var expected = source.cells.Single(c => c.position == state.position);
                Assert.That(state.shape, Is.EqualTo(expected.shape)); Assert.That(state.quarterTurns, Is.EqualTo(expected.quarterTurns));
                Assert.That(item.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
                var original = Database.Get(state.shape).material;
                Assert.That(Vector4.Distance(item.GetComponent<MeshRenderer>().sharedMaterials[VoxelHullAppearance.ShapeIndex(state.shape)].GetColor("_BaseColor"), original.GetColor("_BaseColor") * source.baseColor), Is.LessThan(.000001f));
            }
        }
        [Test] public void ShapeAndRotationSurviveCopiesSerializationAndFallbackRendering()
        {
            var cell = new VoxelCell { shape = BlockShape.Wedge, quarterTurns = new Vector3Int(2, 3, 1), surface = 1 };
            var copy = JsonUtility.FromJson<VoxelCell>(JsonUtility.ToJson(cell.Copy()));
            Assert.That(copy.shape, Is.EqualTo(cell.shape)); Assert.That(copy.quarterTurns, Is.EqualTo(cell.quarterTurns));
            var source = Source(copy); source.hullModules = null; var body = Body(source);
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
            Assert.That(body.GetComponent<BoxCollider>().size, Is.EqualTo(Vector3.one));
        }
        [Test] public void AdjacentBevelledModelsKeepTheFacesVisibleThroughTheirRealSeamGap()
        {
            var model = Database.Get(BlockShape.Cube).mesh;
            Assert.That(model.bounds.extents.x, Is.LessThan(.5f));
            var body = Body(Source(new VoxelCell(), new VoxelCell { position = Vector3Int.right }));
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.GetIndexCount(0), Is.EqualTo(model.GetIndexCount(0) * 2), "Full-cell collision must not cause visual faces across a real gap to disappear.");
            var vertices = mesh.vertices; var normals = mesh.normals; var triangles = mesh.GetTriangles(0);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
                Assert.That(Vector3.Dot(n, (normals[a] + normals[b] + normals[c]).normalized), Is.GreaterThan(.9f));
            }
        }
        [Test] public void DenseBevelledModelsRetainClosedCellsBehindSurfaceSeams()
        {
            var source = Source(); source.cells = VoxelAuthoring.Stamp(VoxelStampShape.Box, new Vector3Int(3, 3, 3), Vector3Int.zero, 0, 0, false, 0);
            var body = Body(source); var model = Database.Get(BlockShape.Cube).mesh;
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0), Is.EqualTo(model.GetIndexCount(0) * 27));
            Assert.That(body.ColliderCount, Is.EqualTo(1), "Keeping visual seams closed must not create per-block physics bodies.");
        }
    }
}
