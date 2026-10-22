using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
namespace Starframe.Tests
{
    public sealed class MultiTargetRangeTests
    {
        bool hadSettings;string originalSettings;
        [UnitySetUp]public IEnumerator SetUp()
        {
            hadSettings=PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);originalSettings=PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,"{\"entries\":[{\"file\":\"test.json\",\"count\":2}]}");
            FlightSession.Begin(FlightRangeBootstrap.DefaultShip(),true);yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);yield return null;
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            if(hadSettings)PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,originalSettings);else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);PlayerPrefs.Save();
            FlightSession.Return();yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);yield return null;
        }
        [UnityTest]public IEnumerator MultipleBlueprintCopiesSpawnSeparatedAndRespawnIndependently()
        {
            var world=Object.FindFirstObjectByType<RangeCombatWorld>();Assert.That(world.Bots.Count,Is.EqualTo(2));
            var first=world.Bots[0];var second=world.Bots[1];
            Assert.That(first.blueprintName,Is.EqualTo("test"));Assert.That(second.ship.Parts.Count,Is.EqualTo(first.ship.Parts.Count));
            Assert.That(Vector3.Distance(first.body.position,second.body.position),Is.GreaterThan(first.ship.HullRadius+second.ship.HullRadius));
            Assert.That(first.ship.Parts[0].record,Is.Not.SameAs(second.ship.Parts[0].record));
            var additional=FlightRangeBootstrap.DefaultShip();string before=JsonUtility.ToJson(additional);world.SpawnTargets(additional,1);
            Assert.That(world.Bots.Count,Is.EqualTo(3));Assert.That(world.Bots[2].ship.Parts.Count,Is.EqualTo(additional.parts.Count));
            world.enabled=false;foreach(var actor in world.Actors)actor.flight.enabled=false;
            world.Projectiles.Clear();int life=second.ship.LifeId;int thirdLife=world.Bots[2].ship.LifeId;
            world.Targeting.ToggleLock();Assert.That(world.Targeting.HasTarget,Is.True);
            var locked=world.Targeting.LockedTarget;Assert.That(locked.Team,Is.EqualTo(1));
            var profiles=new System.Collections.Generic.List<WeaponAimProfile>();world.Player.weapons.GetAimProfiles(profiles);
            Assert.That(world.Targeting.LeadPoints.Count,Is.GreaterThan(0));
            foreach(var lead in world.Targeting.LeadPoints){
                var profile=profiles.Find(p=>p.weapon==lead.weapon);var screen=AimProjection.ScreenPoint(Camera.main,lead.aimDirection);
                var shotDirection=Camera.main.ScreenPointToRay(screen).direction;
                Assert.That(Vector3.Distance(profile.origin+shotDirection*(lead.weapon.projectileSpeed*lead.flightTime),lead.point),Is.LessThan(.01f),"The live lock system's pip must aim along the actual muzzle intercept trajectory.");
            }
            world.Targeting.Clear();
            var core=first.ship.Parts.Find(p=>p.record.shape==BlockShape.Core);first.ship.ApplyDamage(core.record.id,100000,world.Player.ship);
            Assert.That(first.ship.IsAlive,Is.False);Assert.That(second.ship.IsAlive,Is.True);Assert.That(world.BotKills,Is.EqualTo(1));
            world.Step(3.1f);Assert.That(first.ship.IsAlive,Is.True);Assert.That(second.ship.LifeId,Is.EqualTo(life));Assert.That(world.Bots[2].ship.LifeId,Is.EqualTo(thirdLife));
            Assert.That(world.Debris.PartCount,Is.GreaterThan(0));
            foreach(var wreck in world.Debris.Clusters)Assert.That(wreck.WorldBounds.SqrDistance(first.body.position),Is.GreaterThanOrEqualTo(first.ship.HullRadius*first.ship.HullRadius),"Respawn must avoid existing wreckage.");
            foreach(var part in first.ship.Parts){Assert.That(part.present,Is.True);Assert.That(part.hp,Is.EqualTo(part.maxHp));}
            Assert.That(JsonUtility.ToJson(additional),Is.EqualTo(before));
            yield return null;Assert.That(System.Array.Exists(Object.FindFirstObjectByType<RangeHUD>().GetComponentsInChildren<Transform>(true),t=>t.name=="BotMarker2"),Is.True);
        }
        [UnityTest]public IEnumerator TacticalHudTracksDamageAndKeepsOffscreenLockUntilRangeExceeded()
        {
            var world=Object.FindFirstObjectByType<RangeCombatWorld>();
            world.enabled=false;foreach(var actor in world.Actors){actor.flight.enabled=false;actor.body.interpolation=RigidbodyInterpolation.None;actor.body.linearVelocity=Vector3.zero;}
            world.Projectiles.Clear();world.Targeting.ToggleLock();Assert.That(world.Targeting.HasTarget,Is.True);
            var target=world.Targeting.LockedTarget;RangeCombatWorld.Actor locked=null;foreach(var actor in world.Bots)if(actor.ship==target)locked=actor;
            Vector3 coreOffset=target.CorePhysicsPosition-locked.body.position;
            locked.body.position=world.Player.ship.CorePhysicsPosition-Camera.main.transform.forward*2999f-coreOffset;
            Physics.SyncTransforms();world.Targeting.RefreshLeadPoints();
            Assert.That(world.Targeting.HasTarget,Is.True,"Turning away must not drop a lock inside 3 km.");
            yield return new WaitForSeconds(.2f);Canvas.ForceUpdateCanvases();
            Assert.That(Object.FindFirstObjectByType<TargetAimGraphic>().EdgeIndicatorVisible,Is.True);
            Assert.That(Object.FindFirstObjectByType<SphereRadarGraphic>().VisibleContactCount,Is.EqualTo(2));
            var own=GameObject.Find("OwnShipSchematic").GetComponent<ShipStatusGraphic>();
            var enemy=GameObject.Find("TargetShipSchematic").GetComponent<ShipStatusGraphic>();
            Assert.That(enemy.Ship,Is.SameAs(target));Assert.That(own.CorePresent,Is.True);
            var part=world.Player.ship.Parts.Find(p=>p.present&&p.record.shape!=BlockShape.Core);
            world.Player.ship.InvulnerableUntil=0;
            int before=own.VisiblePartCount;world.Player.ship.ApplyDamage(part.record.id,part.maxHp*.25f,target);
            yield return new WaitForSeconds(.2f);Canvas.ForceUpdateCanvases();
            Assert.That(own.DamagedPartCount,Is.EqualTo(1));
            var mesh=own.canvasRenderer.GetMesh();Assert.That(System.Array.Exists(mesh.colors32,c=>c.r>180&&c.g<100),Is.True,"Damage must tint the corresponding schematic geometry red.");
            world.Player.ship.ApplyDamage(part.record.id,100000,target);
            yield return new WaitForSeconds(.2f);Canvas.ForceUpdateCanvases();
            Assert.That(own.VisiblePartCount,Is.LessThan(before));Assert.That(own.CorePresent,Is.True);
            locked.body.position=world.Player.ship.CorePhysicsPosition-Camera.main.transform.forward*3001f-coreOffset;
            Physics.SyncTransforms();world.Targeting.RefreshLeadPoints();Assert.That(world.Targeting.HasTarget,Is.False);
            yield return new WaitForSeconds(.2f);Assert.That(enemy.Ship,Is.Null);
        }
        [UnityTest]public IEnumerator ShieldSharesRangePowerAndEnemySchematicTracksRelativeRoll()
        {
            var blueprint=FlightRangeBootstrap.DefaultShip();blueprint.parts.Add(new BlockRecord{id="test-shield",shape=BlockShape.Shield,position=new Vector3(-1,1.5f,0),rotation=Quaternion.identity});
            FlightSession.Begin(blueprint,true);yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);yield return null;
            var world=Object.FindFirstObjectByType<RangeCombatWorld>();
            world.enabled=false;foreach(var a in world.Actors){a.flight.enabled=false;a.body.interpolation=RigidbodyInterpolation.None;}
            world.Projectiles.Clear();world.Targeting.ToggleLock();Assert.That(world.Targeting.HasTarget,Is.True);
            var player=world.Player.ship;Assert.That(player.Shields.Capacity,Is.EqualTo(300));
            player.Shields.Absorb(player.Shields.Fields[0],100,Vector3.zero);float energy=player.Power.Energy;
            player.BeginStep(2.1f);world.Step(.1f);
            Assert.That(player.Shields.Charge,Is.EqualTo(205).Within(.01f));Assert.That(player.Power.Energy,Is.EqualTo(energy-4).Within(.01f));
            var own=GameObject.Find("OwnShipSchematic").GetComponent<ShipStatusGraphic>();
            var enemy=GameObject.Find("TargetShipSchematic").GetComponent<ShipStatusGraphic>();
            yield return new WaitForSeconds(.2f);var ownRotation=own.DisplayRotation;
            var target=world.Targeting.LockedTarget;var body=target.GetComponent<Rigidbody>();
            body.rotation=Quaternion.Euler(25,40,90);world.Player.body.rotation=Quaternion.Euler(0,0,30);Physics.SyncTransforms();
            yield return new WaitForSeconds(.2f);
            var reference=Quaternion.LookRotation(target.CorePosition-world.Player.ship.transform.position,world.Player.ship.transform.up);
            var expected=Quaternion.Inverse(reference)*body.rotation*Quaternion.Inverse(target.CoreRotation);
            Assert.That(Quaternion.Angle(enemy.DisplayRotation,expected),Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(own.DisplayRotation,ownRotation),Is.LessThan(.01f));
            Assert.That(GameObject.Find("ShieldValue"),Is.Not.Null);Assert.That(GameObject.Find("ShieldFill"),Is.Not.Null);
        }
    }
}
