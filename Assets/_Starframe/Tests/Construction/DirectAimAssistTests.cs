using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;
namespace Starframe.Construction.Tests
{
    public sealed class DirectAimAssistTests
    {
        readonly List<Object> owned=new List<Object>();ProjectileWorld world;ModuleDatabase previous;
        [SetUp]public void Setup(){previous=BlockModules.ConfiguredDatabase;world=new ProjectileWorld();world.Initialize(null);}
        [TearDown]public void Cleanup(){world.Clear();BlockModules.Configure(previous);for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();}
        [TestCase(WeaponDelivery.Beam)][TestCase(WeaponDelivery.Projectile)]
        public void OffsetPlayerMuzzleHitsActualReticleHull(WeaponDelivery delivery)
        {
            var weapon=Weapon(delivery);var owner=Ship(Vector3.zero,0,weapon);var target=Ship(Vector3.forward*100,1);
            var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            guns.SetDirectAimRay(new Ray(new Vector3(0,.5f,-10),Vector3.forward));guns.Prepare();guns.Execute(.02f,1);
            world.Step(1.1f);Assert.That(target.DamageTaken,Is.GreaterThan(0));
        }
        [Test]public void NoAssistWithoutAnActualReticleHullHit()
        {
            var weapon=Weapon(WeaponDelivery.Beam);var owner=Ship(Vector3.zero,0,weapon);var target=Ship(Vector3.forward*100,1);
            var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            guns.SetDirectAimRay(new Ray(new Vector3(1.2f,.5f,-10),Vector3.forward));guns.Prepare();guns.Execute(.02f,1);
            Assert.That(target.DamageTaken,Is.Zero,"No nearby-target magnetism when the crosshair misses the hull.");
        }
        [Test]public void EnemyDoesNotReceivePlayerConvergence()
        {
            var weapon=Weapon(WeaponDelivery.Beam);var owner=Ship(Vector3.zero,1,weapon);var target=Ship(Vector3.forward*100,0);
            var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            guns.SetDirectAimRay(new Ray(new Vector3(0,.5f,-10),Vector3.forward));guns.Prepare();guns.Execute(.02f,1);
            Assert.That(target.DamageTaken,Is.Zero);
        }
        [Test]public void CameraOcclusionPreventsSelectingHiddenHull()
        {
            var owner=Ship(Vector3.zero,0,Weapon(WeaponDelivery.Beam));Ship(Vector3.forward*100,1);
            Box(new Vector3(0,.5f,50),Vector3.one*2);
            Assert.That(new DirectAimAssist().TryGetPoint(owner,new Ray(new Vector3(0,.5f,-10),Vector3.forward),out _),Is.False);
        }
        [Test]public void MuzzlePathStillHitsSceneryBeforeReticleTarget()
        {
            var weapon=Weapon(WeaponDelivery.Beam);var owner=Ship(Vector3.zero,0,weapon);var target=Ship(Vector3.forward*100,1);
            Box(new Vector3(1.5f,.5f,50),new Vector3(.8f,2,2));
            var guns=new ShipWeapons();guns.Initialize(owner,world);guns.FireRequested=true;
            guns.SetDirectAimRay(new Ray(new Vector3(0,.5f,-10),Vector3.forward));guns.Prepare();guns.Execute(.02f,1);
            Assert.That(target.DamageTaken,Is.Zero,"Convergence cannot bypass the ordinary projectile/beam collision query.");
        }
        [TestCase(20,100,200)][TestCase(1,-100,200)][TestCase(1,100,50)]
        public void ExcessiveAngleBehindOrOutsideRangeLeavesDirectionUnchanged(float x,float z,float range)
        {Assert.That(DirectAimAssist.Converge(Vector3.zero,Vector3.forward,new Vector3(x,0,z),range),Is.EqualTo(Vector3.forward));}
        void Box(Vector3 position,Vector3 scale){var go=Own(new GameObject("Aim obstruction"));go.transform.position=position;go.AddComponent<BoxCollider>().size=scale;Physics.SyncTransforms();}
        FlightShipRuntime Ship(Vector3 position,int team,WeaponDefinition weapon=null)
        {
            var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Cube,BlockShape.Laser})
            {
                var d=Own(ScriptableObject.CreateInstance<ModuleDefinition>());d.shape=shape;d.maxHp=1000;d.massKg=100;d.occupiedCells=new[]{Vector3Int.zero};
                if(shape==BlockShape.Laser){d.weapon=weapon;d.category=ModuleCategory.Weapon;d.muzzleLocalPosition=new Vector3(0,0,.6f);}db.modules.Add(d);
            }
            var go=Own(new GameObject("Aim assist test"));go.transform.position=position;
            var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(db);var data=new BlueprintData{name="Aim assist fixture"};
            data.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0),rotation=Quaternion.identity});
            if(weapon)
            {
                // A real connected three-cell outrigger, with the muzzle still three metres off the reticle ray.
                for(int x=1;x<=3;x++)data.parts.Add(new BlockRecord{id="bridge"+x,shape=BlockShape.Cube,position=new Vector3(x,.5f,0),rotation=Quaternion.identity});
                // Weapon connectivity requires its bottom mounting face, not side contact with the bridge.
                data.parts.Add(new BlockRecord{id="gun",shape=BlockShape.Laser,position=new Vector3(3,1.5f,0),rotation=Quaternion.identity});
            }
            assembly.Import(data);var runtime=go.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,assembly.Export(data.name),tuning);runtime.Team=team;
            Physics.SyncTransforms();return runtime;
        }
        WeaponDefinition Weapon(WeaponDelivery delivery){var w=Own(ScriptableObject.CreateInstance<WeaponDefinition>());w.delivery=delivery;w.range=200;w.damage=40;w.projectileDiameter=.2f;w.projectileSpeed=100;w.firstShotImmediate=true;return w;}
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
