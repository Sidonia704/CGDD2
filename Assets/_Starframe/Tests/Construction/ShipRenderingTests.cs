using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class ShipRenderingTests
    {
        GameObject root;
        ShipAssembly assembly;
        FlightShipRuntime ship;
        ShipInstanceRenderer instances;

        [SetUp] public void SetUp()
        {
            if (!SystemInfo.supportsInstancing) Assert.Ignore("GPU instancing unavailable on this test device.");
            var tuning = AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            root = new GameObject("Instanced damage fixture");
            assembly = root.AddComponent<ShipAssembly>(); assembly.Configure(tuning.database);
            var data = new BlueprintData { name = "128 cell hull", parts = new List<BlockRecord>() };
            for (int y = 0; y < 2; y++) for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
                data.parts.Add(new BlockRecord { id = $"{x}-{y}-{z}", shape = data.parts.Count == 0 ? BlockShape.Core : BlockShape.Cube,
                    position = new Vector3(x, y + .5f, z), rotation = Quaternion.identity });
            assembly.Import(data);
            ship = root.AddComponent<FlightShipRuntime>(); ship.Initialize(assembly, data, tuning);
            instances = root.GetComponent<ShipInstanceRenderer>();
        }

        [TearDown] public void TearDown()
        {
            if (root) Object.DestroyImmediate(root);
            BlockModules.Configure(null);
        }

        void Draw() => typeof(ShipInstanceRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instances, null);

        [Test] public void BatchingPreservesIndividualFullCellCollisionAndSharedAssets()
        {
            Assert.That(instances.BatchedRendererCount, Is.EqualTo(127));
            Draw(); Assert.That(instances.VisibleInstanceCount, Is.EqualTo(127));
            Assert.That(instances.SubmittedBatchCount, Is.EqualTo(1));
            foreach (var part in ship.Parts)
            {
                var hit = part.view.GetComponent<ShipPartHitbox>();
                var collider = part.view.GetComponent<MeshCollider>();
                Assert.That(hit.Part, Is.SameAs(part));
                Assert.That(collider.enabled && collider.convex, Is.True);
                Assert.That(collider.sharedMesh.bounds.size, Is.EqualTo(Vector3.one));
                Assert.That(AssetDatabase.Contains(part.view.GetComponent<MeshRenderer>().sharedMaterial), Is.True);
            }
        }

        [Test] public void DamageAndRespawnChangeInstancesWithoutReplacingHitboxes()
        {
            var corner = ship.Parts.Find(p => p.record.id == "7-1-7");
            var original = corner.view.GetComponent<Collider>();
            ship.ApplyDamage(corner.record.id, corner.maxHp); Draw();
            Assert.That(instances.VisibleInstanceCount, Is.EqualTo(126));
            Assert.That(corner.view.activeSelf, Is.False);
            ship.Respawn(); Draw();
            Assert.That(instances.VisibleInstanceCount, Is.EqualTo(127));
            Assert.That(corner.view.GetComponent<Collider>(), Is.SameAs(original));
            Assert.That(corner.view.GetComponent<MeshRenderer>().enabled, Is.False);
            Assert.That(corner.hp, Is.EqualTo(corner.maxHp));
        }

        [Test] public void DamagedHealthRevisionAdvancesDuringHealingButNotIdleSteps()
        {
            var corner = ship.Parts.Find(p => p.record.id == "7-1-7");
            int before = ship.HealthRevision;
            ship.BeginStep(1); ship.AdvanceRecovery(1); Assert.That(ship.HealthRevision, Is.EqualTo(before));
            ship.ApplyDamage(corner.record.id, 10); int damaged = ship.HealthRevision;
            Assert.That(damaged, Is.GreaterThan(before));
            ship.BeginStep(2); ship.AdvanceRecovery(1);
            Assert.That(ship.HealthRevision, Is.GreaterThan(damaged));
        }
    }
}
