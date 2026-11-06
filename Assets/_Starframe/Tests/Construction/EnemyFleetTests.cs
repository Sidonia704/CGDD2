using System.Collections.Generic;
using System.IO;
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

        [Test] public void ThirteenDistinctArchetypesHaveEditableIndependentCopies()
        {
            var ids=new HashSet<string>();var sizes=new Dictionary<EnemySize,int>();
            foreach(var id in new[]{"scout-pulse","scout-rail","scout-beam","scout-mixed","needle","viper","wasp","bastion","harrier","halberd","citadel","citadel-missile","sovereign"})
            {
                var value=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(Root+id+".asset");
                Assert.That(value,Is.Not.Null,id);Assert.That(ids.Add(value.id),Is.True,id);
                Assert.That(value.nameEn,Is.Not.Empty);Assert.That(value.nameZh,Is.Not.Empty);
                Assert.That(value.behavior,Is.Not.Null);Assert.That(value.score,Is.GreaterThan(0));
                sizes.TryGetValue(value.size,out int count);sizes[value.size]=count+1;
                var a=value.CreateBlueprint();var b=value.CreateBlueprint();a.parts.RemoveAt(0);
                Assert.That(b.parts.Count,Is.EqualTo(a.parts.Count+1),"Encounter copies must not mutate template data");
            }
            Assert.That(sizes[EnemySize.Small],Is.EqualTo(7));Assert.That(sizes[EnemySize.Large],Is.EqualTo(5));Assert.That(sizes[EnemySize.Capital],Is.EqualTo(1));
            CollectionAssert.AreEquivalent(ids,EnemyFleetLibrary.LoadTemplates().Select(enemy=>enemy.id));
        }

        [TestCase("needle",false)][TestCase("viper",false)][TestCase("wasp",false)][TestCase("bastion",false)]
        [TestCase("harrier",false)][TestCase("halberd",false)][TestCase("citadel",false)]
        [TestCase("scout-pulse",false)][TestCase("scout-rail",false)][TestCase("scout-beam",false)][TestCase("scout-mixed",false)]
        [TestCase("citadel-missile",false)][TestCase("sovereign",false)]
        public void NativeFleetHasSupportedWeaponsRuntimeNozzleMasksAndSixAxisFlight(string id,bool alternate=false)
        {
            var archetype=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(Root+id+".asset");
            Assert.That(archetype,Is.Not.Null);
            var data=archetype.CreateBlueprint(alternate);var report=DeploymentValidator.ValidateForRange(data,database);
            Assert.That(report.IsValid,Is.True,string.Join(",",report.Errors.Select(e=>e.key)));
            Assert.That(BlockConnectivity.IsConnected(data),Is.True,"All parts need real contact faces");
            Assert.That(data.parts.Count(p=>p.shape==BlockShape.Core),Is.EqualTo(1));
            // The approved t2 missile fit has 172 parts; retain it intact rather than applying the old generated-fleet cap of 160.
            Assert.That(data.parts.Count,Is.LessThanOrEqualTo(archetype.size==EnemySize.Capital?1150:archetype.size==EnemySize.Large?460:200));
            var cells=new Dictionary<Vector3Int,BlockRecord>();foreach(var p in data.parts)foreach(var cell in BlockModules.BodyCells(p))cells.Add(cell,p);
            var clearance=new WeaponClearance();var states=new List<ShipPartState>();float generation=0,storage=0;
            int workingEngines=0,blockedNozzles=0,closedEngines=0;
            foreach(var p in data.parts)
            {
                var definition=database.Get(p.shape);generation+=definition.generationWatts;storage+=definition.storageJoules;
                int mask=-1;
                if(BlockModules.IsThruster(p.shape)){
                    // Thruster6 permits flush neighbouring hull cells. Runtime disables each obstructed nozzle
                    // independently; the approved ship must fly using those actual surviving directions.
                    mask=0;var directions=BlockModules.LocalExhaustDirections(p.shape);
                    for(int nozzle=0;nozzle<directions.Length&&nozzle<31;nozzle++){
                        bool blocked=BlockModules.ExhaustCells(p,nozzle).Any(cell=>cells.ContainsKey(cell));
                        if(blocked)blockedNozzles++;else mask|=1<<nozzle;
                    }
                    if(mask!=0)workingEngines++;else closedEngines++;
                }
                states.Add(new ShipPartState{record=p,present=true,hp=definition.maxHp,maxHp=definition.maxHp,thrustAvailable=mask!=0,availableExhaustMask=mask});
                if(!BlockModules.IsWeapon(p.shape)&&!BlockModules.IsArmor(p.shape))continue;
                Assert.That(BlockConnectivity.HasCompleteMount(p,data.parts),Is.True,"Unsupported "+p.shape+" "+p.cell);
                if(!BlockModules.IsWeapon(p.shape))continue;
                foreach(var other in data.parts)if(other!=p)Assert.That(clearance.IsBlockedBy(p,definition,other,database.Get(other.shape).mesh),Is.False,"Blocked weapon "+p.shape+" "+p.cell+" by "+other.cell);
                if(definition.weapon)Assert.That(data.weaponAssignments.Any(a=>a.partId==p.id),Is.True,"Missing weapon group assignment");
            }
            Assert.That(generation,Is.GreaterThan(0));Assert.That(storage,Is.GreaterThan(0));
            TestContext.WriteLine(id+": working engines="+workingEngines+", fully obstructed engines="+closedEngines+", disabled nozzles="+blockedNozzles);
            Assert.That(workingEngines,Is.GreaterThan(0),"Every engine is obstructed.");
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            var performance=ShipPerformance.Calculate(states,data.parts.Single(p=>p.shape==BlockShape.Core).rotation,tuning);
            for(int axis=0;axis<3;axis++)
            {Assert.That(performance.accelerationPositive[axis],Is.GreaterThan(.001f),"No usable positive thrust on axis "+axis);Assert.That(performance.accelerationNegative[axis],Is.GreaterThan(.001f),"No usable negative thrust on axis "+axis);}
            Assert.That(performance.angularRate,Is.GreaterThan(0));
        }

        [Test] public void ApprovedFleetRetainsExactSourcesPartCountsAndArmament()
        {
            var ids=new[]{"needle","viper","wasp","bastion","harrier","halberd","citadel"};
            var counts=new[]{111,172,75,301,293,344,419};
            var shapes=new[]{
                new[]{BlockShape.Gun1,BlockShape.Gun2},new[]{BlockShape.Laser,BlockShape.Missile1,BlockShape.Missile2},
                new[]{BlockShape.Railgun2},new[]{BlockShape.Gun2,BlockShape.Gun3,BlockShape.Railgun2,BlockShape.Missile1},
                new[]{BlockShape.Laser,BlockShape.Laser3},new[]{BlockShape.Railgun3,BlockShape.Sword1,BlockShape.Sword2},new[]{BlockShape.Gun2}
            };
            for(int i=0;i<ids.Length;i++){
                var enemy=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(Root+ids[i]+".asset");
                Assert.That(enemy,Is.Not.Null);var data=enemy.CreateBlueprint();
                Assert.That(data.name,Is.EqualTo("t"+(i+1)));Assert.That(data.parts.Count,Is.EqualTo(counts[i]),ids[i]);
                CollectionAssert.AreEquivalent(shapes[i],data.parts.Where(p=>database.Get(p.shape).weapon).Select(p=>p.shape).Distinct().ToArray(),ids[i]);
                string snapshot=Path.Combine(Application.dataPath,"..","Documentation","FinalBalanceRound1-2026-11-03","approved-blueprints","t"+(i+1)+".json");
                Assert.That(File.Exists(snapshot),Is.True,"Install the approved final fleet before running native asset acceptance.");
                Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(enemy.blueprint)).SequenceEqual(File.ReadAllBytes(snapshot)),Is.True,"Import must preserve the complete approved source, including weapon groups: "+ids[i]);
            }
        }

        [Test] public void FinalWaveRosterUsesTheApprovedCompositionAndNativeBudget()
        {
            var config=AssetDatabase.LoadAssetAtPath<ArcadeMissionDefinition>("Assets/_Starframe/Settings/Game/SaturnRingWaves.asset");
            Assert.That(config,Is.Not.Null);Assert.That(config.waves.Length,Is.EqualTo(6));Assert.That(config.TotalEnemies,Is.EqualTo(77));
            string[] ids={"scout-pulse","scout-rail","scout-beam","scout-mixed","needle","viper","wasp","bastion","harrier","halberd","citadel","citadel-missile","sovereign"};
            int[][] counts={new[]{3,2,2,0,2,0,1,0,0,0,0,0,0},new[]{2,2,0,2,2,1,2,1,0,0,0,0,0},new[]{0,0,3,3,2,2,1,1,1,0,0,0,0},new[]{2,2,2,0,2,1,0,1,1,2,0,0,0},new[]{2,2,0,2,2,1,1,1,0,1,1,1,0},new[]{0,0,4,3,2,1,1,0,1,1,0,1,1}};
            for(int wave=0;wave<6;wave++){
                var batches=config.waves[wave].batches;Assert.That(config.waves[wave].spawnInterval,Is.GreaterThan(0));
                Assert.That(batches.All(batch=>batch!=null&&batch.enemy&&batch.count>0&&batch.delaySeconds>=0),Is.True);
                for(int i=0;i<ids.Length;i++)Assert.That(batches.Where(batch=>batch.enemy.id==ids[i]).Sum(batch=>batch.count),Is.EqualTo(counts[wave][i]),"Wave "+(wave+1)+" "+ids[i]);
            }
            Assert.That(config.waves.Take(5).SelectMany(w=>w.batches).Any(batch=>batch.enemy.size==EnemySize.Capital),Is.False,"The flagship belongs only to the final wave.");
            Assert.That(config.waves[5].batches[0].enemy.id,Is.EqualTo("sovereign"),"The final boss arrives while its escorts are being deployed.");
            Assert.That(config.waves[5].batches.Where(batch=>batch.enemy.size!=EnemySize.Capital).Sum(batch=>batch.count),Is.GreaterThan(0));
            var progress=new ArcadeWaveProgress();progress.Begin(config.TotalEnemies);
            for(int life=1;life<=config.TotalEnemies;life++)Assert.That(progress.Spawn(life),Is.True);
            for(int life=2;life<=config.TotalEnemies;life++)Assert.That(progress.Defeat(life),Is.True);
            Assert.That(progress.IsCleared,Is.False,"Surviving flagship/escort prevents victory even after all other ships die.");
            Assert.That(progress.Defeat(1),Is.True);Assert.That(progress.IsCleared,Is.True);
            Assert.That(config.maximumAlive,Is.EqualTo(10));Assert.That(config.maximumLarge,Is.EqualTo(3));Assert.That(config.maximumAttackers,Is.EqualTo(3));
            Assert.That(config.emptyFieldReinforcementSeconds,Is.EqualTo(3));
            var mission=AssetDatabase.LoadAssetAtPath<MissionDefinition>("Assets/_Starframe/Settings/Game/ice.asset");
            Assert.That(config.deployment,Is.SameAs(mission));Assert.That(mission.BudgetFor(MissionDifficulty.Standard),Is.EqualTo(30000));
            var catalog=AssetDatabase.LoadAssetAtPath<MissionCatalog>("Assets/_Starframe/Settings/Game/Missions.asset");
            Assert.That(catalog.Find("ice"),Is.SameAs(mission),"The actual deployment path must use the same native mission budget.");
        }

        [Test] public void NewFleetSourcesPreserveEveryPartAndWeaponGroupByteForByte()
        {
            string[] sources={"t0-1","t0-2","t0-3","t0-4","t7-2","t8"};
            string[] ids={"scout-pulse","scout-rail","scout-beam","scout-mixed","citadel-missile","sovereign"};
            int[] counts={52,44,44,45,415,852};
            for(int i=0;i<sources.Length;i++){
                var enemy=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(Root+ids[i]+".asset");Assert.That(enemy,Is.Not.Null);
                var data=enemy.CreateBlueprint();Assert.That(data.name,Is.EqualTo(sources[i]));Assert.That(data.parts.Count,Is.EqualTo(counts[i]));
                string snapshot=Path.Combine(Application.dataPath,"..","Documentation","FleetRevision6","approved-blueprints",sources[i]+".json");
                CollectionAssert.AreEqual(File.ReadAllBytes(snapshot),File.ReadAllBytes(AssetDatabase.GetAssetPath(enemy.blueprint)),sources[i]);
            }
        }

        [Test] public void FinalBehaviorProfilesFollowTheApprovedBatteryTypes()
        {
            var ids=new[]{"needle","viper","wasp","bastion","harrier","halberd","citadel"};
            var roles=new[]{BotCombatRole.Interceptor,BotCombatRole.Missile,BotCombatRole.Cruiser,BotCombatRole.Gunship,BotCombatRole.Cruiser,BotCombatRole.Cruiser,BotCombatRole.Capital};
            for(int i=0;i<ids.Length;i++){
                var enemy=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(Root+ids[i]+".asset");
                Assert.That(enemy.behavior.role,Is.EqualTo(roles[i]),ids[i]);Assert.That(enemy.behavior.preferredRange,Is.GreaterThan(0));
                if(i!=0)Assert.That(enemy.behavior.attackPass,Is.False,ids[i]);
            }
        }
    }
}
