using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;
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
            Assert.That(power.cost,Is.GreaterThan(0));Assert.That(power.massKg,Is.GreaterThan(0));Assert.That(power.maxHp,Is.GreaterThan(0));
            Assert.That(power.storageJoules,Is.GreaterThan(0));Assert.That(power.generationWatts,Is.GreaterThan(0));Assert.That(power.icon,Is.Not.Null);
            var laser=database.Get(BlockShape.Laser);Assert.That(laser.weapon.sustainedPowerWatts,Is.GreaterThan(0));
            Assert.That(laser.occupiedCells.Length,Is.EqualTo(3));
            var flight=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            Assert.That(flight.boostPowerWatts,Is.EqualTo(140),"The current approved reference flight budget reserves 140 W for boost.");
            var bus=new ShipPowerBus();bus.Configure(power.storageJoules,power.generationWatts,true);
            Assert.That(bus.Allocate(1,laser.weapon.sustainedPowerWatts),Is.EqualTo(1),"A charged compact battery can initially supply one basic laser.");
            Assert.That(bus.Energy,Is.EqualTo(Mathf.Clamp(power.storageJoules+power.generationWatts-laser.weapon.sustainedPowerWatts,0,power.storageJoules)).Within(.001f));
            float boostedFireDemand=laser.weapon.sustainedPowerWatts+flight.boostPowerWatts;
            Assert.That(boostedFireDemand,Is.GreaterThan(power.generationWatts),"Boost plus fire must eventually consume the finite battery.");
            float beforeBoost=bus.Energy;
            Assert.That(bus.Allocate(1,boostedFireDemand),Is.EqualTo(1));
            Assert.That(bus.Energy,Is.EqualTo(beforeBoost+power.generationWatts-boostedFireDemand).Within(.001f));
            float secondsUntilEmpty=bus.Energy/(boostedFireDemand-power.generationWatts);
            Assert.That(bus.Allocate(secondsUntilEmpty,boostedFireDemand),Is.EqualTo(1).Within(.0001f));
            Assert.That(bus.Energy,Is.EqualTo(0).Within(.01f));
            Assert.That(bus.Allocate(1,boostedFireDemand),Is.EqualTo(power.generationWatts/boostedFireDemand).Within(.0001f),"The legacy proportional API still reports its generated-power fraction after storage is exhausted.");
            Assert.That(bus.Ratio,Is.LessThan(1));
            bus.Configure(0,0,true);Assert.That(bus.Allocate(1,boostedFireDemand),Is.Zero,"No source means no boost or weapon power.");
            var rules=AssetDatabase.LoadAssetAtPath<Starframe.Combat.BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");Assert.That(rules.unlimitedRangePower,Is.False);
        }
    }
}
