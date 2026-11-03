using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class RailgunFireTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){BlockModules.Configure(null);for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();}
        [TestCase(BlockShape.Railgun1)][TestCase(BlockShape.Railgun2)][TestCase(BlockShape.Railgun3)]
        public void ProductionRailgunsSpawnProjectilesWhenSupportedAndClear(BlockShape shape)
        {
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            var host=Own(new GameObject("Railgun fire fixture "+shape));var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            var cells=BlockModules.LocalCells(shape);int minX=cells[0].x,maxX=cells[0].x;
            foreach(var cell in cells){minX=Mathf.Min(minX,cell.x);maxX=Mathf.Max(maxX,cell.x);}
            int width=maxX-minX+1;
            for(int x=-1;x>=-width;x--)
                Assert.That(assembly.Place(BlockShape.Cube,new Vector3(x,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            var position=new Vector3(-width,1.5f,0);
            Assert.That(assembly.Place(shape,position,Quaternion.identity),Is.True,assembly.PlacementFailure);
            var blueprint=assembly.Export("railgun fire");
            var runtime=host.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);
            var world=new ProjectileWorld();world.Initialize(null);
            var weapons=new ShipWeapons();weapons.Initialize(runtime,world);
            weapons.FireRequested=true;
            float demand=weapons.Prepare();
            Assert.That(demand,Is.GreaterThan(0),shape+" should request its sustained power instead of being blocked.");
            weapons.Execute(.05f,1f);
            Assert.That(weapons.ShotsFired,Is.EqualTo(1),shape+" should fire immediately on the first press.");
            weapons.Execute(.1f,1f);
            Assert.That(weapons.ShotsFired,Is.EqualTo(1),shape+" must respect its cadence after the first shot.");
            for(int step=0;step<5;step++)weapons.Execute(1f,1f);
            Assert.That(weapons.ShotsFired,Is.GreaterThan(1),shape+" did not continue firing.");
            Assert.That(world.TotalSpawned,Is.GreaterThan(0),shape+" did not spawn a projectile.");
            world.Clear();
        }
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
