using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Starframe.Tests
{
    public sealed class ShipRenderingLifecycleTests
    {
        bool hadRoster;
        string roster;

        [SetUp] public void SetUp()
        {
            hadRoster = PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);
            roster = PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, "{\"entries\":[]}");
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            if (hadRoster) PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, roster);
            else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            PlayerPrefs.Save();
            FlightSession.Return();
            yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);
        }

        [UnityTest] public IEnumerator LargeShipRendersDamageRecoveryWrecksAndRendererFallback()
        {
            if (!SystemInfo.supportsInstancing) Assert.Ignore("GPU instancing unavailable on this test device.");
            var data = new BlueprintData { name = "128 cell combat hull", parts = new List<BlockRecord>() };
            for (int y = 0; y < 2; y++) for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
                data.parts.Add(new BlockRecord { id = $"{x}-{y}-{z}", shape = data.parts.Count == 0 ? BlockShape.Core : BlockShape.Cube,
                    position = new Vector3(x, y + .5f, z), rotation = Quaternion.identity });
            FlightSession.Begin(data, true);
            yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);
            yield return null;
            var world = Object.FindFirstObjectByType<RangeCombatWorld>();
            world.enabled = false; world.Player.flight.enabled = false;
            var ship = world.Player.ship; ship.InvulnerableUntil = 0;
            var instances = ship.GetComponent<ShipInstanceRenderer>();
            Assert.That(instances.VisibleInstanceCount, Is.EqualTo(127));
            var renderer = ship.Parts[1].view.GetComponent<MeshRenderer>();
            instances.enabled = false; Assert.That(renderer.enabled, Is.True);
            instances.enabled = true; Assert.That(renderer.enabled, Is.False);

            var corner = ship.Parts.Find(p => p.record.id == "7-1-7");
            var status = GameObject.Find("OwnShipSchematic").GetComponent<ShipStatusGraphic>();
            ship.ApplyDamage(corner.record.id, 10);
            yield return new WaitForSeconds(.2f);
            Assert.That(status.DamagedPartCount, Is.EqualTo(1));
            ship.ApplyDamage(corner.record.id, corner.maxHp);
            yield return null;
            Assert.That(instances.VisibleInstanceCount, Is.EqualTo(126));
            ship.Respawn(); yield return null;
            Assert.That(instances.VisibleInstanceCount, Is.EqualTo(127));

            var core = ship.Parts.Find(p => p.record.shape == BlockShape.Core);
            ship.ApplyDamage(core.record.id, core.maxHp); yield return null;
            Assert.That(instances.VisibleInstanceCount, Is.Zero);
            Assert.That(world.Debris.PartCount, Is.EqualTo(127));
            foreach (var cluster in world.Debris.Clusters) foreach (var part in cluster.Parts)
            {
                Assert.That(part.GetComponent<MeshRenderer>().enabled, Is.True, "Batched source parts must produce visible wreckage.");
                Assert.That(part.GetComponent<Collider>().enabled, Is.True);
            }
            ship.Respawn(); yield return null;
            Assert.That(instances.VisibleInstanceCount, Is.EqualTo(127));
            Assert.That(world.Debris.PartCount, Is.EqualTo(127), "Respawn must preserve the old wreckage.");
        }
    }
}
