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
        [Test] public void OnlyRecentlyHitCellsHaveHealthRecordsAndResetIsInstant()
        {
            var body = Body(Asset(Enumerable.Range(0, 64).Select(i => Vector3Int.right * i).ToArray()));
            Assert.That(body.RecentDamageCount, Is.Zero);
            body.ApplyDamage(Vector3Int.zero, 30); Assert.That(body.GetHealth(Vector3Int.zero), Is.EqualTo(70));
            Assert.That(body.RecentDamageCount, Is.EqualTo(1));
            body.AdvanceDamageTime(1); Assert.That(body.GetHealth(Vector3Int.zero), Is.EqualTo(70), "There is no gradual healing.");
            body.AdvanceDamageTime(1.1); Assert.That(body.GetHealth(Vector3Int.zero), Is.EqualTo(100));
            Assert.That(body.RecentDamageCount, Is.Zero);
        }
        [Test] public void EachHitRefreshesItsOwnResetDeadline()
        {
            var body = Body(Asset(Vector3Int.zero)); body.ApplyDamage(Vector3Int.zero, 20); body.AdvanceDamageTime(1.2);
            body.ApplyDamage(Vector3Int.zero, 20); body.AdvanceDamageTime(1.2);
            Assert.That(body.GetHealth(Vector3Int.zero), Is.EqualTo(60));
            body.AdvanceDamageTime(1); Assert.That(body.GetHealth(Vector3Int.zero), Is.EqualTo(100));
        }
        [Test] public void DestroyedCellsNeverHealAndSourceAssetIsImmutable()
        {
            var asset = Asset(Vector3Int.zero, Vector3Int.right); string before = JsonUtility.ToJson(asset);
            var body = Body(asset); body.ApplyDamage(Vector3Int.zero, 1000); body.FlushMeshChanges(); body.AdvanceDamageTime(1000);
            Assert.That(body.GetHealth(Vector3Int.zero), Is.Zero); Assert.That(body.AliveCount, Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(asset), Is.EqualTo(before));
        }
        [Test] public void IntactInstancesShareMeshButDamageOnlyChangesOneInstance()
        {
            var asset = Asset(Vector3Int.zero, Vector3Int.right, Vector3Int.right * 2);
            var a = Body(asset); var b = Body(asset, Vector3.forward * 10);
            var shared = b.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(a.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(shared));
            a.ApplyDamage(Vector3Int.right * 2, 100); a.FlushMeshChanges();
            Assert.That(a.GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs(shared));
            Assert.That(b.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(shared)); Assert.That(b.AliveCount, Is.EqualTo(3));
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
        [Test] public void DestroyingBridgeCreatesIndependentCollidableFragmentsWithoutRemovingSurvivors()
        {
            var asset = Asset(Vector3Int.zero, Vector3Int.right, Vector3Int.right * 2); asset.fragmentSeparationSpeed = 0;
            var body = Body(asset); body.Body.linearVelocity = new Vector3(3, 2, 4); body.Body.angularVelocity = Vector3.up;
            Vector3 before = body.CellMatrix(Vector3Int.right * 2).MultiplyPoint3x4(Vector3.zero);
            body.ApplyDamage(Vector3Int.right, 100); body.FlushMeshChanges(); Physics.SyncTransforms();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>(); Assert.That(pieces.Length, Is.EqualTo(2));
            Assert.That(pieces.Sum(p => p.AliveCount), Is.EqualTo(2));
            foreach (var piece in pieces) { Assert.That(piece.Body.isKinematic || piece.Body.useGravity, Is.False); Assert.That(piece.ColliderCount, Is.EqualTo(1)); }
            var right = pieces.Single(p => p.Cells.ContainsKey(Vector3Int.right * 2));
            Assert.That(Vector3.Distance(before, right.CellMatrix(Vector3Int.right * 2).MultiplyPoint3x4(Vector3.zero)), Is.LessThan(.001f));
            Assert.That(right.Cells[Vector3Int.right * 2].owner, Is.SameAs(right));
        }
        [Test] public void FragmentKeepsItsRecentDamageAndCanStillBeDestroyed()
        {
            var body = Body(Asset(Enumerable.Range(0, 5).Select(i => Vector3Int.right * i).ToArray()));
            body.ApplyDamage(Vector3Int.right * 4, 25); body.ApplyDamage(Vector3Int.right * 2, 100); body.FlushMeshChanges();
            var right = root.GetComponentsInChildren<VoxelStructureRuntime>().Single(p => p.Cells.ContainsKey(Vector3Int.right * 4));
            Assert.That(right.GetHealth(Vector3Int.right * 4), Is.EqualTo(75));
            Assert.That(right.ApplyDamage(Vector3Int.right * 4, 100), Is.EqualTo(75)); right.FlushMeshChanges();
            Assert.That(root.GetComponentsInChildren<VoxelStructureRuntime>().Sum(p => p.AliveCount), Is.EqualTo(3));
        }
        [Test] public void ExistingLaserPenetratesTwoMapCellsAndConfirmsOnlyOnce()
        {
            var target = Body(Asset(Vector3Int.zero, Vector3Int.forward, Vector3Int.forward * 2));
            var tuning = AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            var go = new GameObject("Shooter"); go.transform.SetParent(root.transform, false); go.transform.localPosition = Vector3.right * 100;
            var assembly = go.AddComponent<ShipAssembly>(); assembly.Configure(tuning.database);
            assembly.Place(BlockShape.Core, new Vector3(0, .5f, 0), Quaternion.identity);
            var shooter = go.AddComponent<FlightShipRuntime>(); shooter.Initialize(assembly, assembly.Export("shooter"), tuning);
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>(); owned.Add(weapon);
            weapon.damage = 40; weapon.projectileSpeed = 100; weapon.projectileDiameter = .05f; weapon.penetrationDepth = 2; weapon.secondLayerDamageMultiplier = .5f; weapon.range = 100;
            var world = new ProjectileWorld(); int hits = 0; world.HitConfirmed += _ => hits++;
            int sceneryHits=0;world.HitFeedbackConfirmed+=(owner,scenery)=>{Assert.That(owner,Is.SameAs(shooter));Assert.That(scenery,Is.True);sceneryHits++;};
            Physics.SyncTransforms(); world.Fire(shooter, weapon, root.transform.position - Vector3.forward * 5, Vector3.forward); world.Step(.2f);
            Assert.That(target.GetHealth(Vector3Int.zero), Is.EqualTo(60).Within(.01f));
            Assert.That(sceneryHits,Is.EqualTo(1),"A terrain bolt must request the subtle white feedback only once.");
            Assert.That(target.GetHealth(Vector3Int.forward), Is.EqualTo(80).Within(.01f));
            Assert.That(target.GetHealth(Vector3Int.forward * 2), Is.EqualTo(100).Within(.01f));
            Assert.That(shooter.DamageDealt, Is.EqualTo(60).Within(.01f)); Assert.That(hits, Is.EqualTo(1)); world.Clear();
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
