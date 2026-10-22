using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
namespace Starframe.Tests
{
    public sealed class VisualPowerAssetTests
    {
        [Test]public void CoreVisualIsSmallerButEveryColliderUsesCompleteCells()
        {
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");var root=new GameObject("Full grid collision contract");
            try{var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(database);
                foreach(BlockShape shape in System.Enum.GetValues(typeof(BlockShape))){var bounds=assembly.Collision(shape).bounds;var cells=BlockModules.LocalCells(shape);Assert.That(bounds.size.x*bounds.size.y*bounds.size.z,Is.EqualTo(cells.Length).Within(.0001f),shape+" must retain full cell collision.");}
                Assert.That(assembly.Solid(BlockShape.Core).bounds.size.x,Is.InRange(.8f,.9f));Assert.That(assembly.Collision(BlockShape.Core).bounds.size,Is.EqualTo(Vector3.one));
                var laser=assembly.Collision(BlockShape.Laser).bounds;Assert.That(laser.size,Is.EqualTo(new Vector3(1,1,3)));Assert.That(laser.center,Is.EqualTo(Vector3.forward));
                Assert.That(assembly.Place(BlockShape.Laser,new Vector3(0,.5f,0),Quaternion.identity),Is.True);
                Assert.That(assembly.CanPlace(BlockShape.Cube,new Vector3(0,.5f,2),Quaternion.identity),Is.False,"The third weapon cell must not accept another block.");
            }finally{Object.DestroyImmediate(root);BlockModules.Configure(null);}
        }
        [Test]public void ProductionDatabaseIncludesPowerAndFiniteEnergy()
        {
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");var power=database.Get(BlockShape.Power);
            Assert.That(power.massKg,Is.EqualTo(600));Assert.That(power.storageJoules,Is.EqualTo(500));Assert.That(power.generationWatts,Is.EqualTo(60));Assert.That(power.icon,Is.Not.Null);
            Assert.That(database.Get(BlockShape.Laser).weapon.sustainedPowerWatts,Is.EqualTo(50));Assert.That(database.Get(BlockShape.Laser).occupiedCells.Length,Is.EqualTo(3));
            var rules=AssetDatabase.LoadAssetAtPath<Starframe.Combat.BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");Assert.That(rules.unlimitedRangePower,Is.False);
        }
    }
}
