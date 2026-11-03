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
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Cube,BlockShape.FlareLauncher,BlockShape.Laser}){
                var module=Own(ScriptableObject.CreateInstance<ModuleDefinition>());module.shape=shape;module.massKg=100;module.maxHp=1000;
                module.occupiedCells=new[]{Vector3Int.zero};module.category=shape==BlockShape.Core?ModuleCategory.Core:shape==BlockShape.Cube?ModuleCategory.Hull:ModuleCategory.Weapon;
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

        [TestCase(240f)] [TestCase(245f)] [TestCase(250f)]
        public void FastIncomingMissileTriggersARealAnticipatoryBurstAndCanBeDiverted(float speed)
        {
            var owner=Ship(new Vector3(0,4400,-1000),0,false);
            var target=Ship(new Vector3(0,4400,0),1,true);
            var world=Coordinator(target,out var actor,out var flares);
            flare.decoyChance=1;flare.spreadHalfAngleDegrees=0;
            var missile=IncomingMissile(speed);
            projectiles.Fire(owner,missile,target.CorePhysicsPosition-Vector3.forward*350,Vector3.forward,null,default,target);
            float firstBurstAt=-1,firstBurstDistance=0;
            for(int i=1;i<=175;i++){
                target.BeginStep(.02f);projectiles.Step(.02f);
                projectiles.TryGetIncomingThreat(target,out float distance,out _);
                React(world,actor,i*.02);
                if(firstBurstAt<0&&flares.Bursts.Count>0){firstBurstAt=i*.02f;firstBurstDistance=distance;}
                flares.Step(.02f);
            }
            Assert.That(firstBurstAt,Is.GreaterThan(0),"The coordinator must actually emit a burst against a moving live missile.");
            Assert.That(firstBurstAt,Is.LessThan(rules.botReactionSeconds),"An imminent fast approach cannot wait out the full nominal reaction.");
            Assert.That(firstBurstDistance,Is.GreaterThan(flare.decoyRadius+target.HullRadius),
                "The burst is launched before radius entry using its reachable future position, rather than an inflated radius.");
            Assert.That(actor.countermeasures.GetCooldownRemaining("flare"),Is.GreaterThan(0));
            Assert.That(target.DamageTaken,Is.Zero,"A successful reachable burst must divert the real swept missile away from this small hull.");
            Assert.That(projectiles.TryGetIncomingThreat(target,out _,out _),Is.False);
        }

        [Test] public void BotForecastPreservesRoofObstructionAndSpendsNothingUntilTheMountIsClear()
        {
            var owner=Ship(new Vector3(0,4500,-1000),0,false);
            var target=Ship(new Vector3(0,4500,0),1,true);
            var world=Coordinator(target,out var actor,out var flares);
            var roof=new ShipPartState{record=new BlockRecord{id="roof",shape=BlockShape.Laser,position=new Vector3(0,5.5f,0),rotation=Quaternion.identity},hp=100,maxHp=100};
            target.Parts.Add(roof);
            projectiles.Fire(owner,IncomingMissile(245),target.CorePhysicsPosition-Vector3.forward*350,Vector3.forward,null,default,target);
            for(int i=1;i<=20;i++){target.BeginStep(.02f);projectiles.Step(.02f);React(world,actor,i*.02);}
            Assert.That(flares.Bursts,Is.Empty);Assert.That(actor.countermeasures.GetCooldownRemaining("flare"),Is.Zero);
            roof.present=false;
            for(int i=21;i<=30;i++){target.BeginStep(.02f);projectiles.Step(.02f);React(world,actor,i*.02);}
            Assert.That(flares.Bursts.Count,Is.EqualTo(1));Assert.That(actor.countermeasures.GetCooldownRemaining("flare"),Is.GreaterThan(0));
        }

        [Test] public void BotSpacesFollowupBurstsAcrossInstalledLaunchersAndRetainsTheirCooldowns()
        {
            var owner=Ship(new Vector3(0,4600,-1000),0,false);
            var target=Ship(new Vector3(0,4600,0),1,true,2);
            var world=Coordinator(target,out var actor,out var flares);
            projectiles.Fire(owner,IncomingMissile(100),target.CorePhysicsPosition-Vector3.forward*140,Vector3.forward,null,default,target);
            React(world,actor,0);React(world,actor,.6);
            Assert.That(flares.Bursts.Count,Is.EqualTo(1));Assert.That(actor.countermeasures.ReadyCount,Is.EqualTo(1));
            React(world,actor,.8);
            Assert.That(flares.Bursts.Count,Is.EqualTo(1),"A single decision cannot empty every installed launcher.");
            React(world,actor,1.2);
            Assert.That(flares.Bursts.Count,Is.EqualTo(2));Assert.That(actor.countermeasures.ReadyCount,Is.Zero);
            React(world,actor,3);
            Assert.That(flares.Bursts.Count,Is.EqualTo(2));
            Assert.That(actor.countermeasures.GetCooldownRemaining("flare"),Is.EqualTo(10));
            Assert.That(actor.countermeasures.GetCooldownRemaining("flare-1"),Is.EqualTo(10));
        }

        [Test] public void MissingLauncherNeverCreatesAFreeAiBurst()
        {
            var owner=Ship(new Vector3(0,4700,-1000),0,false);
            var target=Ship(new Vector3(0,4700,0),1,false);
            var world=Coordinator(target,out var actor,out var flares);
            projectiles.Fire(owner,IncomingMissile(240),target.CorePhysicsPosition-Vector3.forward*100,Vector3.forward,null,default,target);
            for(int i=0;i<20;i++)React(world,actor,i*.1);
            Assert.That(flares.Bursts,Is.Empty);Assert.That(actor.countermeasures.LauncherCount,Is.Zero);
        }

        RangeCombatWorld Coordinator(FlightShipRuntime target,out RangeCombatWorld.Actor actor,out FlareWorld flares)
        {
            flares=Own(new GameObject("Anticipatory AI flares")).AddComponent<FlareWorld>();flares.Initialize();projectiles.Flares=flares;
            var countermeasures=target.gameObject.AddComponent<ShipCountermeasureController>();countermeasures.Initialize(target,flares);
            var world=Own(new GameObject("Anticipatory AI coordinator")).AddComponent<RangeCombatWorld>();
            Set(world,"rules",rules);Set(world,"flightTuning",tuning);Set(world,"projectiles",projectiles);
            actor=new RangeCombatWorld.Actor{ship=target,countermeasures=countermeasures};return world;
        }
        WeaponDefinition IncomingMissile(float speed)
        {
            var missile=Own(ScriptableObject.CreateInstance<WeaponDefinition>());missile.delivery=WeaponDelivery.GuidedMissile;
            missile.projectileSpeed=missile.missileLaunchSpeed=speed;missile.missileAcceleration=0;missile.range=2000;
            missile.missileTurnDegrees=110;missile.missileTurnAcceleration=360;missile.missileSeekerConeDegrees=60;missile.missileSeekerLossSeconds=.3f;
            return missile;
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

        FlightShipRuntime Ship(Vector3 position,int team,bool launcher,int launcherCount=1)
        {
            var root=Own(new GameObject("AI flare fixture"));root.transform.position=position;
            var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(database);
            var data=new BlueprintData{name="AI flare fixture"};
            data.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0)});
            if(launcher){
                data.parts.Add(new BlockRecord{id="flare",shape=BlockShape.FlareLauncher,position=new Vector3(0,1.5f,0)});
                for(int i=1;i<launcherCount;i++){
                    data.parts.Add(new BlockRecord{id="support-"+i,shape=BlockShape.Cube,position=new Vector3(i,.5f,0),rotation=Quaternion.identity});
                    data.parts.Add(new BlockRecord{id="flare-"+i,shape=BlockShape.FlareLauncher,position=new Vector3(i,1.5f,0),rotation=Quaternion.identity});
                }
            }
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
