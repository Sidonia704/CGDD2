using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class WeaponVisualTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){BlockModules.Configure(null);for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();}
        [TestCase(BlockShape.Gun1)][TestCase(BlockShape.Gun2)][TestCase(BlockShape.Gun3)]
        public void MultiBarrelGunKeepsBothSidesAtTheirRealMuzzles(BlockShape shape)
        {
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            var host=Own(new GameObject("Multi barrel visual fixture "+shape));host.transform.SetPositionAndRotation(new Vector3(10,20,30),Quaternion.identity);
            var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            var module=database.Get(shape);var cells=BlockModules.LocalCells(shape);int maxX=cells[0].x;
            foreach(var cell in cells)maxX=Mathf.Max(maxX,cell.x);
            int width=maxX+1;
            for(int x=-1;x>=-width;x--)
                Assert.That(assembly.Place(BlockShape.Cube,new Vector3(x,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            var position=new Vector3(-width,1.5f,0);
            Assert.That(assembly.Place(shape,position,Quaternion.identity),Is.True,assembly.PlacementFailure);
            var blueprint=assembly.Export("multi barrel visual");
            var runtime=host.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);
            var poolObject=Own(new GameObject("Visual pool fixture"));var pool=poolObject.AddComponent<LaserVisualPool>();pool.Initialize(Shader.Find("Starframe/Combat/VoxelGlow"),1);
            var world=new ProjectileWorld();world.Initialize(pool);
            var weapons=new ShipWeapons();weapons.Initialize(runtime,world);
            var origins=new List<Vector3>();world.ShotFired+=(_,shotPosition)=>origins.Add(shotPosition);
            weapons.FireRequested=true;weapons.Prepare();weapons.Execute(.5f,1);
            int barrels=module.weapon.muzzleOffsets.Length;
            Assert.That(origins.Count,Is.GreaterThanOrEqualTo(barrels),shape+" should cycle every barrel in half a second.");
            Assert.That(Mathf.Abs(origins[0].x-origins[1].x),Is.GreaterThan(.05f),shape+" adjacent barrels must not collapse to the same side.");
            world.Step(.5f);
            Assert.That(world.ActiveCount,Is.EqualTo(origins.Count));
            Assert.That(pool.ActiveVisualCount,Is.EqualTo(origins.Count),"Each live projectile must keep its own visual.");
            var visuals=(IList)typeof(LaserVisualPool).GetField("visuals",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pool);
            var active=(IDictionary)typeof(LaserVisualPool).GetField("activeHandles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pool);
            float travel=module.weapon.projectileSpeed*.5f;
            var checkedCount=0;
            foreach(DictionaryEntry entry in active)
            {
                var visual=visuals[(int)entry.Value];var type=visual.GetType();
                var transform=(Transform)type.GetField("transform").GetValue(visual);
                var renderer=(MeshRenderer)type.GetField("renderer").GetValue(visual);
                var direction=(Vector3)type.GetField("direction").GetValue(visual);
                var lastLength=(float)type.GetField("lastLength").GetValue(visual);
                Assert.That(renderer.enabled,Is.True,"An active projectile must not have a hidden tracer.");
                var head=transform.position+direction*lastLength;
                bool matched=false;
                foreach(var origin in origins)if((head-(origin+Vector3.forward*travel)).sqrMagnitude<.05f){matched=true;break;}
                Assert.That(matched,Is.True,shape+" tracer head must stay on its projectile after the ship is offset in the world.");
                checkedCount++;
            }
            Assert.That(checkedCount,Is.EqualTo(origins.Count));
            world.Clear();
        }
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
