using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class SharedLeadPointTests
    {
        readonly List<Object> owned=new List<Object>();
        readonly List<WeaponAimProfile> profiles=new List<WeaponAimProfile>();
        ModuleDatabase previous;
        ProjectileWorld world;

        [SetUp]public void Setup()
        {previous=BlockModules.ConfiguredDatabase;world=new ProjectileWorld();world.Initialize(null);}

        [TearDown]public void Cleanup()
        {
            world.Clear();BlockModules.Configure(previous);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
            owned.Clear();profiles.Clear();
        }

        [Test]public void DistinctSizeDefinitionsWithOneSpeedShareOneCoreReferencedProfile()
        {
            var guns=Create(out var ship,new[]{600f,600f,600f},new[]{200f,400f,800f});
            guns.GetLeadProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(1));
            Assert.That(profiles[0].projectileSpeed,Is.EqualTo(600));
            Assert.That(profiles[0].range,Is.EqualTo(800));
            Assert.That(profiles[0].gunCount,Is.EqualTo(3));
            AssertVector(profiles[0].origin,ship.CorePhysicsPosition);
            guns.GetLeadProfiles(profiles,renderPose:true);
            AssertVector(profiles[0].origin,ship.CorePosition);
        }

        [TestCase(300f)][TestCase(599.75f)]
        public void ActualSpeedsStaySeparateAndSortedRegardlessOfMountOrderOrHeldGroup(float otherSpeed)
        {
            var guns=Create(out _,new[]{600f,otherSpeed,600f},new[]{200f,400f,800f});
            guns.UseGroupedInput=true;guns.SetGroupInput(guns.GetGroupId("small"),true,true);
            guns.GetLeadProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(2));
            Assert.That(profiles[0].projectileSpeed,Is.EqualTo(otherSpeed));
            Assert.That(profiles[0].gunCount,Is.EqualTo(1));
            Assert.That(profiles[0].range,Is.EqualTo(400));
            Assert.That(profiles[1].projectileSpeed,Is.EqualTo(600));
            Assert.That(profiles[1].gunCount,Is.EqualTo(2));
            Assert.That(profiles[1].range,Is.EqualTo(800));
            guns.ClearGroupInputs();guns.GetLeadProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(2),"Release must not change the available lead-speed set.");
            Assert.That(profiles[1].gunCount,Is.EqualTo(2));
        }

        [Test]public void DisablingLongerRangeMountsRecomputesSharedRangeWithoutHidingShortMounts()
        {
            var guns=Create(out _,new[]{600f,600f,600f},new[]{100f,300f,900f});
            guns.SetGroupEnabled(guns.GetGroupId("large"),false);guns.GetLeadProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(1));Assert.That(profiles[0].gunCount,Is.EqualTo(2));
            Assert.That(profiles[0].range,Is.EqualTo(300));
            guns.SetGroupEnabled(guns.GetGroupId("medium"),false);guns.GetLeadProfiles(profiles);
            Assert.That(profiles[0].gunCount,Is.EqualTo(1));Assert.That(profiles[0].range,Is.EqualTo(100));
            guns.SetGroupEnabled(guns.GetGroupId("small"),false);guns.GetLeadProfiles(profiles);
            Assert.That(profiles,Is.Empty);
        }

        [Test]public void ABlockedMuzzleDoesNotContributeToSharedCountOrMaximumRange()
        {
            var guns=Create(out _,new[]{600f,600f,600f},new[]{900f,300f,500f},blockSmall:true);
            Assert.That(guns.BlockedGuns,Is.EqualTo(1),"Fixture must exercise a real own-hull obstruction.");
            guns.GetLeadProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(1));Assert.That(profiles[0].gunCount,Is.EqualTo(2));
            Assert.That(profiles[0].range,Is.EqualTo(500));
        }

        [Test]public void DestroyingARepresentativeMountKeepsTheSharedLeadOriginAndSpeedStable()
        {
            var guns=Create(out var ship,new[]{600f,600f,600f},new[]{800f,800f,800f});
            guns.GetLeadProfiles(profiles);var before=profiles[0];
            var representative=ship.Parts.Find(part=>before.weapon!=null&&ship.Tuning.GetDefinition(part.record.shape).weapon==before.weapon);
            Assert.That(representative,Is.Not.Null,"The profile must identify a contributing projectile definition.");
            Assert.That(ship.ApplyDamage(representative.record.id,10000),Is.GreaterThan(0));
            guns.Prepare();guns.GetLeadProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(1));Assert.That(profiles[0].gunCount,Is.EqualTo(2));
            Assert.That(profiles[0].projectileSpeed,Is.EqualTo(before.projectileSpeed));
            Assert.That(profiles[0].range,Is.EqualTo(before.range));
            AssertVector(profiles[0].origin,before.origin);
            Assert.That(InterceptSolver.TrySolve(before.origin,before.origin+new Vector3(0,0,600),new Vector3(40,0,0),before.projectileSpeed,before.range,out var first,out _),Is.True);
            Assert.That(InterceptSolver.TrySolve(profiles[0].origin,before.origin+new Vector3(0,0,600),new Vector3(40,0,0),profiles[0].projectileSpeed,profiles[0].range,out var after,out _),Is.True);
            AssertVector(after,first);
        }

        [TestCase(WeaponDelivery.Beam)][TestCase(WeaponDelivery.GuidedMissile)]
        public void NonProjectileDeliveryDoesNotCreateAProjectileLeadProfile(WeaponDelivery delivery)
        {
            var guns=Create(out _,new[]{600f,600f,600f},new[]{200f,400f,800f},thirdDelivery:delivery);
            guns.GetLeadProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(1));Assert.That(profiles[0].gunCount,Is.EqualTo(2));
            Assert.That(profiles[0].range,Is.EqualTo(400));
        }

        [TestCase(-20,0,0)][TestCase(20,0,0)]
        [TestCase(0,-20,0)][TestCase(0,20,0)]
        [TestCase(0,0,-30)][TestCase(0,0,30)]
        public void FrontRearAndSideMuzzlesMeetTheirOwnMovingTargetIntercept(float x,float y,float z)
        {
            var reference=new Vector3(31,17,-40);var muzzle=reference+new Vector3(x,y,z);
            var target=reference+new Vector3(50,20,1000);var velocity=new Vector3(80,-20,15);
            const float speed=300,range=3000;
            Assert.That(InterceptSolver.TrySolve(reference,target,velocity,speed,range,out var referencePoint,out _),Is.True);
            Assert.That(InterceptSolver.TrySolve(muzzle,target,velocity,speed,range,out var muzzlePoint,out float time),Is.True);
            var forward=(referencePoint-reference).normalized;
            Assert.That(DirectAimAssist.TryCompensateLead(reference,muzzle,forward,target,velocity,speed,range,out var corrected),Is.True);
            Assert.That(Vector3.Angle(corrected,muzzlePoint-muzzle),Is.LessThan(.03f));
            Assert.That(Vector3.Distance(muzzle+corrected.normalized*speed*time,target+velocity*time),Is.LessThan(.05f));
            Assert.That(Vector3.Angle(forward,corrected),Is.LessThanOrEqualTo(DirectAimAssist.MaximumCorrectionDegrees+.01f));
        }

        [Test]public void CompensationPreservesThePlayersAimErrorInsteadOfSnappingToTheTarget()
        {
            var reference=Vector3.zero;var muzzle=new Vector3(-20,5,12);
            var target=new Vector3(0,0,1000);var velocity=new Vector3(60,15,0);
            Assert.That(InterceptSolver.TrySolve(reference,target,velocity,300,3000,out var referencePoint,out _),Is.True);
            Assert.That(InterceptSolver.TrySolve(muzzle,target,velocity,300,3000,out var muzzlePoint,out _),Is.True);
            var ideal=referencePoint.normalized;var forward=Quaternion.AngleAxis(2,Vector3.up)*ideal;
            Assert.That(DirectAimAssist.TryCompensateLead(reference,muzzle,forward,target,velocity,300,3000,out var corrected),Is.True);
            float error=Vector3.Angle(corrected,muzzlePoint-muzzle);
            Assert.That(error,Is.EqualTo(Vector3.Angle(forward,ideal)).Within(.03f));
            Assert.That(error,Is.GreaterThan(1.9f),"The reticle error must survive the parallax rotation.");
        }

        [TestCase(0,2000,0)][TestCase(100,2000,200)][TestCase(100,50,0)]
        public void NoSolutionOrOutOfRangeDoesNotProduceLeadCompensation(float speed,float range,float awaySpeed)
        {
            Assert.That(DirectAimAssist.TryCompensateLead(Vector3.zero,new Vector3(5,0,0),Vector3.forward,
                Vector3.forward*1000,Vector3.forward*awaySpeed,speed,range,out _),Is.False);
        }

        [Test]public void ExcessiveMountParallaxIsRejectedOrBoundedToFourDegrees()
        {
            var muzzle=new Vector3(500,0,0);var target=Vector3.forward*1000;
            bool compensated=DirectAimAssist.TryCompensateLead(Vector3.zero,muzzle,Vector3.forward,target,Vector3.zero,300,3000,out var corrected);
            if(!compensated)return;
            Assert.That(Vector3.Angle(Vector3.forward,corrected),Is.LessThanOrEqualTo(DirectAimAssist.MaximumCorrectionDegrees+.01f));
            Assert.That(Vector3.Angle(corrected,target-muzzle),Is.GreaterThan(10),"A large mounting offset must not bypass the correction limit.");
        }

        [Test]public void ActualShotsFromAllThreeSizesHitTheMovingTargetUsingOneSharedLead()
        {
            var guns=MovingShotFixture(out var owner,out var target,out var velocity);
            int hits=0;world.HitConfirmed+=source=>{if(source==owner)hits++;};
            guns.SetLeadTarget(target,velocity);guns.FireRequested=true;guns.Prepare();guns.Execute(.02f,1);
            Assert.That(guns.ShotsFired,Is.EqualTo(3));Assert.That(world.TotalSpawned,Is.EqualTo(3));
            var targetBody=target.GetComponent<Rigidbody>();const float dt=.002f;
            for(int step=0;step<2000&&world.ActiveCount>0;step++)
            {
                var position=target.transform.position+velocity*dt;
                target.transform.position=position;targetBody.position=position;Physics.SyncTransforms();world.Step(dt);
            }
            Assert.That(hits,Is.EqualTo(3),"Every actual projectile must meet the moving one-cell core despite different muzzle offsets.");
            Assert.That(target.DamageTaken,Is.EqualTo(120).Within(.001f));
        }

        [TestCase(false)][TestCase(true)]
        public void ClearingTheLeadTargetOrChangingItsLifePreventsStaleCorrectionOnActualShots(bool respawn)
        {
            var guns=MovingShotFixture(out var owner,out var target,out var velocity);
            guns.SetLeadTarget(target,velocity);
            if(respawn){int previousLife=target.LifeId;target.Respawn();Assert.That(target.LifeId,Is.Not.EqualTo(previousLife));}
            else guns.SetLeadTarget(null,Vector3.zero);
            int emissions=0;var forward=owner.GetComponent<Rigidbody>().rotation*Vector3.forward;
            world.MuzzleFlash+=(origin,direction,definition)=>
            {
                emissions++;Assert.That(Vector3.Angle(direction,forward),Is.LessThan(.03f),"An invalidated lead must leave the physical barrel direction intact.");
            };
            guns.FireRequested=true;guns.Prepare();guns.Execute(.02f,1);
            Assert.That(emissions,Is.EqualTo(3));Assert.That(world.TotalSpawned,Is.EqualTo(3));
        }

        ShipWeapons MovingShotFixture(out FlightShipRuntime owner,out FlightShipRuntime target,out Vector3 velocity)
        {
            var guns=Create(out owner,new[]{600f,600f,600f},new[]{2000f,2000f,2000f});
            var go=Own(new GameObject("Moving shared-lead target"));go.transform.position=owner.transform.position+Vector3.forward*800;
            var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(owner.Tuning.database);
            var data=new BlueprintData{name="Moving lead target"};Add(data,"core",BlockShape.Core,new Vector3(0,.5f,0));
            assembly.Import(data);target=go.AddComponent<FlightShipRuntime>();target.Initialize(assembly,assembly.Export(data.name),owner.Tuning);target.Team=1;
            velocity=new Vector3(80,0,0);Physics.SyncTransforms();
            Assert.That(InterceptSolver.TrySolve(owner.CorePhysicsPosition,target.CorePhysicsPosition,velocity,600,2000,out var point,out _),Is.True);
            var rotation=Quaternion.LookRotation(point-owner.CorePhysicsPosition);
            owner.GetComponent<Rigidbody>().rotation=rotation;owner.transform.rotation=rotation;Physics.SyncTransforms();
            return guns;
        }

        ShipWeapons Create(out FlightShipRuntime runtime,float[] speeds,float[] ranges,bool blockSmall=false,WeaponDelivery thirdDelivery=WeaponDelivery.Projectile)
        {
            var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Cube})
            {
                var definition=Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape=shape;definition.maxHp=1000;definition.massKg=100;definition.occupiedCells=new[]{Vector3Int.zero};db.modules.Add(definition);
            }
            var shapes=new[]{BlockShape.Gun1,BlockShape.Gun2,BlockShape.Gun3};
            for(int i=0;i<shapes.Length;i++)
            {
                var weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());
                weapon.name="Shared lead tier "+i;weapon.delivery=i==2?thirdDelivery:WeaponDelivery.Projectile;
                weapon.projectileSpeed=speeds[i];weapon.range=ranges[i];weapon.projectileDiameter=.1f;
                weapon.firstShotImmediate=true;weapon.damage=40;weapon.shotsPerSecond=1;
                var definition=Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape=shapes[i];definition.category=ModuleCategory.Weapon;definition.weapon=weapon;
                definition.maxHp=1000;definition.massKg=100;definition.muzzleLocalPosition=new Vector3(0,0,.6f);
                definition.muzzleClearanceDiameter=.1f;definition.occupiedCells=new Vector3Int[i+1];
                for(int cell=0;cell<=i;cell++)definition.occupiedCells[cell]=new Vector3Int(cell,0,0);
                db.modules.Add(definition);
            }
            var go=Own(new GameObject("Shared lead multi-size fixture"));go.transform.position=new Vector3(23,1500,-41);
            var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(db);
            var data=new BlueprintData{name="Shared lead fixture"};
            Add(data,"core",BlockShape.Core,new Vector3(0,.5f,0));
            for(int x=1;x<=11;x++)Add(data,"bridge"+x,BlockShape.Cube,new Vector3(x,.5f,0));
            var ids=new[]{"small","medium","large"};var positions=new[]{1,5,9};
            for(int i=0;i<ids.Length;i++)
            {
                Add(data,ids[i],shapes[i],new Vector3(positions[i],1.5f,0));
                data.weaponGroups.Add(new WeaponGroupRecord{id=i,name=ids[i],binding=WeaponInputBinding.MouseLeft()});
                data.weaponAssignments.Add(new WeaponGroupAssignment{partId=ids[i],groupId=i});
            }
            if(blockSmall)
            {
                Add(data,"obstructionSupport",BlockShape.Cube,new Vector3(1,.5f,1));
                Add(data,"obstruction",BlockShape.Cube,new Vector3(1,1.5f,1));
            }
            assembly.Import(data);runtime=go.AddComponent<FlightShipRuntime>();
            runtime.Initialize(assembly,assembly.Export(data.name),tuning);Physics.SyncTransforms();
            var guns=new ShipWeapons();guns.Initialize(runtime,world);guns.Prepare();return guns;
        }

        static void Add(BlueprintData data,string id,BlockShape shape,Vector3 position)
        {data.parts.Add(new BlockRecord{id=id,shape=shape,position=position,rotation=Quaternion.identity});}
        static void AssertVector(Vector3 actual,Vector3 expected)
        {Assert.That(Vector3.Distance(actual,expected),Is.LessThan(.0001f));}
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
