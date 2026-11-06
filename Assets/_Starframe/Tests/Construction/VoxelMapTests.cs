using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class VoxelMapTests
    {
        GameObject root;
        readonly List<Object> owned = new List<Object>();
        [SetUp] public void Setup() { root = new GameObject("Voxel map fixture"); root.transform.position = new Vector3(2000, 20000, 0); }
        [TearDown] public void Cleanup()
        {
            if (root) Object.DestroyImmediate(root);
            VoxelTemplateCache.Clear();
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]); owned.Clear();
            BlockModules.Configure(null);
        }
        VoxelAssetDefinition Asset(params Vector3Int[] cells)
        {
            var asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); owned.Add(asset);
            foreach (var cell in cells) asset.cells.Add(new VoxelCell { position = cell }); return asset;
        }
        VoxelStructureRuntime Body(VoxelAssetDefinition asset, Vector3? offset = null)
        {
            var go = new GameObject("Floating voxel fixture"); go.transform.SetParent(root.transform, false); go.transform.localPosition = offset ?? Vector3.zero;
            var body = go.AddComponent<VoxelStructureRuntime>(); body.Initialize(asset); Physics.SyncTransforms(); return body;
        }




        [Test] public void SolidBoxUsesOneDynamicBodyAndOneExactCompoundBox()
        {
            var asset = Asset(); asset.cells = VoxelAuthoring.Stamp(VoxelStampShape.Box, new Vector3Int(4, 3, 2), Vector3Int.zero, 0, 0, false, 0);
            var body = Body(asset);
            Assert.That(body.Body.isKinematic, Is.False); Assert.That(body.Body.useGravity, Is.False);
            Assert.That(body.ColliderCount, Is.EqualTo(1));
            Assert.That(body.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(4, 3, 2)));
            Assert.That(body.Body.mass, Is.EqualTo(2400).Within(.01f));
            Assert.That(body.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(208), "Hidden internal faces must be absent.");
        }
        [Test] public void CollisionMergingDoesNotFillEmptyHoles()
        {
            var body = Body(Asset(Vector3Int.zero, Vector3Int.right * 2));
            Assert.That(body.ColliderCount, Is.EqualTo(2));
            var start = root.transform.position + new Vector3(1, 0, -5);
            Assert.That(Physics.Raycast(start, Vector3.forward, 10), Is.False);
        }



        [Test] public void GeneratorIsDeterministicAndMapCopyGetsIndependentIdentity()
        {
            var a = VoxelAuthoring.Stamp(VoxelStampShape.Asteroid, new Vector3Int(10, 8, 9), Vector3Int.zero, 42, .5f, false, 0);
            var b = VoxelAuthoring.Stamp(VoxelStampShape.Asteroid, new Vector3Int(10, 8, 9), Vector3Int.zero, 42, .5f, false, 0);
            CollectionAssert.AreEqual(a.Select(c => c.position), b.Select(c => c.position));
            var item = new VoxelMapInstance { asset = Asset(Vector3Int.zero) }; var copy = item.Copy();
            Assert.That(copy.id, Is.Not.EqualTo(item.id)); Assert.That(copy.asset, Is.SameAs(item.asset));
        }
        [Test] public void DuplicateCellsAndMissingMapAssetsAreRejected()
        {
            var asset = Asset(Vector3Int.zero, Vector3Int.zero); Assert.That(asset.Validate(out _), Is.False);
            var map = ScriptableObject.CreateInstance<SpaceMapDefinition>(); owned.Add(map); map.instances.Add(new VoxelMapInstance());
            Assert.That(map.Validate(out _), Is.False);
        }
        [Test] public void PickUsesRotatedScaledAssetSpace()
        {
            var body = Body(Asset(Vector3Int.zero)); body.transform.localRotation = Quaternion.Euler(20, 50, 10); body.transform.localScale = Vector3.one * 3;
            var center = body.transform.position;
            var ray = new Ray(center - body.transform.forward * 20, body.transform.forward);
            Assert.That(VoxelAuthoring.Pick(body, ray, out var cell, out _, out float distance), Is.True);
            Assert.That(cell, Is.EqualTo(Vector3Int.zero)); Assert.That(distance, Is.EqualTo(18.5f).Within(.01f));
        }
    }
}
