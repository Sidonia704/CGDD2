using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class FlareCountermeasureTests
    {
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        ModuleDatabase database;
        FlightTuning tuning;
        FlareDefinition flare;
        UnityEngine.Random.State random;
        T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
        [SetUp]public void Setup()
        {
            random=UnityEngine.Random.state;
            database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            flare=Own(ScriptableObject.CreateInstance<FlareDefinition>());
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Cube,BlockShape.FlareLauncher})
            {
                var definition=Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape=shape;definition.massKg=100;definition.maxHp=80;
                if(shape==BlockShape.FlareLauncher){definition.category=ModuleCategory.Weapon;definition.countermeasure=flare;}
                database.modules.Add(definition);
            }
            BlockModules.Configure(database);tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
        }
        [TearDown]public void Cleanup()
        {
            BlockModules.Configure(null);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();UnityEngine.Random.state=random;
        }
        static BlockRecord Part(BlockShape shape,Vector3Int cell,Quaternion rotation)=>new BlockRecord{id=Guid.NewGuid().ToString("N"),shape=shape,position=(Vector3)cell+Vector3.up*.5f,rotation=rotation};
        (ShipAssembly assembly,FlightShipRuntime ship,ShipCountermeasureController controller,FlareWorld world) Fixture(bool two=false)
        {
            var host=Own(new GameObject("Countermeasure ship"));var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,-.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.FlareLauncher,Vector3.up*.5f,Quaternion.identity),Is.True,assembly.PlacementFailure);
            if(two)
            {
                Assert.That(assembly.Place(BlockShape.Cube,new Vector3(1,-.5f,0),Quaternion.identity),Is.True);
                Assert.That(assembly.Place(BlockShape.FlareLauncher,new Vector3(1,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            }
            var ship=host.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export("Flare fixture"),tuning);
            var world=Own(new GameObject("Countermeasure world")).AddComponent<FlareWorld>();world.Initialize();
            var controller=host.AddComponent<ShipCountermeasureController>();controller.Initialize(ship,world);
            return(assembly,ship,controller,world);
        }

        [Test]public void SavedShapeAndActionNumbersStayStableAndOldBindingsGainTabWithoutChangingF()
        {
            Assert.That((int)BlockShape.Thruster6,Is.EqualTo(36));Assert.That((int)BlockShape.FlareLauncher,Is.EqualTo(37));
            Assert.That((int)GameAction.TargetLock,Is.EqualTo(9));Assert.That((int)GameAction.Countermeasure,Is.EqualTo(10));
            var old=GameInput.DefaultBindings().Where(b=>b.action!=GameAction.Countermeasure).ToArray();
            old.Single(b=>b.action==GameAction.TargetLock).path="<Keyboard>/g";
            var normalized=GameInput.Normalize(old);
            Assert.That(normalized.Single(b=>b.action==GameAction.TargetLock).path,Is.EqualTo("<Keyboard>/g"));
            Assert.That(normalized.Single(b=>b.action==GameAction.Countermeasure).path,Is.EqualTo("<Keyboard>/tab"));
            Assert.That(GameInput.DefaultBindings().Single(b=>b.action==GameAction.TargetLock).path,Is.EqualTo("<Keyboard>/f"));
        }
        [Test]public void FlareDefaultsAreOneTwelveSparkSalvoWithTenSecondIndependentCooldown()
        {
            Assert.That(flare.burstCount,Is.EqualTo(12));Assert.That(flare.cooldownSeconds,Is.EqualTo(10));
            Assert.That(flare.spreadHalfAngleDegrees,Is.EqualTo(22));Assert.That(flare.emissionSpeed,Is.EqualTo(24));
            Assert.That(flare.lifetime,Is.EqualTo(3));Assert.That(flare.decoyChance,Is.EqualTo(.45f));Assert.That(flare.decoyRadius,Is.EqualTo(150));
            Assert.That(BlockModules.LocalCells(BlockShape.FlareLauncher),Is.EqualTo(new[]{Vector3Int.zero}));
        }
        [Test]public void FlareIsMountedEquipmentButNeverEntersPrimaryWeaponsOrAimProfiles()
        {
            var fixture=Fixture();Assert.That(BlockModules.IsWeapon(BlockShape.FlareLauncher),Is.True);
            var part=fixture.ship.Parts.Single(p=>p.record.shape==BlockShape.FlareLauncher);
            Assert.That(WeaponGroups.IsWeapon(part.record,database),Is.False);
            Assert.That(fixture.ship.BlueprintSnapshot.weaponAssignments,Is.Empty);
            var weapons=new ShipWeapons();weapons.Initialize(fixture.ship,new ProjectileWorld());weapons.FireRequested=true;weapons.Prepare();
            var profiles=new List<WeaponAimProfile>();weapons.GetAimProfiles(profiles);
            Assert.That(weapons.ActiveGuns,Is.Zero);Assert.That(profiles,Is.Empty);
        }
        [Test]public void EveryMountingRotationChecksOnlyTheOneCellWideUpwardColumnIncludingOtherWeapons()
        {
            var clearance=new WeaponClearance();
            foreach(var normal in new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.left,Vector3.forward,Vector3.back})for(int turn=0;turn<4;turn++)
            {
                var rotation=BlockPlacement.FaceRotation(normal,turn);var launch=Part(BlockShape.FlareLauncher,Vector3Int.zero,rotation);
                var up=Vector3Int.RoundToInt(rotation*Vector3.up);var side=Vector3Int.RoundToInt(rotation*Vector3.right);
                Assert.That(clearance.IsBlockedBy(launch,database.Get(BlockShape.FlareLauncher),Part(BlockShape.Cube,up*8,Quaternion.identity),null),Is.True);
                Assert.That(WeaponClearance.CountermeasurePathBlockedBy(launch,Part(BlockShape.Gun1,up*3,Quaternion.identity)),Is.True,"Another weapon body must block too.");
                Assert.That(WeaponClearance.CountermeasurePathBlockedBy(launch,Part(BlockShape.Cube,up*3+side,Quaternion.identity)),Is.False,"Side-by-side cells remain legal.");
                Assert.That(WeaponClearance.CountermeasurePathBlockedBy(launch,Part(BlockShape.Cube,-up,Quaternion.identity)),Is.False,"The support is below the launch mouth.");
            }
        }
        [TestCase(false)][TestCase(true)]public void AssemblyRejectsRoofBeforeOrAfterLauncherWithoutRejectingSideTower(bool roofFirst)
        {
            var host=Own(new GameObject("Flare roof placement"));var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,-.5f,0),Quaternion.identity),Is.True);
            if(!roofFirst)Assert.That(assembly.Place(BlockShape.FlareLauncher,Vector3.up*.5f,Quaternion.identity),Is.True);
            for(int y=-1;y<=2;y++)Assert.That(assembly.Place(BlockShape.Cube,new Vector3(1,y+.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            if(roofFirst)
            {
                Assert.That(assembly.Place(BlockShape.Cube,new Vector3(0,2.5f,0),Quaternion.identity),Is.True);
                Assert.That(assembly.CanPlace(BlockShape.FlareLauncher,Vector3.up*.5f,Quaternion.identity),Is.False);
            }
            else Assert.That(assembly.CanPlace(BlockShape.Cube,new Vector3(0,2.5f,0),Quaternion.identity),Is.False);
            Assert.That(assembly.PlacementFailure,Does.Contain("遮挡"));
        }
        [Test]public void SalvoSharesOneMovingBurstAndTwelvePooledVoxelsWithoutPhysicsComponents()
        {
            var fixture=Fixture();flare.spreadHalfAngleDegrees=0;
            var start=new Vector3(4,5,6);var inherited=new Vector3(17,-3,8);var up=Vector3.right;
            var burst=fixture.world.EmitBurst(fixture.ship,flare,start,up,inherited);int firstId=burst.Id;
            Assert.That(fixture.world.Bursts.Count,Is.EqualTo(1));Assert.That(fixture.world.ActiveSparkCount,Is.EqualTo(12));
            Assert.That(burst.OwnerLifeId,Is.EqualTo(fixture.ship.LifeId));
            Assert.That(fixture.world.GetComponentsInChildren<Rigidbody>(),Is.Empty);Assert.That(fixture.world.GetComponentsInChildren<Collider>(),Is.Empty);
            fixture.world.Step(.5f);var expected=start+(inherited+up*24)*.5f;
            Assert.That(Vector3.Distance(burst.Position,expected),Is.LessThan(.001f));
            foreach(var renderer in fixture.world.GetComponentsInChildren<MeshRenderer>())
            {
                var ejection=(renderer.transform.position-start)/.5f-inherited;
                Assert.That(Vector3.Dot(ejection.normalized,up),Is.GreaterThanOrEqualTo(Mathf.Cos(22*Mathf.Deg2Rad)-.0001f));
            }
            fixture.world.Step(2.5f);Assert.That(burst.Active,Is.False);Assert.That(fixture.world.Bursts,Is.Empty);Assert.That(fixture.world.ActiveSparkCount,Is.Zero);
            var next=fixture.world.EmitBurst(fixture.ship,flare,start,up,inherited);
            Assert.That(next.Id,Is.GreaterThan(firstId));Assert.That(fixture.world.PooledSparkCount,Is.EqualTo(12));
        }
        [Test]public void VisualSparksUseOriginalSpeedAndLaunchCone()
        {
            var fixture=Fixture();var inherited=new Vector3(80,30,-12);var up=new Vector3(1,2,3).normalized;
            fixture.world.EmitBurst(fixture.ship,flare,Vector3.zero,up,inherited);fixture.world.Step(.25f);
            foreach(var renderer in fixture.world.GetComponentsInChildren<MeshRenderer>())
            {
                var ejection=renderer.transform.position/.25f-inherited;
                Assert.That(ejection.magnitude,Is.EqualTo(24).Within(.001f));
                Assert.That(Vector3.Dot(ejection.normalized,up),Is.GreaterThanOrEqualTo(Mathf.Cos(22*Mathf.Deg2Rad)-.0001f));
            }
        }
        [Test]public void VisualFlaresBurstAtVariedDistancesAndLeaveStationaryBrightPoints()
        {
            var fixture=Fixture();
            fixture.world.EmitBurst(fixture.ship,flare,Vector3.zero,Vector3.forward,Vector3.zero);
            fixture.world.Step(2);
            var renderers=fixture.world.GetComponentsInChildren<MeshRenderer>();
            var positions=renderers.Select(r=>r.transform.position).ToArray();
            Assert.That(positions.Select(p=>Mathf.RoundToInt(p.magnitude)).Distinct().Count(),Is.GreaterThan(1));
            Assert.That(positions.All(p=>p.z>0),Is.True);
            Assert.That(fixture.world.GetComponentsInChildren<TrailRenderer>(),Is.Empty);
            fixture.world.Step(.2f);
            for(int i=0;i<positions.Length;i++)Assert.That(Vector3.Distance(positions[i],renderers[i].transform.position),Is.LessThan(.001f));
            Assert.That(renderers.All(r=>r.enabled),Is.True);
            Assert.That(fixture.world.Bursts.Count,Is.EqualTo(1));
        }

        [TestCase(30)][TestCase(60)][TestCase(144)]
        public void MovingCameraSeesSmoothSparksAcrossPhysicsTicksAndBurst(int frameRate)
        {
            var fixture=Fixture();var inherited=new Vector3(350,70,-90);
            var burst=fixture.world.EmitBurst(fixture.ship,flare,Vector3.zero,Vector3.up,inherited);
            var present=typeof(FlareWorld).GetMethod("Present",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var renderers=fixture.world.GetComponentsInChildren<MeshRenderer>();
            var previous=new Vector3[renderers.Length];float previousTime=0;int ticks=0;
            for(int frame=1;frame<=frameRate*2.5f;frame++)
            {
                double now=(double)frame/frameRate;
                while((ticks+1)*.02<=now+1e-9){fixture.world.Step(.02f);ticks++;}
                var logical=burst.Position;
                present.Invoke(fixture.world,new object[]{(float)((now-ticks*.02)/.02)});
                float renderTime=Mathf.Max(0,(float)now-.02f);
                for(int i=0;i<renderers.Length;i++)
                {
                    var relative=renderers[i].transform.position-inherited*renderTime;
                    // Tracking inherited motion should leave only the original 24 m/s ejection.
                    Assert.That(Vector3.Distance(relative,previous[i]),Is.LessThanOrEqualTo(24*(renderTime-previousTime)+.004f));
                    var size=renderers[i].transform.localScale;
                    Assert.That(size.x,Is.EqualTo(size.y));Assert.That(size.y,Is.EqualTo(size.z));
                    previous[i]=relative;
                }
                Assert.That(burst.Position,Is.EqualTo(logical),"Rendering must not move the lure.");
                previousTime=renderTime;
            }
        }
        [Test]public void BurstFlashStartsContinuouslyInsteadOfPoppingToTripleSize()
        {
            var fixture=Fixture();fixture.world.EmitBurst(fixture.ship,flare,Vector3.zero,Vector3.up,Vector3.zero);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var visuals=(System.Collections.IList)typeof(FlareWorld).GetField("visuals",flags).GetValue(fixture.world);
            var visual=visuals[0];var sparks=(System.Collections.IList)visual.GetType().GetField("sparks").GetValue(visual);
            var spark=sparks[0];float burstAt=(float)spark.GetType().GetField("burstAt").GetValue(spark);
            var target=(Transform)spark.GetType().GetField("transform").GetValue(spark);
            fixture.world.Step(burstAt-.0001f);float before=target.localScale.x;
            fixture.world.Step(.0002f);
            Assert.That(Mathf.Abs(target.localScale.x-before),Is.LessThan(.001f));
            fixture.world.Step(.045f);Assert.That(target.localScale.x,Is.GreaterThan(before*2));
        }

        [Test]public void PressesRotateLaunchersAndEachWaitsForItsOwnCooldown()
        {
            var fixture=Fixture(true);var launchers=fixture.ship.Parts.Where(p=>p.record.shape==BlockShape.FlareLauncher).ToArray();
            Assert.That(fixture.controller.LauncherCount,Is.EqualTo(2));Assert.That(fixture.controller.ReadyCount,Is.EqualTo(2));
            Assert.That(fixture.controller.TryDeploy(),Is.True);Assert.That(fixture.world.Bursts.Count,Is.EqualTo(1));
            Assert.That(fixture.controller.ReadyCount,Is.EqualTo(1));Assert.That(fixture.controller.TryDeploy(),Is.True);
            Assert.That(fixture.world.Bursts.Count,Is.EqualTo(2));Assert.That(fixture.controller.ReadyCount,Is.Zero);Assert.That(fixture.controller.TryDeploy(),Is.False);
            Assert.That(fixture.controller.GetCooldownRemaining(launchers[0].record.id),Is.EqualTo(10).Within(.001f));
            fixture.ship.BeginStep(9.99f);Assert.That(fixture.controller.TryDeploy(),Is.False);
            fixture.ship.BeginStep(.02f);Assert.That(fixture.controller.TryDeploy(),Is.True);Assert.That(fixture.controller.ReadyCount,Is.EqualTo(1));
        }
        [Test]public void RuntimeBlockedLauncherSpendsNoCooldownAndFiresImmediatelyWhenCleared()
        {
            var fixture=Fixture();var launcher=fixture.ship.Parts.Single(p=>p.record.shape==BlockShape.FlareLauncher);
            var blocker=new ShipPartState{record=Part(BlockShape.Gun1,Vector3Int.up*4,Quaternion.identity),hp=80,maxHp=80};fixture.ship.Parts.Add(blocker);
            Assert.That(fixture.controller.TryDeploy(),Is.False);Assert.That(fixture.world.Bursts,Is.Empty);Assert.That(fixture.controller.GetCooldownRemaining(launcher.record.id),Is.Zero);
            blocker.present=false;Assert.That(fixture.controller.TryDeploy(),Is.True);Assert.That(fixture.world.ActiveSparkCount,Is.EqualTo(12));
        }
        [Test]public void ThreatForecastRejectsRecedingUnreachableAndOutOfConeBurstsWithoutSpendingCooldown()
        {
            var fixture=Fixture();var missile=Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            var core=fixture.ship.CorePhysicsPosition;
            Assert.That(fixture.controller.TryDeployForThreat(core-Vector3.forward*200,Vector3.back*250,missile,1),Is.False);
            Assert.That(fixture.controller.TryDeployForThreat(core-Vector3.forward*1000,Vector3.forward*100,missile,10),Is.False);
            missile.missileSeekerConeDegrees=.1f;
            Assert.That(fixture.controller.TryDeployForThreat(core-Vector3.forward*200,Vector3.forward*250,missile,1),Is.False);
            Assert.That(fixture.world.Bursts,Is.Empty);Assert.That(fixture.controller.ReadyCount,Is.EqualTo(1));
            missile.missileSeekerConeDegrees=60;
            Assert.That(fixture.controller.TryDeployForThreat(core-Vector3.forward*300,Vector3.forward*250,missile,1.2f),Is.True);
            Assert.That(fixture.world.Bursts.Count,Is.EqualTo(1));Assert.That(fixture.controller.ReadyCount,Is.Zero);
        }

        [Test]public void DestroyedLaunchersAreSkippedAndRespawnClearsIndependentCooldowns()
        {
            var fixture=Fixture(true);var first=fixture.ship.Parts.First(p=>p.record.shape==BlockShape.FlareLauncher);
            fixture.ship.ApplyDamage(first.record.id,10000);
            Assert.That(fixture.controller.LauncherCount,Is.EqualTo(1));Assert.That(fixture.controller.TryDeploy(),Is.True);Assert.That(fixture.controller.TryDeploy(),Is.False);
            fixture.ship.Respawn();Assert.That(fixture.controller.ReadyCount,Is.EqualTo(2));Assert.That(fixture.controller.TryDeploy(),Is.True);
        }
    }
}
