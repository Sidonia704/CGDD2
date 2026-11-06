using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;
namespace Starframe.Construction.Tests
{
    public sealed class BlueprintCostDisplayTests
    {
        [Test]public void CardPricesUseModuleAndMissionRulesRatherThanComponentCount()
        {
            var database=ScriptableObject.CreateInstance<ModuleDatabase>();var hull=ScriptableObject.CreateInstance<ModuleDefinition>();var weapon=ScriptableObject.CreateInstance<ModuleDefinition>();var mission=ScriptableObject.CreateInstance<MissionDefinition>();
            try{
                hull.shape=BlockShape.Cube;hull.category=ModuleCategory.Hull;hull.cost=0;
                weapon.shape=BlockShape.Laser;weapon.category=ModuleCategory.Weapon;weapon.cost=25;
                database.modules=new List<ModuleDefinition>{hull,weapon};
                var data=new BlueprintData{parts=new List<BlockRecord>{new BlockRecord{shape=BlockShape.Cube},new BlockRecord{shape=BlockShape.Laser},new BlockRecord{shape=BlockShape.Laser}}};
                Assert.That(BlueprintCostDisplay.Calculate(data,database),Is.EqualTo(50));
                weapon.cost=0;mission.useProvisionalPricing=true;mission.unpricedPartCost=75;
                Assert.That(BlueprintCostDisplay.Calculate(data,database,mission),Is.EqualTo(150));
                Assert.That(BlueprintCostDisplay.Format(31425,true),Is.EqualTo("COST 31,425"));
                Assert.That(BlueprintCostDisplay.Format(31425,false),Is.EqualTo("造价 31,425"));
            }finally{Object.DestroyImmediate(mission);Object.DestroyImmediate(weapon);Object.DestroyImmediate(hull);Object.DestroyImmediate(database);}
        }
        [Test]public void MissingOrInvalidEntryCannotBreakBlueprintGrid()
        {
            var database=ScriptableObject.CreateInstance<ModuleDatabase>();
            try{Assert.That(BlueprintCostDisplay.Calculate(null,database),Is.Zero);Assert.That(BlueprintCostDisplay.Calculate(new BlueprintData{parts=new List<BlockRecord>{null,new BlockRecord{shape=BlockShape.VLS1}}},database),Is.Zero);}
            finally{Object.DestroyImmediate(database);}
        }
    }
}
