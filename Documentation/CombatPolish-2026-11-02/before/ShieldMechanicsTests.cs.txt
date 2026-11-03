using System.Collections.Generic;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class ShieldMechanicsTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();BlockModules.Configure(null);}
        [Test]public void ShieldInterceptsSweptFastShotBeforeHullAndIgnoresOwnOutgoingShot()
        {
            var target=Ship(new Vector3(0,1000,0));var shooter=Ship(new Vector3(0,1000,-30));
            var world=new ProjectileWorld();world.RegisterShields(target);world.RegisterShields(shooter);
            int confirmations=0;world.HitConfirmed+=source=>{Assert.That(source,Is.EqualTo(shooter));confirmations++;};
            var weapon=Weapon(40);var core=target.Parts.Find(p=>p.record.shape==BlockShape.Core);float hp=core.hp;
            world.Fire(shooter,weapon,new Vector3(0,1000.5f,-20),Vector3.forward);world.Step(.05f);
            Assert.That(target.Shields.Charge,Is.EqualTo(260).Within(.001f));Assert.That(core.hp,Is.EqualTo(hp));Assert.That(world.ActiveCount,Is.Zero);
            Assert.That(confirmations,Is.EqualTo(1),"A shield absorption must confirm even without hull damage.");
            world.Fire(target,weapon,target.CorePhysicsPosition,Vector3.right);world.Step(.03f);
            Assert.That(target.Shields.Charge,Is.EqualTo(260).Within(.001f));
            Assert.That(confirmations,Is.EqualTo(1),"A shot leaving its own shield must not confirm a hit.");
        }
        [Test]public void ShieldOverkillContinuesWithOnlyRemainingDamage()
        {
            var target=Ship(new Vector3(0,1100,0));var shooter=Ship(new Vector3(0,1100,-30));
            var world=new ProjectileWorld();world.RegisterShields(target);var core=target.Parts.Find(p=>p.record.shape==BlockShape.Core);float hp=core.hp;
            var weapon=Weapon(340);weapon.projectileDiameter=.05f;
            world.Fire(shooter,weapon,new Vector3(-20,1100.5f,0),Vector3.right);world.Step(.06f);target.FlushChanges();
            Assert.That(target.Shields.Charge,Is.Zero);Assert.That(core.hp,Is.EqualTo(hp-40).Within(.01f));
        }
        [Test]public void ShieldSamplesFollowMovementBetweenPhysicsSteps()
        {
            var target=Ship(new Vector3(0,1400,0));var shooter=Ship(new Vector3(0,1400,-30));
            var world=new ProjectileWorld();world.RegisterShields(target);var weapon=Weapon(40);
            world.Fire(shooter,weapon,target.CorePhysicsPosition-Vector3.forward*20,Vector3.forward);world.Step(.05f);
            Assert.That(target.Shields.Charge,Is.EqualTo(260).Within(.01f));
            target.GetComponent<Rigidbody>().position+=Vector3.right*40;Physics.SyncTransforms();
            world.Fire(shooter,weapon,target.CorePhysicsPosition-Vector3.forward*20,Vector3.forward);world.Step(.05f);
            Assert.That(target.Shields.Charge,Is.EqualTo(220).Within(.01f),"A fixed-step sample must not retain the previous shield position.");
        }
        [Test]public void SameStepVolleyUsesRemainingShieldHealthForEachProjectile()
        {
            var target=Ship(new Vector3(0,1500,0));var shooter=Ship(new Vector3(0,1500,-30));
            var world=new ProjectileWorld();world.RegisterShields(target);var weapon=Weapon(40);weapon.projectileDiameter=.05f;
            for(int i=0;i<10;i++)world.Fire(shooter,weapon,target.CorePhysicsPosition-Vector3.forward*20,Vector3.forward);
            world.Step(.05f);
            Assert.That(target.Shields.Charge,Is.Zero);
            Assert.That(target.DamageTaken,Is.EqualTo(100).Within(.01f),"Only geometry is cached: later rounds must pass through a depleted shield.");
        }
        [Test]public void RecoveryWaitsTwoSecondsDrawsPowerAndStopsWhenEmpty()
        {
            var ship=Ship(new Vector3(0,1200,0));var field=ship.Shields.Fields[0];ship.Shields.Absorb(field,150,Vector3.zero);
            ship.BeginStep(1.9f);Assert.That(ship.Shields.PrepareRecovery(.1f),Is.Zero);
            ship.BeginStep(.2f);float demand=ship.Shields.PrepareRecovery(.1f);Assert.That(demand,Is.EqualTo(100).Within(.01f));
            float energy=ship.Power.Energy;float ratio=ship.Power.Allocate(.1f,demand);ship.Shields.ExecuteRecovery(ratio);
            Assert.That(ship.Shields.Charge,Is.EqualTo(155).Within(.01f));Assert.That(ship.Power.Energy,Is.EqualTo(energy-10).Within(.01f));
            ship.Power.SetEnergy(0);ship.BeginStep(1);demand=ship.Shields.PrepareRecovery(1);ratio=ship.Power.Allocate(1,demand);ship.Shields.ExecuteRecovery(ratio);
            Assert.That(ship.Shields.Charge,Is.EqualTo(155).Within(.01f));
            ship.Power.SetEnergy(50);ship.BeginStep(1);demand=ship.Shields.PrepareRecovery(1);ratio=ship.Power.Allocate(1,demand);ship.Shields.ExecuteRecovery(ratio);
            Assert.That(ship.Shields.Charge,Is.EqualTo(180).Within(.01f));Assert.That(ship.Power.Energy,Is.Zero);
            ship.Shields.Absorb(field,1,Vector3.zero);ship.BeginStep(1.9f);Assert.That(ship.Shields.PrepareRecovery(1),Is.Zero);
        }
        [Test]public void RemovedGeneratorDropsShieldAndRespawnRestoresIt()
        {
            var ship=Ship(new Vector3(0,1300,0));var generator=ship.Parts.Find(p=>p.record.shape==BlockShape.Shield);
            ship.ApplyDamage(generator.record.id,10000);Assert.That(ship.IsAlive,Is.True);Assert.That(ship.Shields.Capacity,Is.Zero);
            ship.Respawn();Assert.That(ship.Shields.Charge,Is.EqualTo(300));Assert.That(ship.Shields.Capacity,Is.EqualTo(300));
        }
        [Test]public void SphereEntryHandlesGrazingInsideAndMisses()
        {
            Assert.That(ShipShieldSystem.TrySphereEntry(new Vector3(0,0,-20),Vector3.forward,Vector3.zero,8,0,40,out var entry),Is.True);Assert.That(entry,Is.EqualTo(12));
            Assert.That(ShipShieldSystem.TrySphereEntry(Vector3.zero,Vector3.forward,Vector3.zero,8,0,40,out entry),Is.True);Assert.That(entry,Is.Zero);
            Assert.That(ShipShieldSystem.TrySphereEntry(new Vector3(9,0,-20),Vector3.forward,Vector3.zero,8,0,40,out entry),Is.False);
        }
        FlightShipRuntime Ship(Vector3 position)
        {
            var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Shield,BlockShape.Cube,BlockShape.Power})
            {
                var d=Own(ScriptableObject.CreateInstance<ModuleDefinition>());d.shape=shape;d.maxHp=1000;d.massKg=100;
                if(shape==BlockShape.Shield){d.shieldCapacity=300;d.shieldRadius=8;d.shieldRecoveryDelay=2;d.shieldRecoveryPerSecond=50;d.shieldPowerWatts=100;}
                if(shape==BlockShape.Power)d.storageJoules=500;db.modules.Add(d);
            }
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
            var go=Own(new GameObject("shield fixture"));go.transform.position=position;
            var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(db);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Shield,new Vector3(1,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(0,.5f,1),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Power,new Vector3(0,.5f,-1),Quaternion.identity),Is.True);
            var ship=go.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export("shield"),tuning);Physics.SyncTransforms();return ship;
        }
        WeaponDefinition Weapon(float damage){var w=Own(ScriptableObject.CreateInstance<WeaponDefinition>());w.damage=damage;w.projectileSpeed=800;w.range=100;w.projectileDiameter=1;w.penetrationDepth=1;return w;}
        T Own<T>(T item)where T:Object{owned.Add(item);return item;}
    }
}
