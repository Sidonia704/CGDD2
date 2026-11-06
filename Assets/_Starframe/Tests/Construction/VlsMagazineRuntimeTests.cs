using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class VlsMagazineRuntimeTests
    {
        readonly List<Object> owned=new List<Object>();
        readonly List<WeaponHudState> states=new List<WeaponHudState>();
        ProjectileWorld world;ModuleDatabase previous;
        [SetUp]public void Setup(){previous=BlockModules.ConfiguredDatabase;world=new ProjectileWorld();world.Initialize(null);}
        [TearDown]public void Cleanup(){world.Clear();BlockModules.Configure(previous);for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();}

        [Test]public void ConventionalReloadMustFinishBeforeFreshAcquisitionBegins()
        {
            var weapon=Missile(false,1);weapon.missileLockSeconds=.4f;weapon.reloadSeconds=1;
            var owner=Ship(0,0,weapon);var target=Ship(1,60);var guns=Guns(owner);
            guns.Target=target;guns.HasValidTargetLock=true;guns.FireRequested=true;
            Tick(guns,6,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(1));
            Assert.That(guns.MissileLockProgress,Is.Zero);
            guns.FireRequested=false;
            while(guns.MissileReloadRemaining>0)Tick(guns,1,.05f);
            Assert.That(guns.ReadyMissileCount,Is.EqualTo(1));Assert.That(guns.MissileLockProgress,Is.Zero);
            guns.FireRequested=true;Tick(guns,3,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(1));
            Tick(guns,2,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(2));
        }

        [Test]public void IndependentMagazinesBalanceWholeGroupAndKeepPerRoundDamage()
        {
            var large=Missile(true,16);var small=Missile(true,8);
            var owner=Ship(0,0,large,small);var targets=new[]{Ship(1,60),Ship(1,75),Ship(1,90)};
            var guns=Guns(owner);guns.SetVlsVisibleTargets(targets);Tick(guns,41,.1f);
            Assert.That(guns.VlsTargets.Count,Is.EqualTo(3));Assert.That(guns.HasMissileWeapons,Is.False);
            guns.FireRequested=true;Tick(guns,5,.1f);
            guns.GetHudWeaponStates(states);Assert.That(states[0].AmmoRemaining,Is.EqualTo(8));Assert.That(states[1].AmmoRemaining,Is.Zero);
            Assert.That(guns.ShotsFired,Is.EqualTo(16),"Both modules independently launch eight rounds in half a second.");
            Tick(guns,5,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(24));Assert.That(guns.VlsTargets,Is.Empty);
            foreach(var target in targets)Assert.That(guns.IsVlsLockedTarget(target),Is.True,"In-flight seekers retain their target marker during launcher reload.");
            var counts=new Dictionary<FlightShipRuntime,int>();foreach(var target in targets)counts[target]=0;
            foreach(var missile in ActiveMissiles()){
                counts[Field<FlightShipRuntime>(missile,"target")]++;
                Assert.That(Field<float>(missile,"damage"),Is.EqualTo(30).Within(.001f),"Tube count must not divide the VLS per-round payload.");
                Assert.That(Vector3.Dot(Field<Vector3>(missile,"direction"),Vector3.up),Is.GreaterThan(.999f));
            }
            foreach(var count in counts.Values)Assert.That(count,Is.EqualTo(8));
        }

        [TestCase("expired")][TestCase("removed")][TestCase("respawned")][TestCase("diverted")]
        public void InFlightVlsMarkerEndsWhenItsSeekerNoLongerTracksThisTarget(string reason)
        {
            var weapon=Missile(true,8);weapon.missileGuidanceSeconds=.15f;
            var owner=Ship(0,0,weapon);var target=Ship(1,60);var guns=Guns(owner);
            Transform source=null;foreach(var part in owner.Parts)if(part.record.id=="gun0")source=part.view.transform;
            world.FireMissileTube(owner,weapon,source,Vector3.up*.6f,Vector3.up,target,0);
            Assert.That(guns.IsVlsLockedTarget(target),Is.True);
            if(reason=="expired")world.Step(.2f);
            else if(reason=="removed")world.Clear();
            else if(reason=="respawned")target.Respawn();
            else foreach(var missile in ActiveMissiles())missile.GetType().GetField("flareId").SetValue(missile,1);
            Assert.That(guns.IsVlsLockedTarget(target),Is.False);
        }

        [Test]public void VlsIgnoresConventionalShipWideStaggerClock()
        {
            var conventional=Missile(false,1);conventional.missileLockSeconds=0;
            var owner=Ship(0,0,Missile(true,8),conventional);var target=Ship(1,60);var guns=Guns(owner);
            guns.MissileLaunchesPerSecond=.1f;guns.Target=target;guns.HasValidTargetLock=true;guns.SetVlsVisibleTargets(new[]{target});
            Tick(guns,41,.1f);guns.FireRequested=true;Tick(guns,5,.1f);
            Assert.That(guns.ShotsFired,Is.EqualTo(9),"Eight VLS rounds remain independent of the conventional ten-second stagger.");
        }

        [Test]public void LateVisibleContactNeedsItsOwnFourSecondsAndLeavingViewClearsLock()
        {
            var owner=Ship(0,0,Missile(true,8));var first=Ship(1,60);var late=Ship(1,75);var guns=Guns(owner);
            guns.SetVlsVisibleTargets(new[]{first});Tick(guns,41,.1f);
            guns.SetVlsVisibleTargets(new[]{first,late});Tick(guns,20,.1f);
            Assert.That(guns.IsVlsLockedTarget(first),Is.True);Assert.That(guns.IsVlsLockedTarget(late),Is.False);
            Tick(guns,21,.1f);Assert.That(guns.IsVlsLockedTarget(late),Is.True);
            guns.SetVlsVisibleTargets(new[]{late});Assert.That(guns.IsVlsLockedTarget(first),Is.False);
            guns.SetVlsVisibleTargets(new[]{first,late});Assert.That(guns.IsVlsLockedTarget(first),Is.False);
        }

        [Test]public void FullMagazineReloadWaitsForLastRoundAndNeedsPoweredTwentyFourSeconds()
        {
            var owner=Ship(0,0,Missile(true,8));var guns=Guns(owner);guns.SetVlsVisibleTargets(new[]{Ship(1,60)});Tick(guns,41,.1f);
            guns.FireRequested=true;Tick(guns,4,.1f);guns.GetHudWeaponStates(states);
            Assert.That(states[0].AmmoRemaining,Is.EqualTo(2));Assert.That(states[0].ReloadRemaining,Is.Zero);
            Tick(guns,1,.1f);guns.GetHudWeaponStates(states);Assert.That(states[0].ReloadRemaining,Is.EqualTo(24).Within(.001f));
            Tick(guns,40,.1f,0);guns.GetHudWeaponStates(states);Assert.That(states[0].ReloadRemaining,Is.EqualTo(24).Within(.001f));
            guns.FireRequested=false;Tick(guns,239,.1f);guns.GetHudWeaponStates(states);Assert.That(states[0].AmmoRemaining,Is.Zero);
            Tick(guns,2,.1f);guns.GetHudWeaponStates(states);Assert.That(states[0].AmmoRemaining,Is.EqualTo(8));Assert.That(states[0].LockProgress,Is.LessThan(.04f));
        }

        [Test]public void DisablingGroupCancelsItsSalvoWithoutRefillingUnusedRounds()
        {
            var owner=Ship(0,0,Missile(true,8));var guns=Guns(owner);guns.SetVlsVisibleTargets(new[]{Ship(1,60)});Tick(guns,41,.1f);
            guns.FireRequested=true;Tick(guns,2,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(3));
            int group=guns.GetGroupId("gun0");guns.SetGroupEnabled(group,false);Tick(guns,10,.1f);guns.GetHudWeaponStates(states);
            Assert.That(guns.ShotsFired,Is.EqualTo(3));Assert.That(states[0].AmmoRemaining,Is.EqualTo(5));Assert.That(guns.VlsTargets,Is.Empty);
            guns.SetGroupEnabled(group,true);Tick(guns,20,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(3));
        }

        ShipWeapons Guns(FlightShipRuntime ship){var guns=new ShipWeapons();guns.Initialize(ship,world);return guns;}
        void Tick(ShipWeapons guns,int count,float dt,float power=1){for(int i=0;i<count;i++){guns.Prepare();guns.Execute(dt,power);}}
        IEnumerable ActiveMissiles()=>(IEnumerable)typeof(ProjectileWorld).GetField("missiles",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(world);
        static T Field<T>(object value,string field)=>(T)value.GetType().GetField(field,BindingFlags.Public|BindingFlags.Instance).GetValue(value);
        WeaponDefinition Missile(bool vls,int capacity)
        {
            var w=Own(ScriptableObject.CreateInstance<WeaponDefinition>());w.delivery=WeaponDelivery.GuidedMissile;w.family=vls?WeaponFamily.VerticalLaunchMissile:WeaponFamily.GuidedMissile;
            w.magazineSize=capacity;w.salvoLaunchesPerSecond=16;w.reloadSeconds=24;w.missileLockSeconds=vls?4:2;w.damage=30;w.range=200;w.requireTargetLock=true;
            w.missileLaunchOffsets=vls?new[]{Vector3.left*.15f,Vector3.right*.15f}:new[]{Vector3.zero};return w;
        }
        FlightShipRuntime Ship(int team,float depth,params WeaponDefinition[] weapons)
        {
            var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
            var shapes=new[]{BlockShape.Laser,BlockShape.Gun1,BlockShape.Gun2};
            var core=Own(ScriptableObject.CreateInstance<ModuleDefinition>());core.shape=BlockShape.Core;core.maxHp=1000;core.massKg=100;core.occupiedCells=new[]{Vector3Int.zero};db.modules.Add(core);
            var cube=Own(ScriptableObject.CreateInstance<ModuleDefinition>());cube.shape=BlockShape.Cube;cube.maxHp=100;cube.massKg=10;cube.occupiedCells=new[]{Vector3Int.zero};db.modules.Add(cube);
            for(int i=0;i<weapons.Length;i++){var d=Own(ScriptableObject.CreateInstance<ModuleDefinition>());d.shape=shapes[i];d.category=ModuleCategory.Weapon;d.maxHp=100;d.massKg=10;d.occupiedCells=new[]{Vector3Int.zero};d.weapon=weapons[i];d.muzzleLocalPosition=weapons[i].IsVls?Vector3.up*.6f:Vector3.forward*.6f;db.modules.Add(d);}
            var go=Own(new GameObject("VLS runtime fixture"));go.transform.position=new Vector3(0,18000,depth);var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(db);var data=new BlueprintData{name="VLS fixture"};
            data.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0),rotation=Quaternion.identity});
            for(int i=0;i<weapons.Length;i++){if(i>0)data.parts.Add(new BlockRecord{id="support"+i,shape=BlockShape.Cube,position=new Vector3(i,.5f,0),rotation=Quaternion.identity});data.parts.Add(new BlockRecord{id="gun"+i,shape=shapes[i],position=new Vector3(i,1.5f,0),rotation=Quaternion.identity});}
            assembly.Import(data);var ship=go.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export(data.name),tuning);ship.Team=team;Physics.SyncTransforms();return ship;
        }
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
