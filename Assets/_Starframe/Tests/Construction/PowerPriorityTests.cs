using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class PowerPriorityTests
    {
        readonly List<Object> owned=new List<Object>();
        ProjectileWorld projectiles;
        [TearDown]public void Cleanup()
        {
            projectiles?.Clear();projectiles=null;
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
            owned.Clear();BlockModules.Configure(null);
        }

        [TestCase(30f,.5f,0f,0f)]
        [TestCase(80f,1f,.4f,0f)]
        [TestCase(130f,1f,1f,.5f)]
        [TestCase(200f,1f,1f,1f)]
        public void ScarcityServesShieldsThenBoostThenWeapons(float generation,float shield,float boost,float weapon)
        {
            var bus=new ShipPowerBus();bus.Configure(0,generation);
            bus.AllocatePrioritized(1,60,50,40,false,out float sr,out float br,out float wr);
            Assert.That(sr,Is.EqualTo(shield).Within(.0001f));
            Assert.That(br,Is.EqualTo(boost).Within(.0001f));
            Assert.That(wr,Is.EqualTo(weapon).Within(.0001f));
            Assert.That(bus.RequestedPower,Is.EqualTo(150));
            Assert.That(bus.AllocatedPower,Is.EqualTo(Mathf.Min(generation,150)).Within(.0001f));
            Assert.That(bus.Ratio,Is.EqualTo(bus.AllocatedPower/150).Within(.0001f));
            Assert.That(bus.Energy,Is.Zero);
        }

        [Test]public void BatteryAndGenerationAreIntegratedOnceForTheCombinedPriorityDemand()
        {
            var bus=new ShipPowerBus();bus.Configure(100,10,true);
            bus.AllocatePrioritized(.25f,60,50,40,false,out _,out _,out _);
            Assert.That(bus.Energy,Is.EqualTo(65).Within(.0001f));
            bus.AllocatePrioritized(.25f,60,50,40,false,out _,out _,out _);
            Assert.That(bus.Energy,Is.EqualTo(30).Within(.0001f));
            bus.AllocatePrioritized(.25f,60,50,40,false,out float shield,out float boost,out float weapon);
            Assert.That(shield,Is.EqualTo(1));Assert.That(boost,Is.EqualTo(1));
            Assert.That(weapon,Is.EqualTo(.5f).Within(.0001f));Assert.That(bus.Energy,Is.Zero);
        }

        [Test]public void EmptySourcesStopConsumersWhileIdleRequestsAllowBatteryCharging()
        {
            var bus=new ShipPowerBus();bus.Configure(100,0);
            bus.AllocatePrioritized(1,60,50,40,false,out float shield,out float boost,out float weapon);
            Assert.That(shield,Is.Zero);Assert.That(boost,Is.Zero);Assert.That(weapon,Is.Zero);
            bus.Configure(100,10);
            bus.AllocatePrioritized(1,0,0,0,false,out shield,out boost,out weapon);
            Assert.That(shield,Is.EqualTo(1));Assert.That(boost,Is.EqualTo(1));Assert.That(weapon,Is.EqualTo(1));
            Assert.That(bus.Energy,Is.EqualTo(10));Assert.That(bus.AllocatedPower,Is.Zero);
            bus.AllocatePrioritized(1,0,0,40,false,out shield,out boost,out weapon);
            Assert.That(weapon,Is.EqualTo(.5f));Assert.That(bus.Energy,Is.Zero);
        }

        [Test]public void UnlimitedPowerDoesNotDebitBatteryAndZeroTimeDoesNotAdvanceIt()
        {
            var bus=new ShipPowerBus();bus.Configure(100,10);bus.SetEnergy(23);
            bus.AllocatePrioritized(1,60,50,40,true,out float shield,out float boost,out float weapon);
            Assert.That(shield,Is.EqualTo(1));Assert.That(boost,Is.EqualTo(1));Assert.That(weapon,Is.EqualTo(1));
            Assert.That(bus.Energy,Is.EqualTo(23));Assert.That(bus.AllocatedPower,Is.EqualTo(150));
            bus.AllocatePrioritized(0,600,500,400,false,out _,out _,out _);
            Assert.That(bus.Energy,Is.EqualTo(23));Assert.That(bus.RequestedPower,Is.EqualTo(150));
        }

        [TestCase(0),TestCase(1)]
        public void CombatExecuteRoutesDistinctRatiosForPlayerAndEnemy(int team)
        {
            var database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;tuning.boostPowerWatts=50;
            var rules=Own(ScriptableObject.CreateInstance<BattleTuning>());
            rules.unlimitedRangePower=false;rules.playerShieldRecoveryMultiplier=1;rules.playerShieldDelayMultiplier=1;
            var weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());weapon.sustainedPowerWatts=40;weapon.shotsPerSecond=4;
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Laser,BlockShape.Shield})
            {
                var module=Own(ScriptableObject.CreateInstance<ModuleDefinition>());module.shape=shape;
                module.massKg=100;module.maxHp=100;module.occupiedCells=new[]{Vector3Int.zero};
                if(shape==BlockShape.Laser){module.weapon=weapon;module.muzzleLocalPosition=Vector3.forward*.6f;}
                if(shape==BlockShape.Shield){module.shieldCapacity=100;module.shieldRecoveryPerSecond=20;module.shieldRecoveryDelay=0;module.shieldPowerWatts=60;}
                database.modules.Add(module);
            }
            var host=Own(new GameObject("Priority actor fixture"));host.transform.position=new Vector3(12000,12000,0);
            var body=host.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Laser,new Vector3(0,1.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Shield,new Vector3(-1,.5f,0),Quaternion.identity),Is.True);
            var ship=host.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export("priority"),tuning,rules);ship.Team=team;
            var flight=host.AddComponent<ShipFlightController>();flight.Initialize(body,ship,tuning,null,1);
            flight.SetExternalInput(Vector3.forward,Vector2.zero,boost:true);
            projectiles=new ProjectileWorld();projectiles.Initialize(null);
            var weapons=new ShipWeapons();weapons.Initialize(ship,projectiles);weapons.FireRequested=true;
            var combatHost=Own(new GameObject("Priority combat fixture"));var combat=combatHost.AddComponent<RangeCombatWorld>();
            Set(combat,"rules",rules);Set(combat,"flightTuning",tuning);
            var actor=new RangeCombatWorld.Actor{ship=ship,flight=flight,body=body,weapons=weapons};
            var execute=typeof(RangeCombatWorld).GetMethod("Execute",BindingFlags.Instance|BindingFlags.NonPublic);
            ship.Power.Configure(0,80);ship.Shields.Fields[0].charge=60;Physics.SyncTransforms();
            execute.Invoke(combat,new object[]{actor,1f});
            Assert.That(ship.Shields.Charge,Is.EqualTo(80).Within(.001f),"Shield recovery receives its full first-priority share.");
            Assert.That(flight.BoostPowerRatio,Is.EqualTo(.4f).Within(.001f));
            Assert.That(weapons.ShotsFired,Is.Zero,"Weapons receive no power while higher priorities consume generation.");
            Assert.That(ship.Power.RequestedPower,Is.EqualTo(150).Within(.001f));
            Assert.That(ship.Power.WeaponRatio,Is.Zero);Assert.That(ship.Power.Energy,Is.Zero);
            ship.Shields.Fields[0].charge=100;
            execute.Invoke(combat,new object[]{actor,1f});
            Assert.That(flight.BoostPowerRatio,Is.EqualTo(1));
            Assert.That(ship.Power.WeaponRatio,Is.EqualTo(.75f).Within(.001f));
            Assert.That(weapons.ShotsFired,Is.EqualTo(3),"Full shields release power to boost, then ordinary powered weapon cadence.");
        }

        static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
