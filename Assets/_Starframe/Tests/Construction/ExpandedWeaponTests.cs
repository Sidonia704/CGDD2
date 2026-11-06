using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.App;
using Starframe.Flight;
using Starframe.Maps;

namespace Starframe.Construction.Tests
{
    public sealed class ExpandedWeaponTests
    {
        readonly List<Object> owned=new List<Object>();ProjectileWorld world;
        ModuleDatabase previous;Random.State randomState;
        [SetUp]public void Setup(){previous=BlockModules.ConfiguredDatabase;randomState=Random.state;world=new ProjectileWorld();world.Initialize(null);}
        [TearDown]public void Cleanup(){world.Clear();BlockModules.Configure(previous);for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();Random.state=randomState;}
        [TestCase(0,90)][TestCase(0,-90)][TestCase(0,180)][TestCase(90,0)][TestCase(-90,0)]
        public void OffAxisMissileCapturesAndHitsItsPrelockedTarget(float pitch,float yaw)
        {
            var profiles=UnityEditor.AssetDatabase.FindAssets("t:WeaponDefinition",new[]{"Assets/_Starframe"})
                .Select(g=>UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Where(w=>w.delivery==WeaponDelivery.GuidedMissile&&!w.IsVls).ToArray();
            Assert.That(profiles.Length,Is.EqualTo(3));
            for(int tier=0;tier<profiles.Length;tier++)
            {
                var weapon=Own(Object.Instantiate(profiles[tier]));
                float height=3500+tier*200;
                var owner=Ship(new Vector3(0,height,0),0,weapon);var target=Ship(new Vector3(0,height,600),1);
                var gun=owner.GetComponent<ShipAssembly>().Blocks.First(b=>b.Record.id=="gun");
                gun.transform.localRotation=Quaternion.Euler(pitch,yaw,0);Physics.SyncTransforms();
                world.FireMissileTube(owner,weapon,gun.transform,new Vector3(0,0,.6f),gun.transform.forward,target,0);
                for(int i=0;i<weapon.missileGuidanceSeconds/.02f&&world.ActiveMissileCount>0;i++)world.Step(.02f);
                Assert.That(target.DamageTaken,Is.GreaterThan(0),profiles[tier].name+": actual side/rear/vertical launch must hit.");
                world.Clear();
            }
        }

        [Test]public void DirectWeaponsFollowCoreFacingWhileMissilesAndCountermeasuresRemainRotatable()
        {
            foreach(BlockShape shape in System.Enum.GetValues(typeof(BlockShape)))
            {
                if(!BlockModules.IsWeapon(shape))continue;
                bool free=shape==BlockShape.Missile1||shape==BlockShape.Missile2||shape==BlockShape.Missile3||shape==BlockShape.VLS1||shape==BlockShape.VLS2||shape==BlockShape.FlareLauncher;
                Assert.That(BlockPlacement.MountFacesForward(shape,Quaternion.identity,Vector3.forward),Is.True,shape.ToString());
                Assert.That(BlockPlacement.MountFacesForward(shape,Quaternion.Euler(0,90,0),Vector3.forward),Is.EqualTo(free),shape.ToString());
                Assert.That(BlockPlacement.MountFacesForward(shape,Quaternion.Euler(0,180,0),Vector3.forward),Is.EqualTo(free),shape.ToString());
                Assert.That(BlockPlacement.MountFacesForward(shape,Quaternion.Euler(90,0,0),Vector3.forward),Is.EqualTo(free),shape.ToString());
                Assert.That(BlockPlacement.MountFacesForward(shape,Quaternion.Euler(0,0,90),Vector3.forward),Is.True,"Roll retains muzzle direction: "+shape);
            }
            var owner=Ship(new Vector3(0,3550,0),0,Weapon(WeaponDelivery.Projectile));
            var assembly=owner.GetComponent<ShipAssembly>();
            Assert.That(assembly.RotateCore("core"),Is.False,"Rotating the core cannot bypass the installation rule.");
            Assert.That(assembly.ShipForward,Is.EqualTo(Vector3.forward));
            Assert.That(assembly.CanPlace(BlockShape.Laser,new Vector3(2,1.5f,0),Quaternion.Euler(0,90,0)),Is.False);
            Assert.That(assembly.PlacementFailure,Is.EqualTo("非导弹武器必须朝向船头。"));
        }

        [Test]public void MissileRequiresTwoSecondsOnSameTargetAndReloadCannotBeBypassedByReleasing()
        {
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.missileLockSeconds=2;weapon.reloadSeconds=4;
            var owner=Ship(new Vector3(0,2400,0),0,weapon);var target=Ship(new Vector3(0,2400,50),1);
            var guns=new ShipWeapons();guns.Initialize(owner,world);guns.Target=target;guns.HasValidTargetLock=true;guns.FireRequested=true;
            Tick(guns,19,.1f);Assert.That(guns.ShotsFired,Is.Zero);
            Tick(guns,2,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(1));Assert.That(guns.ReadyMissileCount,Is.Zero);
            guns.FireRequested=false;Tick(guns,10,.1f);float remaining=guns.MissileReloadRemaining;
            guns.FireRequested=true;Tick(guns,2,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(1));Assert.That(guns.MissileReloadRemaining,Is.LessThan(remaining));
            guns.FireRequested=false;Tick(guns,40,.1f);Assert.That(guns.ReadyMissileCount,Is.EqualTo(1));
            guns.FireRequested=true;Tick(guns,1,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(1),"The reloaded launcher still needs a fresh acquisition.");
            Tick(guns,10,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(2));
        }
        [Test]public void ChangingTargetsRestartsMissileAcquisitionAndNoPowerPausesReload()
        {
            var weapon=Weapon(WeaponDelivery.GuidedMissile);var owner=Ship(new Vector3(0,2500,0),0,weapon);
            var first=Ship(new Vector3(0,2500,50),1);var second=Ship(new Vector3(20,2500,50),1);
            var guns=new ShipWeapons();guns.Initialize(owner,world);guns.Target=first;guns.HasValidTargetLock=true;guns.FireRequested=true;
            Tick(guns,15,.1f);guns.Target=second;Tick(guns,10,.1f);Assert.That(guns.ShotsFired,Is.Zero);
            Tick(guns,11,.1f);Assert.That(guns.ShotsFired,Is.EqualTo(1));
            float remaining=guns.MissileReloadRemaining;Tick(guns,10,.1f,0);
            Assert.That(guns.MissileReloadRemaining,Is.EqualTo(remaining).Within(.0001f));
            guns.HasValidTargetLock=false;Tick(guns,1,.1f);Assert.That(guns.MissileLockProgress,Is.Zero);
        }
        [Test]public void GuidedMissileTurnsTowardsOffsetTargetAndDealsExplosiveDamage()
        {
            var owner=Ship(new Vector3(0,2600,0),0);var target=Ship(new Vector3(9,2600,50),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.damage=80;weapon.missileTurnDegrees=160;weapon.missileLaunchSpeed=20;weapon.projectileSpeed=50;weapon.explosionRadius=3;
            int hits=0;world.HitConfirmed+=_=>hits++;
            world.Fire(owner,weapon,new Vector3(0,2600.5f,3),Vector3.forward,null,default,target);
            for(int i=0;i<100&&world.ActiveCount>0;i++)world.Step(.02f);
            Assert.That(target.DamageTaken,Is.GreaterThan(0));Assert.That(hits,Is.EqualTo(1));Assert.That(world.ActiveMissileCount,Is.Zero);
        }
        [Test]public void TwinRackFiresTwoMissilesWithOneVolleyDamageBudget()
        {
            var owner=Ship(new Vector3(0,2650,0),0);var target=Ship(new Vector3(0,2650,25),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.damage=80;weapon.missileLaunchSpeed=50;weapon.projectileSpeed=50;
            weapon.missileLaunchOffsets=new[]{Vector3.up*.15f,Vector3.down*.15f};
            world.Fire(owner,weapon,new Vector3(0,2650.5f,3),Vector3.forward,null,default,target);
            Assert.That(world.ActiveMissileCount,Is.EqualTo(2));Assert.That(world.TotalSpawned,Is.EqualTo(2));
            for(int i=0;i<50&&world.ActiveCount>0;i++)world.Step(.02f);
            Assert.That(target.DamageTaken,Is.GreaterThan(50));Assert.That(target.DamageTaken,Is.LessThanOrEqualTo(80.001f));
        }
        [Test]public void MissileBlastIsAbsorbedByShieldWithoutLeakingIntoHull()
        {
            var owner=Ship(new Vector3(0,2700,0),0);var target=Ship(new Vector3(0,2700,25),1,null,true);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.damage=100;weapon.missileLaunchSpeed=50;weapon.projectileSpeed=50;
            world.RegisterShields(target);float charge=target.Shields.Charge;
            world.Fire(owner,weapon,new Vector3(0,2700.5f,3),Vector3.forward,null,default,target);
            for(int i=0;i<50&&world.ActiveCount>0;i++)world.Step(.02f);
            Assert.That(target.Shields.Charge,Is.LessThan(charge));Assert.That(target.DamageTaken,Is.Zero);
        }
        [Test]public void ExpiredSeekerOnlyWarnsWhileItsCoastingPathCanStillHitTheOriginalTarget()
        {
            var owner=Ship(new Vector3(0,2900,0),0);var target=Ship(new Vector3(0,2900,100),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.missileGuidanceSeconds=.2f;weapon.missileLaunchSpeed=weapon.projectileSpeed=20;
            world.Fire(owner,weapon,new Vector3(0,2900.5f,3),Vector3.forward,null,default,target);
            Assert.That(world.TryGetIncomingThreat(target,out float distance,out float eta),Is.True);
            Assert.That(distance,Is.EqualTo(97).Within(.01f));Assert.That(eta,Is.GreaterThan(0));
            world.Step(.25f);
            Assert.That(world.TryGetIncomingThreat(target,out _,out _),Is.True,"An unguided missile on a collision path remains dangerous.");
            target.transform.position+=Vector3.right*40;Physics.SyncTransforms();world.Step(.02f);
            Assert.That(world.TryGetIncomingThreat(target,out _,out _),Is.False,"An expired seeker must not reacquire the dodging ship.");
            Assert.That(world.ActiveMissileCount,Is.EqualTo(1));
        }
        [Test]public void TargetRespawnCannotRestoreTheOldMissilesSeekerOrWarning()
        {
            var owner=Ship(new Vector3(0,2950,0),0);var target=Ship(new Vector3(0,2950,100),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.missileLaunchSpeed=weapon.projectileSpeed=20;
            world.Fire(owner,weapon,new Vector3(0,2950.5f,3),Vector3.forward,null,default,target);
            target.Respawn();world.Step(.14f);
            Assert.That(world.TryGetIncomingThreat(target,out _,out _),Is.False);
            Assert.That(world.ActiveMissileCount,Is.EqualTo(1));
        }
        [Test]public void FlareBurstRollsOnlyOnceForEachMissileAcrossLaterFrames()
        {
            var owner=Ship(new Vector3(0,3000,0),0);var target=Ship(new Vector3(0,3000,100),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.missileLaunchSpeed=weapon.projectileSpeed=20;
            var flares=Own(new GameObject("Missile flare roll fixture")).AddComponent<FlareWorld>();world.Flares=flares;
            var definition=Own(ScriptableObject.CreateInstance<FlareDefinition>());definition.decoyChance=.45f;
            flares.EmitBurst(target,definition,new Vector3(0,3000.5f,90),Vector3.up,Vector3.zero);
            world.Fire(owner,weapon,new Vector3(0,3000.5f,3),Vector3.forward,null,default,target);
            var previous=Random.state;
            try{
                int seed=0;
                do{Random.InitState(++seed);}while(Random.value<definition.decoyChance);
                var afterOneRoll=Random.state;
                Random.InitState(seed);world.Step(.14f);world.Step(.02f);world.Step(.02f);
                Assert.That(Random.state,Is.EqualTo(afterOneRoll),"A single burst's twelve sparks must not cause twelve or repeated-frame rolls.");
            }finally{Random.state=previous;}
        }
        [Test]public void SuccessfulFlareDiversionCoastsAfterExpiryWithoutReacquiringTheShip()
        {
            var owner=Ship(new Vector3(0,3050,0),0);var target=Ship(new Vector3(0,3050,100),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.missileLaunchSpeed=weapon.projectileSpeed=20;
            var flares=Own(new GameObject("Missile flare diversion fixture")).AddComponent<FlareWorld>();world.Flares=flares;
            var definition=Own(ScriptableObject.CreateInstance<FlareDefinition>());definition.decoyChance=1;
            flares.EmitBurst(target,definition,new Vector3(30,3050.5f,50),Vector3.up,Vector3.zero);
            world.Fire(owner,weapon,new Vector3(0,3050.5f,3),Vector3.forward,null,default,target);
            for(int i=0;i<40;i++)world.Step(.02f);
            Assert.That(world.TryGetIncomingThreat(target,out _,out _),Is.False,"The diverted missile's current path now misses the ship.");
            flares.Step(definition.lifetime+.1f);world.Step(.02f);
            for(int i=0;i<10;i++)world.Step(.02f);
            Assert.That(world.TryGetIncomingThreat(target,out _,out _),Is.False,"Expiry must not make the seeker snap back to the original ship.");
            Assert.That(world.ActiveMissileCount,Is.EqualTo(1));
        }
        [Test]public void MissilePassesEmptyProxyVolumeAndExplodesAtTheFirstOccupiedVoxel()
        {
            var owner=Ship(new Vector3(0,3100,0),0);var target=Ship(new Vector3(0,3100,100),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.missileLaunchSpeed=weapon.projectileSpeed=1000;
            var asset=Own(ScriptableObject.CreateInstance<VoxelAssetDefinition>());
            for(int x=-4;x<=4;x++)asset.cells.Add(new VoxelCell{position=new Vector3Int(x,0,4)});
            for(int z=0;z<4;z++){
                asset.cells.Add(new VoxelCell{position=new Vector3Int(-4,0,z)});
                asset.cells.Add(new VoxelCell{position=new Vector3Int(4,0,z)});
            }
            var scenery=Own(new GameObject("Hollow missile bounds proxy")).AddComponent<VoxelStructureRuntime>();
            scenery.transform.position=new Vector3(0,3100.5f,20);scenery.Initialize(asset,false,true,false,true);
            Assert.That(scenery.UsesBoundsCollisionProxy,Is.True);Physics.SyncTransforms();
            int explosions=0;Vector3 impact=default;world.MissileExplosion+=(point,_)=>{explosions++;impact=point;};
            world.Fire(owner,weapon,new Vector3(0,3100.5f,3),Vector3.forward,null,default,target);world.Step(.1f);
            Assert.That(explosions,Is.EqualTo(1));Assert.That(impact.z,Is.EqualTo(23.5f).Within(.03f));
            Assert.That(target.DamageTaken,Is.Zero);Assert.That(world.ActiveMissileCount,Is.Zero);
        }
        [Test]public void MultipleBarrelsAlternateActualShotOriginsWithoutMultiplyingCadence()
        {
            var weapon=Weapon(WeaponDelivery.Projectile);weapon.shotsPerSecond=10;weapon.muzzleOffsets=new[]{Vector3.left*.2f,Vector3.right*.2f};
            var owner=Ship(new Vector3(0,2750,0),0,weapon);var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            var origins=new List<Vector3>();world.ShotFired+=(_,position)=>origins.Add(position);
            Tick(guns,4,.1f);
            Assert.That(origins.Count,Is.EqualTo(4));Assert.That(origins[0].x,Is.EqualTo(-.2f).Within(.001f));Assert.That(origins[1].x,Is.EqualTo(.2f).Within(.001f));
            Assert.That(origins[2].x,Is.EqualTo(origins[0].x).Within(.001f));Assert.That(origins[3].x,Is.EqualTo(origins[1].x).Within(.001f));
        }
        [Test]public void BeamDamagesImmediatelyWithinReachAndStopsAtFirstSurface()
        {
            var owner=Ship(new Vector3(0,2800,0),0);var target=Ship(new Vector3(0,2800,8),1);var far=Ship(new Vector3(0,2800,18),1);
            var beam=Weapon(WeaponDelivery.Beam);beam.range=12;beam.damage=30;
            world.Fire(owner,beam,new Vector3(0,2800.5f,3),Vector3.forward);
            Assert.That(target.DamageTaken,Is.EqualTo(30).Within(.01f));Assert.That(far.DamageTaken,Is.Zero);Assert.That(world.ActiveCount,Is.Zero);
        }
        [Test]public void UnboundedBeamHitsBeyondOldSwordReachWithoutAFlightStep()
        {
            var owner=Ship(new Vector3(0,3500,0),0);var target=Ship(new Vector3(0,3500,600),1);
            var beam=Weapon(WeaponDelivery.Beam);beam.unboundedBeam=true;beam.range=45;beam.damage=30;
            world.Fire(owner,beam,new Vector3(0,3500.5f,3),Vector3.forward);
            Assert.That(target.DamageTaken,Is.EqualTo(30).Within(.01f));Assert.That(world.ActiveCount,Is.Zero);
        }
        [Test]public void UnboundedBeamStopsAtNearbyShieldAndCannotLeakToDistantHull()
        {
            var owner=Ship(new Vector3(0,3550,0),0);var shielded=Ship(new Vector3(0,3550,20),1,null,true);var far=Ship(new Vector3(0,3550,600),1);
            world.RegisterShields(shielded);float charge=shielded.Shields.Charge;
            var beam=Weapon(WeaponDelivery.Beam);beam.unboundedBeam=true;beam.range=45;beam.damage=30;
            world.Fire(owner,beam,new Vector3(0,3550.5f,3),Vector3.forward);
            Assert.That(shielded.Shields.Charge,Is.EqualTo(charge-30).Within(.01f));
            Assert.That(shielded.DamageTaken,Is.Zero);Assert.That(far.DamageTaken,Is.Zero);
        }
        [Test]public void MissileTubeStaggerLaunchesEightPerSecondWithoutLongFrameBurst()
        {
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.missileLockSeconds=0;weapon.reloadSeconds=10;
            weapon.missileLaunchOffsets=Enumerable.Range(0,16).Select(i=>Vector3.right*(i%4)*.05f).ToArray();
            var owner=Ship(new Vector3(0,3600,0),0,weapon);var target=Ship(new Vector3(0,3600,100),1);
            var guns=new ShipWeapons();guns.Initialize(owner,world);guns.Target=target;guns.HasValidTargetLock=true;
            Tick(guns,1,.001f);guns.FireRequested=true;Tick(guns,50,.02f);
            Assert.That(guns.MissileLaunchesPerSecond,Is.EqualTo(8));Assert.That(guns.ShotsFired,Is.EqualTo(8));
            Tick(guns,1,2);Assert.That(guns.ShotsFired,Is.EqualTo(9),"A long frame may launch one tube, never a catch-up burst.");
            Tick(guns,1,.02f);Assert.That(guns.ShotsFired,Is.EqualTo(9),"The following normal frame must not pay obsolete launch debt.");
        }
        [Test]public void GatlingGroupDisableCoolsItsExistingHeatInsteadOfResettingIt()
        {
            var weapon=Weapon(WeaponDelivery.Projectile);weapon.family=WeaponFamily.Gatling;weapon.shotsPerSecond=20;
            var owner=Ship(new Vector3(0,3650,0),0,weapon);var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            Tick(guns,300,.02f);
            var slots=(System.Collections.IList)typeof(ShipWeapons).GetField("guns",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(guns);
            var thermal=(GatlingThermalState)slots[0].GetType().GetField("thermal").GetValue(slots[0]);
            Assert.That(thermal.HeatFraction,Is.EqualTo(.75f).Within(.001f),"Six powered seconds use six of the default eight-second heat budget.");
            int group=guns.Groups[0].id;guns.SetGroupEnabled(group,false);Tick(guns,50,.02f);
            Assert.That(thermal.HeatFraction,Is.EqualTo(7f/12f).Within(.001f),"One disabled second removes one sixth of full heat without resetting it.");
            guns.SetGroupEnabled(group,true);Tick(guns,401,.02f);
            Assert.That(thermal.IsOverheated,Is.True);Assert.That(guns.Prepare(),Is.Zero,"An overheated mount must release power to higher priority systems.");
        }
        [TestCase(WeaponDelivery.Projectile,0,66)]
        [TestCase(WeaponDelivery.Projectile,1,30)]
        [TestCase(WeaponDelivery.Beam,0,66)]
        [TestCase(WeaponDelivery.Beam,1,30)]
        [TestCase(WeaponDelivery.GuidedMissile,0,21.25f)]
        [TestCase(WeaponDelivery.GuidedMissile,1,12.5f)]
        public void CategoryAndFactionDamageApplyExactlyOnce(WeaponDelivery delivery,int team,float expected)
        {
            var scale=Own(ScriptableObject.CreateInstance<CombatDamageTuning>());
            world.DamageTuning=scale;world.EnemyDamageMultiplier=.5f;
            var owner=Ship(new Vector3(0,3250,0),team);var target=Ship(new Vector3(0,3250,20),team==0?1:0);
            var weapon=Weapon(delivery);weapon.damage=100;weapon.projectileSpeed=weapon.missileLaunchSpeed=100;
            world.Fire(owner,weapon,new Vector3(0,3250.5f,3),Vector3.forward,null,default,target);
            for(int i=0;i<100&&world.ActiveCount>0;i++)world.Step(.01f);
            Assert.That(target.DamageTaken,Is.EqualTo(expected).Within(.05f));
        }
        [Test]public void MissileBreakingLastPointOfShieldCannotHurtHullButNextMissileCan()
        {
            var owner=Ship(new Vector3(0,3300,0),0);var target=Ship(new Vector3(0,3300,25),1,null,true);
            world.RegisterShields(target);var field=target.Shields.Fields[0];field.charge=1;
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.damage=100;weapon.projectileSpeed=weapon.missileLaunchSpeed=100;
            weapon.explosionRadius=8;
            world.Fire(owner,weapon,new Vector3(0,3300.5f,3),Vector3.forward,null,default,target);
            for(int i=0;i<100&&world.ActiveCount>0;i++)world.Step(.01f);
            Assert.That(field.charge,Is.Zero);Assert.That(target.DamageTaken,Is.Zero);
            world.Fire(owner,weapon,new Vector3(0,3300.5f,3),Vector3.forward,null,default,target);
            for(int i=0;i<100&&world.ActiveCount>0;i++)world.Step(.01f);
            Assert.That(target.DamageTaken,Is.GreaterThan(0));Assert.That(target.DamageTaken,Is.LessThanOrEqualTo(150.01f));
        }
        [Test]public void InFlightMissileKeepsItsLaunchDamageWhenTuningChanges()
        {
            var scale=Own(ScriptableObject.CreateInstance<CombatDamageTuning>());world.DamageTuning=scale;
            var owner=Ship(new Vector3(0,3350,0),0);var target=Ship(new Vector3(0,3350,25),1);
            var weapon=Weapon(WeaponDelivery.GuidedMissile);weapon.damage=100;weapon.projectileSpeed=weapon.missileLaunchSpeed=50;
            world.Fire(owner,weapon,new Vector3(0,3350.5f,3),Vector3.forward,null,default,target);
            scale.missileDamageMultiplier=10;weapon.damage=1000;
            for(int i=0;i<100&&world.ActiveCount>0;i++)world.Step(.01f);
            Assert.That(target.DamageTaken,Is.EqualTo(21.25f).Within(.05f));
        }
        [Test]public void NativeWeaponProfilesSelectTheirApprovedAudioCategory()
        {
            var owner=Ship(new Vector3(0,9000,0),0);
            var native=UnityEditor.AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            Assert.That(native,Is.Not.Null);owner.Tuning.database=native;
            var shapes=new[]{BlockShape.Gun1,BlockShape.Gun2,BlockShape.Gun3,BlockShape.Laser,BlockShape.Laser2,BlockShape.Laser3,
                BlockShape.Railgun1,BlockShape.Railgun2,BlockShape.Railgun3,BlockShape.Missile1,BlockShape.Missile2,BlockShape.Missile3,BlockShape.Sword1,BlockShape.Sword2};
            var cues=new[]{GameSound.Gatling,GameSound.Gatling,GameSound.Gatling,GameSound.LaserRapid,GameSound.LaserRapid,GameSound.LaserRapid,
                GameSound.RailSmall,GameSound.RailSmall,GameSound.RailLarge,GameSound.MissileShot,GameSound.MissileShot,GameSound.MissileShot,GameSound.DirectLaser,GameSound.DirectLaser};
            for(int i=0;i<shapes.Length;i++)Assert.That(GameAudioRuntime.ShotSound(owner,native.Get(shapes[i]).weapon),Is.EqualTo(cues[i]),shapes[i].ToString());
        }
        [Test]public void WeaponShotEventMatchesActualProjectileBeamAndMissileSpawns()
        {
            var owner=Ship(new Vector3(0,9100,0),0);var target=Ship(new Vector3(0,9100,50),1);
            var position=new Vector3(0,9100,3);int legacy=0;var emitted=new List<WeaponDefinition>();
            world.ShotFired+=(ship,origin)=>{Assert.That(ship,Is.SameAs(owner));legacy++;};
            world.WeaponShotFired+=(ship,origin,weapon)=>{Assert.That(ship,Is.SameAs(owner));Assert.That(origin,Is.EqualTo(position));emitted.Add(weapon);};
            var projectile=Weapon(WeaponDelivery.Projectile);var beam=Weapon(WeaponDelivery.Beam);var missile=Weapon(WeaponDelivery.GuidedMissile);
            world.Fire(owner,projectile,position,Vector3.zero);
            world.Fire(owner,missile,position,Vector3.forward);
            Assert.That(emitted,Is.Empty,"Rejected zero-direction and unlocked missile requests are silent.");
            world.Fire(owner,projectile,position,Vector3.forward);
            world.Fire(owner,beam,position,Vector3.forward);
            world.Fire(owner,missile,position,Vector3.forward,null,default,target);
            Assert.That(emitted,Is.EqualTo(new[]{projectile,beam,missile}));
            Assert.That(legacy,Is.EqualTo(3));Assert.That(world.TotalSpawned,Is.EqualTo(3));
        }
        [Test]public void RailWindupEmitsAudioBeforeProjectileAtConfiguredPeak()
        {
            var weapon=Weapon(WeaponDelivery.Projectile);weapon.family=WeaponFamily.Railgun;weapon.firstShotImmediate=true;
            weapon.shotsPerSecond=1;weapon.fireWindupSeconds=.08f;
            var owner=Ship(new Vector3(0,9200,0),0,weapon);var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            int starts=0;System.Action<FlightShipRuntime,WeaponDefinition,Vector3> handler=(source,definition,origin)=>{
                Assert.That(source,Is.SameAs(owner));Assert.That(definition,Is.SameAs(weapon));Assert.That(guns.ShotsFired,Is.Zero);starts++;
            };
            ShipWeapons.ShotWindupStarted+=handler;
            try{
                Tick(guns,1,.02f);Assert.That(starts,Is.EqualTo(1));Assert.That(guns.ShotsFired,Is.Zero);
                Tick(guns,3,.02f);Assert.That(guns.ShotsFired,Is.Zero,"No discharge before the 80 ms audio peak.");
                Tick(guns,1,.02f);Assert.That(guns.ShotsFired,Is.EqualTo(1));Assert.That(starts,Is.EqualTo(1));
            }finally{ShipWeapons.ShotWindupStarted-=handler;}
        }
        [TestCase("release")][TestCase("power")][TestCase("group")][TestCase("reset")]
        public void RailWindupCannotDischargeAfterCancellation(string reason)
        {
            var weapon=Weapon(WeaponDelivery.Projectile);weapon.family=WeaponFamily.Railgun;weapon.firstShotImmediate=true;
            weapon.shotsPerSecond=1;weapon.fireWindupSeconds=.08f;
            var owner=Ship(new Vector3(0,9300,0),0,weapon);var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            int cancelled=0;System.Action<FlightShipRuntime,WeaponDefinition> handler=(source,definition)=>cancelled++;
            ShipWeapons.ShotWindupCancelled+=handler;
            try{
                Tick(guns,1,.02f);
                if(reason=="release")guns.FireRequested=false;
                if(reason=="group")guns.SetGroupEnabled(guns.GetGroupId("gun"),false);
                if(reason=="reset")guns.Reset();
                Tick(guns,8,.02f,reason=="power"?0:1);
                Assert.That(guns.ShotsFired,Is.Zero);Assert.That(cancelled,Is.EqualTo(1),"Cancelled preparation must stop its audio exactly once.");
            }finally{ShipWeapons.ShotWindupCancelled-=handler;}
        }
        void Tick(ShipWeapons guns,int count,float dt,float power=1){for(int i=0;i<count;i++){guns.Prepare();guns.Execute(dt,power);}}
        FlightShipRuntime Ship(Vector3 position,int team,WeaponDefinition weapon=null,bool shield=false)
        {
            var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Laser,BlockShape.Shield}){
                var d=Own(ScriptableObject.CreateInstance<ModuleDefinition>());d.shape=shape;d.maxHp=1000;d.massKg=100;d.occupiedCells=new[]{Vector3Int.zero};
                if(shape==BlockShape.Laser){d.weapon=weapon;d.category=ModuleCategory.Weapon;d.muzzleLocalPosition=new Vector3(0,0,.6f);}
                if(shape==BlockShape.Shield&&shield){d.shieldCapacity=1000;d.shieldRadius=4;}db.modules.Add(d);
            }
            var go=Own(new GameObject("Expanded weapon test"));go.transform.position=position;
            var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(db);
            var data=new BlueprintData{name="Expanded weapon fixture"};
            data.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0),rotation=Quaternion.identity});
            if(weapon)data.parts.Add(new BlockRecord{id="gun",shape=BlockShape.Laser,position=new Vector3(0,1.5f,0),rotation=Quaternion.identity});
            if(shield)data.parts.Add(new BlockRecord{id="shield",shape=BlockShape.Shield,position=new Vector3(1,.5f,0),rotation=Quaternion.identity});
            assembly.Import(data);var runtime=go.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,assembly.Export(data.name),tuning);runtime.Team=team;
            Physics.SyncTransforms();return runtime;
        }
        WeaponDefinition Weapon(WeaponDelivery delivery){var w=Own(ScriptableObject.CreateInstance<WeaponDefinition>());w.delivery=delivery;w.range=200;w.damage=40;w.projectileDiameter=.2f;w.requireTargetLock=delivery==WeaponDelivery.GuidedMissile;return w;}
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}

