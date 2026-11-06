using System.Collections.Generic;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class MapDestructionDisabledTests
    {
        readonly List<Object> owned=new List<Object>();
        ModuleDatabase previous;
        GameObject root;
        [SetUp]public void Setup(){previous=BlockModules.ConfiguredDatabase;root=Own(new GameObject("Indestructible map fixture"));root.transform.position=new Vector3(21000,23000,0);}
        [TearDown]public void Cleanup(){for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();BlockModules.Configure(previous);}
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
        SpaceMapRuntime Map()
        {
            var asset=Own(ScriptableObject.CreateInstance<VoxelAssetDefinition>());
            asset.cells.Add(new VoxelCell{position=Vector3Int.zero});asset.cells.Add(new VoxelCell{position=Vector3Int.forward});
            var definition=Own(ScriptableObject.CreateInstance<SpaceMapDefinition>());definition.instances.Add(new VoxelMapInstance{asset=asset,driftVelocity=Vector3.right*2});
            var map=root.AddComponent<SpaceMapRuntime>();map.definition=definition;map.Build();Physics.SyncTransforms();return map;
        }
        [Test]public void ImmutableMapKeepsDynamicCollisionAndDriftWithoutDestructionApi()
        {
            var map=Map();var body=map.Structures[0];
            Assert.That(typeof(VoxelStructureRuntime).GetMethod("ApplyDamage"),Is.Null);
            Assert.That(typeof(VoxelStructureRuntime).GetMethod("GetHealth"),Is.Null);
            int revision=body.RenderRevision,geometry=body.GeometryBuildCount,collision=body.ColliderBuildCount;
            float mass=body.Body.mass;
            Assert.That(body.AliveCount,Is.EqualTo(2));
            Assert.That(body.RenderRevision,Is.EqualTo(revision));Assert.That(body.GeometryBuildCount,Is.EqualTo(geometry));Assert.That(body.ColliderBuildCount,Is.EqualTo(collision));
            Assert.That(body.Body.isKinematic,Is.False);Assert.That(body.Body.useGravity,Is.False);Assert.That(body.Body.mass,Is.EqualTo(mass));Assert.That(body.Body.linearVelocity.x,Is.EqualTo(2));
            Assert.That(body.GetComponents<Collider>().Length,Is.GreaterThan(0));Assert.That(map.Structures.Count,Is.EqualTo(1));
        }
        [TestCase(WeaponDelivery.Projectile)][TestCase(WeaponDelivery.Beam)][TestCase(WeaponDelivery.GuidedMissile)]
        public void WeaponsHitSolidSceneryWithWhiteFeedbackWithoutDestroyingCells(WeaponDelivery delivery)
        {
            var map=Map();var body=map.Structures[0];var owner=Ship(Vector3.right*100,0);var target=Ship(Vector3.forward*10,1);
            var weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());weapon.delivery=delivery;weapon.damage=100000;weapon.range=50;weapon.projectileDiameter=.1f;
            weapon.projectileSpeed=weapon.missileLaunchSpeed=100;weapon.penetrationDepth=40;weapon.explosionRadius=2;
            var world=new ProjectileWorld();world.Initialize(null);int feedback=0,impacts=0;
            world.HitFeedbackConfirmed+=(ship,scenery)=>{Assert.That(ship,Is.SameAs(owner));Assert.That(scenery,Is.True);feedback++;};world.Impact+=_=>impacts++;
            try{
                world.Fire(owner,weapon,root.transform.position+new Vector3(0,.1f,-5),Vector3.forward,null,default,target);
                for(int i=0;i<100&&world.ActiveCount>0;i++)world.Step(.02f);
                Assert.That(world.ActiveCount,Is.Zero);Assert.That(feedback,Is.EqualTo(1));Assert.That(impacts,Is.GreaterThan(0));
                Assert.That(body.AliveCount,Is.EqualTo(2));Assert.That(owner.DamageDealt,Is.Zero);Assert.That(target.DamageTaken,Is.Zero);
            }finally{world.Clear();}
        }
        FlightShipRuntime Ship(Vector3 offset,int team)
        {
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            var go=new GameObject("Damage barrier ship");go.transform.SetParent(root.transform,false);go.transform.localPosition=offset;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity);
            var ship=go.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export("Barrier fixture"),tuning);ship.Team=team;Physics.SyncTransforms();return ship;
        }
    }
}
