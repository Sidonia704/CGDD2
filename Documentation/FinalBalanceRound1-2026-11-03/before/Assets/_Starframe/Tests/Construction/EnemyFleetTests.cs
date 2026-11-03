using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.App;
using Starframe.Combat;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class EnemyFleetTests
    {
        const string Root="Assets/_Starframe/Data/Enemies/";
        ModuleDatabase database,previous;
        [SetUp] public void SetUp()
        {
            database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            Assert.That(database,Is.Not.Null);previous=BlockModules.ConfiguredDatabase;BlockModules.Configure(database);
        }
        [TearDown] public void TearDown()=>BlockModules.Configure(previous);

        [Test] public void SevenDistinctArchetypesHaveEditableIndependentCopies()
        {
            var ids=new HashSet<string>();var sizes=new Dictionary<EnemySize,int>();
            foreach(var id in new[]{"needle","viper","wasp","bastion","harrier","halberd","citadel"})
            {
                var value=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(Root+id+".asset");
                Assert.That(value,Is.Not.Null,id);Assert.That(ids.Add(value.id),Is.True,id);
                Assert.That(value.nameEn,Is.Not.Empty);Assert.That(value.nameZh,Is.Not.Empty);
                Assert.That(value.behavior,Is.Not.Null);Assert.That(value.score,Is.GreaterThan(0));
                sizes.TryGetValue(value.size,out int count);sizes[value.size]=count+1;
                var a=value.CreateBlueprint();var b=value.CreateBlueprint();a.parts.RemoveAt(0);
                Assert.That(b.parts.Count,Is.EqualTo(a.parts.Count+1),"Encounter copies must not mutate template data");
            }
            Assert.That(sizes[EnemySize.Small],Is.EqualTo(3));Assert.That(sizes[EnemySize.Large],Is.EqualTo(3));Assert.That(sizes[EnemySize.Capital],Is.EqualTo(1));
        }

        [TestCase("needle",false)][TestCase("viper",false)][TestCase("wasp",false)][TestCase("bastion",false)]
        [TestCase("harrier",false)][TestCase("halberd",false)][TestCase("citadel",false)][TestCase("citadel",true)]
        public void NativeFleetHasSupportedWeaponsClearNozzlesAndSixAxisFlight(string id,bool alternate=false)
        {
            var archetype=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(Root+id+".asset");
            Assert.That(archetype,Is.Not.Null);if(alternate)Assert.That(archetype.alternateBlueprint,Is.Not.Null);
            var data=archetype.CreateBlueprint(alternate);var report=DeploymentValidator.ValidateForRange(data,database);
            Assert.That(report.IsValid,Is.True,string.Join(",",report.Errors.Select(e=>e.key)));
            Assert.That(BlockConnectivity.IsConnected(data),Is.True,"All parts need real contact faces");
            Assert.That(data.parts.Count(p=>p.shape==BlockShape.Core),Is.EqualTo(1));
            Assert.That(data.parts.Count,Is.LessThanOrEqualTo(archetype.size==EnemySize.Capital?1150:archetype.size==EnemySize.Large?460:160));
            var cells=new Dictionary<Vector3Int,BlockRecord>();foreach(var p in data.parts)foreach(var cell in BlockModules.BodyCells(p))cells.Add(cell,p);
            var clearance=new WeaponClearance();var states=new List<ShipPartState>();float generation=0,storage=0;
            foreach(var p in data.parts)
            {
                var definition=database.Get(p.shape);generation+=definition.generationWatts;storage+=definition.storageJoules;
                states.Add(new ShipPartState{record=p,present=true,hp=definition.maxHp,maxHp=definition.maxHp,thrustAvailable=true,availableExhaustMask=-1});
                if(BlockModules.IsThruster(p.shape))foreach(var cell in BlockModules.ExhaustCells(p))Assert.That(cells.ContainsKey(cell),Is.False,"Blocked nozzle "+p.shape+" "+p.cell);
                if(!BlockModules.IsWeapon(p.shape)&&!BlockModules.IsArmor(p.shape))continue;
                Assert.That(BlockConnectivity.HasCompleteMount(p,data.parts),Is.True,"Unsupported "+p.shape+" "+p.cell);
                if(!BlockModules.IsWeapon(p.shape))continue;
                foreach(var other in data.parts)if(other!=p)Assert.That(clearance.IsBlockedBy(p,definition,other,database.Get(other.shape).mesh),Is.False,"Blocked weapon "+p.shape+" "+p.cell+" by "+other.cell);
                if(definition.weapon)Assert.That(data.weaponAssignments.Any(a=>a.partId==p.id),Is.True,"Missing weapon group assignment");
            }
            Assert.That(generation,Is.GreaterThan(0));Assert.That(storage,Is.GreaterThan(0));
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            var performance=ShipPerformance.Calculate(states,Quaternion.identity,tuning);
            for(int axis=0;axis<3;axis++)
            {Assert.That(performance.accelerationPositive[axis],Is.GreaterThan(0));Assert.That(performance.accelerationNegative[axis],Is.GreaterThan(0));}
            Assert.That(performance.angularRate,Is.GreaterThan(0));
        }
    }
}
