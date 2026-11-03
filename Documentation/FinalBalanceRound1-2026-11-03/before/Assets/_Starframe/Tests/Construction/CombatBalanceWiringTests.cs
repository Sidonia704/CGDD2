using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Starframe.App;
using Starframe.Combat;
using Starframe.Flight;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Starframe.Construction.Tests
{
    public sealed class CombatBalanceWiringTests
    {
        readonly List<Object> owned=new List<Object>();
        ModuleDatabase database,previousDatabase;
        ModuleDefinition gun;
        FlightTuning tuning;
        BattleTuning rules;
        FlareDefinition flare;
        UnityEngine.Random.State previousRandom;
        ProjectileWorld projectiles;

        [SetUp] public void SetUp()
        {
            previousDatabase=BlockModules.ConfiguredDatabase;previousRandom=UnityEngine.Random.state;
            database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            rules=Own(ScriptableObject.CreateInstance<BattleTuning>());rules.botReactionSeconds=1.2f;rules.botPilotDecisionSeconds=.1f;
            flare=Own(ScriptableObject.CreateInstance<FlareDefinition>());flare.decoyRadius=150;flare.cooldownSeconds=10;
            foreach(var shape in new[]{BlockShape.Core,BlockShape.FlareLauncher,BlockShape.Laser}){
                var module=Own(ScriptableObject.CreateInstance<ModuleDefinition>());module.shape=shape;module.massKg=100;module.maxHp=1000;
                module.occupiedCells=new[]{Vector3Int.zero};module.category=shape==BlockShape.Core?ModuleCategory.Core:ModuleCategory.Weapon;
                if(shape==BlockShape.FlareLauncher)module.countermeasure=flare;
                if(shape==BlockShape.Laser){module.weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());gun=module;}
                database.modules.Add(module);
            }
            projectiles=new ProjectileWorld();projectiles.Initialize(null);
        }
        [TearDown] public void TearDown()
        {
            projectiles.Clear();
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
            owned.Clear();BlockModules.Configure(previousDatabase);UnityEngine.Random.state=previousRandom;
        }

        [Test] public void BotUsesOnlyItsRealIncomingThreatAfterReactionAndInsideFlareReach()
        {
            var owner=Ship(new Vector3(0,4200,-1000),0,false);
            var target=Ship(new Vector3(0,4200,0),1,true);
            var flares=Own(new GameObject("AI flare wiring world")).AddComponent<FlareWorld>();flares.Initialize();
            var countermeasures=target.gameObject.AddComponent<ShipCountermeasureController>();countermeasures.Initialize(target,flares);
            var world=Own(new GameObject("AI countermeasure coordinator")).AddComponent<RangeCombatWorld>();
            Set(world,"rules",rules);Set(world,"flightTuning",tuning);Set(world,"projectiles",projectiles);
            var actor=new RangeCombatWorld.Actor{ship=target,countermeasures=countermeasures};
            var missile=Own(ScriptableObject.CreateInstance<WeaponDefinition>());missile.delivery=WeaponDelivery.GuidedMissile;
            missile.projectileSpeed=missile.missileLaunchSpeed=100;missile.missileAcceleration=0;missile.range=2000;
            React(world,actor,0);Assert.That(flares.ActiveSparkCount,Is.Zero,"A launcher does not fire without an incoming missile.");
            projectiles.Fire(owner,missile,target.CorePhysicsPosition-Vector3.forward*300,Vector3.forward,null,default,target);
            React(world,actor,.1);React(world,actor,.5);
            Assert.That(flares.ActiveSparkCount,Is.Zero,"The real threat must remain noticed for the bot reaction delay.");
            React(world,actor,1.4);
            Assert.That(flares.ActiveSparkCount,Is.Zero,"A far missile is not yet within the installed flare's reach.");
            projectiles.Step(1.6f);target.BeginStep(3);
            React(world,actor,3);
            Assert.That(flares.ActiveSparkCount,Is.EqualTo(flare.burstCount));
            Assert.That(countermeasures.ReadyCount,Is.Zero);
            React(world,actor,3.2);React(world,actor,4.5);
            Assert.That(flares.ActiveSparkCount,Is.EqualTo(flare.burstCount),"The existing launcher cooldown prevents repeated deployments.");
            projectiles.Clear();React(world,actor,4.7);
            Assert.That(actor.incomingMissileSince,Is.EqualTo(-1));
        }

        [Test] public void DeploymentRejectsMalformedLiveMissileFieldsButIgnoresUnusedMissileSettingsOnDirectWeapons()
        {
            var ship=new BlueprintData{name="Delivery validation"};
            ship.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0)});
            ship.parts.Add(new BlockRecord{id="gun",shape=BlockShape.Laser,position=new Vector3(0,1.5f,0)});
            var valid=gun.weapon;valid.delivery=WeaponDelivery.GuidedMissile;
            Assert.That(DeploymentValidator.ValidateForRange(ship,database).IsValid,Is.True);
            var mutations=new Action<WeaponDefinition>[] {
                weapon=>weapon.delivery=(WeaponDelivery)99,
                weapon=>weapon.secondLayerDamageMultiplier=1.01f,
                weapon=>weapon.reloadSeconds=float.NaN,
                weapon=>weapon.missileLockSeconds=-1,
                weapon=>weapon.missileTurnDegrees=float.PositiveInfinity,
                weapon=>weapon.missileTurnAcceleration=-1,
                weapon=>weapon.missileGuidanceSeconds=float.NaN,
                weapon=>weapon.missileSeekerLossSeconds=-1,
                weapon=>weapon.missileSeekerConeDegrees=180,
                weapon=>weapon.missileAcceleration=-1,
                weapon=>weapon.missileLaunchSpeed=0,
                weapon=>weapon.explosionRadius=0,
                weapon=>weapon.missileLaunchOffsets=new[]{new Vector3(float.NaN,0,0)}
            };
            foreach(var mutate in mutations){
                var invalid=Own(Object.Instantiate(valid));mutate(invalid);gun.weapon=invalid;
                Assert.That(DeploymentValidator.ValidateForRange(ship,database).Errors.Exists(issue=>issue.key=="deploy.error.moduleConfig"),Is.True);
            }
            gun.weapon=valid;valid.delivery=WeaponDelivery.Projectile;valid.reloadSeconds=float.NaN;
            Assert.That(DeploymentValidator.ValidateForRange(ship,database).IsValid,Is.True,"Unused missile-only settings must not disable a direct weapon.");
        }

        FlightShipRuntime Ship(Vector3 position,int team,bool launcher)
        {
            var root=Own(new GameObject("AI flare fixture"));root.transform.position=position;
            var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(database);
            var data=new BlueprintData{name="AI flare fixture"};
            data.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0)});
            if(launcher)data.parts.Add(new BlockRecord{id="flare",shape=BlockShape.FlareLauncher,position=new Vector3(0,1.5f,0)});
            assembly.Import(data);var runtime=root.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,assembly.Export(data.name),tuning,rules);runtime.Team=team;
            Physics.SyncTransforms();return runtime;
        }
        static void React(RangeCombatWorld world,RangeCombatWorld.Actor actor,double time)
        {
            Set(world,"clock",time);
            typeof(RangeCombatWorld).GetMethod("UpdateCountermeasures",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(world,new object[]{actor,true});
        }
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
