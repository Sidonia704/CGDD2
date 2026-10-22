using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;
namespace Starframe.Tests
{
    public sealed class DebrisMechanicsTests
    {
        GameObject root;BattleTuning rules;WeaponDefinition weapon;
        FlightShipRuntime ship;DebrisWorld debris;
        [SetUp]public void SetUp()
        {
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            rules=ScriptableObject.CreateInstance<BattleTuning>();root=new GameObject("Debris regression fixture");
            var original=new GameObject("Original ship");original.transform.SetParent(root.transform,false);var assembly=original.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);
            var data=new BlueprintData{name="breakable line",parts=new List<BlockRecord>()};
            for(int i=0;i<5;i++)data.parts.Add(new BlockRecord{id="part-"+i,shape=i==0?BlockShape.Core:BlockShape.Cube,position=new Vector3(i-3,.5f,0),rotation=Quaternion.identity});
            assembly.Import(data);ship=original.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,data,tuning,rules);
            debris=root.AddComponent<DebrisWorld>();debris.Initialize(rules,null);
            ship.PartsDetached+=parts=>debris.Spawn(parts,tuning,Vector3.forward*9,ship.CorePosition,ship);
            ship.ApplyDamage("part-2",10);ship.ApplyDamage("part-1",10000);Physics.SyncTransforms();
        }
        [TearDown]public void TearDown(){if(root)Object.DestroyImmediate(root);if(rules)Object.DestroyImmediate(rules);if(weapon)Object.DestroyImmediate(weapon);BlockModules.Configure(null);}

        [Test]public void DetachedIslandRetainsHealthAndCollisionButLosesShipFunctions()
        {
            Assert.That(debris.Clusters.Count,Is.EqualTo(1));Assert.That(debris.PartCount,Is.EqualTo(3));
            var island=debris.Clusters[0];Assert.That(island.Body.isKinematic,Is.False);Assert.That(island.Body.useGravity,Is.False);
            Assert.That(island.Body.linearVelocity.magnitude,Is.GreaterThan(5));Assert.That(island.Body.angularVelocity.magnitude,Is.GreaterThan(.1f));
            var part=Find("part-2");Assert.That(part.Part.hp,Is.EqualTo(part.Part.maxHp-10));
            foreach(var wreck in island.Parts){
                Assert.That(wreck.GetComponent<MeshCollider>().enabled,Is.True);Assert.That(wreck.GetComponent<MeshCollider>().sharedMesh.bounds.size,Is.EqualTo(Vector3.one));
                Assert.That(wreck.GetComponent<ShipPartHitbox>(),Is.Null);Assert.That(wreck.GetComponent<ShipFlightController>(),Is.Null);
            }
            Assert.That(ship.Parts.Find(p=>p.record.id=="part-2").present,Is.False);
            float hp=part.Part.hp;ship.Respawn();Assert.That(part.Part.hp,Is.EqualTo(hp),"Repairing or respawning the ship must not heal its independent wreckage.");
        }
        [Test]public void LasersDamageAndDestroyDebrisUsingTheNormalProjectileSweep()
        {
            var target=Find("part-2");float hp=target.Part.hp;var state=target.Part;
            weapon=ScriptableObject.CreateInstance<WeaponDefinition>();weapon.damage=20;weapon.projectileSpeed=100;weapon.projectileDiameter=.1f;weapon.penetrationDepth=2;weapon.range=100;
            var projectiles=new ProjectileWorld();var muzzle=target.transform.position-Vector3.forward*5;
            projectiles.Fire(ship,weapon,muzzle,Vector3.forward);projectiles.Step(.1f);
            Assert.That(state.hp,Is.EqualTo(hp-20));Assert.That(state.present,Is.True);
            weapon.damage=1000;projectiles.Fire(ship,weapon,muzzle,Vector3.forward);projectiles.Step(.1f);debris.Step(0);
            Assert.That(state.present,Is.False);Assert.That(debris.PartCount,Is.EqualTo(2));projectiles.Clear();
        }
        [Test]public void DestroyingDebrisBridgeSplitsItIntoIndependentDriftingBodies()
        {
            var left=Find("part-2");var right=Find("part-4");var beforeLeft=left.transform.position;var beforeRight=right.transform.position;
            Find("part-3").ApplyDamage(1000);debris.Step(0);Physics.SyncTransforms();
            Assert.That(debris.Clusters.Count,Is.EqualTo(2));Assert.That(left.Cluster,Is.Not.SameAs(right.Cluster));
            Assert.That(Vector3.Distance(left.transform.position,beforeLeft),Is.LessThan(.001f));Assert.That(Vector3.Distance(right.transform.position,beforeRight),Is.LessThan(.001f));
            Assert.That(left.Cluster.Body.mass,Is.EqualTo(300));Assert.That(right.Cluster.Body.mass,Is.EqualTo(300));
        }
        [Test]public void EnclosedCoreIsIgnoredOnlyUntilWreckageClearsItsSourceShip()
        {
            var island=debris.Clusters[0];var core=ship.Parts.Find(p=>p.record.shape==BlockShape.Core).view.GetComponent<Collider>();
            var collider=Find("part-3").GetComponent<Collider>();ship.transform.position+=Vector3.right*3;Physics.SyncTransforms();debris.Step(0);
            Assert.That(island.SuppressSourceCollisions,Is.True);Assert.That(Physics.GetIgnoreCollision(core,collider),Is.True);
            island.Body.position+=Vector3.forward*30;Physics.SyncTransforms();debris.Step(0);
            Assert.That(island.SuppressSourceCollisions,Is.False);Assert.That(Physics.GetIgnoreCollision(core,collider),Is.False);
        }
        [TestCase(false)][TestCase(true)]public void CoreDeathTransfersSurvivorsBeforeDeathAndRespawnDoesNotAlterTheirHealth(bool deferred)
        {
            ship.Respawn();ship.ApplyDamage("part-2",17);int existing=debris.PartCount,deaths=0;
            var transferred=new List<DebrisPartHitbox>();
            ship.PartsDetached+=parts=>{foreach(var p in parts)transferred.Add(p.view.GetComponent<ShipPartHitbox>().DetachedReplacement);};
            ship.Died+=()=>{
                deaths++;Assert.That(transferred.Count,Is.EqualTo(4));Assert.That(debris.PartCount,Is.EqualTo(existing+4));
                Assert.That(ship.Performance.totalMass,Is.Zero);Assert.That(ship.Power.Capacity,Is.Zero);
            };
            var core=ship.Parts.Find(p=>p.record.shape==BlockShape.Core);ship.ApplyDamage(core.record.id,core.maxHp,null,deferred);
            Assert.That(ship.IsAlive,Is.False);Assert.That(core.detached,Is.False);Assert.That(deaths,Is.EqualTo(1));
            var damaged=transferred.Find(p=>p.Part.record.id=="part-2");float health=damaged.Part.hp;
            Assert.That(health,Is.EqualTo(damaged.Part.maxHp-17));
            foreach(var part in transferred){Assert.That(part.Part.present,Is.True);Assert.That(part.GetComponent<Collider>().enabled,Is.True);}
            ship.BeginStep(100);ship.AdvanceRecovery(100);ship.FlushChanges();ship.ApplyDamage(core.record.id,10000);
            Assert.That(deaths,Is.EqualTo(1));Assert.That(debris.PartCount,Is.EqualTo(existing+4));
            ship.Respawn();Assert.That(ship.IsAlive,Is.True);Assert.That(damaged.Part.hp,Is.EqualTo(health));
            Assert.That(debris.PartCount,Is.EqualTo(existing+4));damaged.ApplyDamage(10000);debris.Step(0);
            Assert.That(debris.PartCount,Is.EqualTo(existing+3));Assert.That(ship.Parts.Find(p=>p.record.id=="part-2").hp,Is.EqualTo(damaged.Part.maxHp));
        }
        [Test]public void HealthyWreckageSurvivesElapsedTimeAndDriftingBeyondTheRange()
        {
            var island=debris.Clusters[0];island.Body.position=Vector3.one*10000;debris.Step(36000);
            Assert.That(debris.PartCount,Is.EqualTo(3));Assert.That(island.gameObject.activeSelf,Is.True);
            foreach(var part in island.Parts)Assert.That(part.Part.hp,Is.GreaterThan(0));
        }
        [Test]public void CorePiercingShotStillDamagesTheNextBlockAfterItBecomesWreckage()
        {
            ship.Respawn();
            var tuning=ship.Tuning;var shooterObject=new GameObject("Independent shooter");shooterObject.transform.SetParent(root.transform,false);shooterObject.transform.position=Vector3.up*20;
            var assembly=shooterObject.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);
            var data=new BlueprintData{name="shooter",parts=new List<BlockRecord>{new BlockRecord{id="shooter-core",shape=BlockShape.Core,position=Vector3.up*.5f,rotation=Quaternion.identity}}};
            assembly.Import(data);var shooter=shooterObject.AddComponent<FlightShipRuntime>();shooter.Initialize(assembly,data,tuning,rules);Physics.SyncTransforms();
            var core=ship.Parts.Find(p=>p.record.shape==BlockShape.Core);var bridge=ship.Parts.Find(p=>p.record.id=="part-1");
            weapon=ScriptableObject.CreateInstance<WeaponDefinition>();weapon.damage=core.maxHp;weapon.projectileSpeed=100;weapon.projectileDiameter=.1f;weapon.penetrationDepth=2;weapon.range=100;weapon.secondLayerDamageMultiplier=bridge.maxHp*.5f/core.maxHp;
            var projectiles=new ProjectileWorld();projectiles.Fire(shooter,weapon,new Vector3(-6,.5f,0),Vector3.right);projectiles.Step(.1f);
            Assert.That(ship.IsAlive,Is.False);var wreck=bridge.view.GetComponent<ShipPartHitbox>().DetachedReplacement;
            Assert.That(wreck,Is.Not.Null);Assert.That(wreck.Part.hp,Is.EqualTo(bridge.maxHp*.5f).Within(.001f));projectiles.Clear();
        }
        DebrisPartHitbox Find(string id){foreach(var cluster in debris.Clusters)foreach(var part in cluster.Parts)if(part.Part.record.id==id)return part;Assert.Fail("Missing debris "+id);return null;}
    }
}
