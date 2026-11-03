using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Starframe.Tests
{
    public sealed class VoxelPhysicsTests
    {
        Scene scene;
        PhysicsScene physics;
        readonly List<VoxelAssetDefinition> assets = new List<VoxelAssetDefinition>();
        [UnitySetUp] public IEnumerator Setup()
        {
            scene = SceneManager.CreateScene("Voxel isolated physics", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physics = scene.GetPhysicsScene(); yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            yield return SceneManager.UnloadSceneAsync(scene);
            VoxelTemplateCache.Clear(); foreach (var asset in assets) if (asset) Object.Destroy(asset); assets.Clear();
        }
        VoxelStructureRuntime Body(Vector3 position, params Vector3Int[] cells)
        {
            var asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); assets.Add(asset);
            foreach (var cell in cells) asset.cells.Add(new VoxelCell { position = cell });
            var go = new GameObject("Floating physics asset"); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = position;
            var body = go.AddComponent<VoxelStructureRuntime>(); body.Initialize(asset); return body;
        }
        [UnityTest] public IEnumerator CollisionPushesAnIntactAssetWithoutGravityOrCellLoss()
        {
            var a = Body(Vector3.left * 3, Vector3Int.zero); var b = Body(Vector3.zero, Vector3Int.zero, Vector3Int.forward);
            a.Body.linearVelocity = Vector3.right * 8;
            for (int i = 0; i < 90; i++) { physics.Simulate(.02f); if (i % 15 == 0) yield return null; }
            Assert.That(b.Body.position.x, Is.GreaterThan(.5f)); Assert.That(Mathf.Abs(b.Body.position.y), Is.LessThan(.05f));
            Assert.That(b.AliveCount, Is.EqualTo(2)); Assert.That(b.GetComponents<Rigidbody>().Length, Is.EqualTo(1));
            Assert.That(a.Body.useGravity || b.Body.useGravity, Is.False);
        }
        [UnityTest] public IEnumerator BreakingBridgeMakesSurvivingBodiesDriftIndependentlyAndRemainDamageable()
        {
            var body = Body(Vector3.zero, Vector3Int.left, Vector3Int.zero, Vector3Int.right);
            body.ApplyDamage(Vector3Int.zero, 1000); body.FlushMeshChanges(); yield return null;
            var pieces = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<VoxelStructureRuntime>()).ToArray();
            Assert.That(pieces.Length, Is.EqualTo(2)); Assert.That(pieces.Sum(p => p.AliveCount), Is.EqualTo(2));
            float initialDistance = Vector3.Distance(pieces[0].Body.worldCenterOfMass, pieces[1].Body.worldCenterOfMass);
            for (int i = 0; i < 50; i++) physics.Simulate(.02f);
            Assert.That(Vector3.Distance(pieces[0].Body.worldCenterOfMass, pieces[1].Body.worldCenterOfMass), Is.GreaterThan(initialDistance + .1f));
            var victim = pieces[0]; var cell = victim.Cells.First(pair => pair.Value.Alive).Key;
            victim.ApplyDamage(cell, 1000); victim.FlushMeshChanges();
            Assert.That(pieces.Sum(p => p.AliveCount), Is.EqualTo(1)); Assert.That(pieces[1].ColliderCount, Is.GreaterThan(0));
            yield return null;
        }
        [UnityTest] public IEnumerator EditorPreviewNeverCreatesAnActivePhysicsBodyOrCollider()
        {
            var asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); assets.Add(asset); asset.cells.Add(new VoxelCell());
            var go = new GameObject("Nonphysical editor preview"); SceneManager.MoveGameObjectToScene(go, scene);
            var preview = go.AddComponent<VoxelStructureRuntime>(); preview.Initialize(asset, true); yield return null;
            Assert.That(go.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(go.GetComponents<Collider>().All(c => !c.enabled), Is.True);
        }
    }
}
