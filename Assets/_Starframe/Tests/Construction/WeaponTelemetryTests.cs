using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponTelemetryTests
    {
        readonly List<Object> owned=new List<Object>();
        ProjectileWorld world;
        FlightShipRuntime runtime;
        ShipWeapons weapons;
        ModuleDefinition gunModule;
        string firstGun,secondGun;
        int secondGroup;

        [TearDown] public void Cleanup()
        {
            world?.Clear();BlockModules.Configure(null);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test] public void TelemetryKeepsDisabledMountsAndDoesNotShareHeatBetweenMounts()
        {
            CreateFixture();
            Assert.That(weapons.GatlingHeat.Count,Is.EqualTo(2));
            FireGroup(0,6);
            weapons.SetGroupEnabled(0,false);
            Assert.That(weapons.GatlingHeat.Count,Is.EqualTo(2),"Changing selection must not hide a hot mount.");
            Assert.That(weapons.GatlingHeat.HeatFraction,Is.EqualTo(.5f).Within(.00001f));
            Assert.That(weapons.GatlingHeat.AnyOverheated,Is.False);
            runtime.SetPartPresent(firstGun,false);
            Assert.That(weapons.GatlingHeat.Count,Is.EqualTo(1));
            Assert.That(weapons.GatlingHeat.HeatFraction,Is.Zero,"The other mount never fired and must remain cold.");
            runtime.SetPartPresent(secondGun,false);
            Assert.That(weapons.GatlingHeat.Count,Is.Zero);
            Assert.That(weapons.GatlingHeat.AnyOverheated,Is.False);
            Assert.That(weapons.GatlingHeat.DisplayHeatFraction,Is.Zero);
        }

        [Test] public void OverheatTelemetryRemainsVisibleUntilTheDisabledMountActuallyCoolsToZero()
        {
            CreateFixture();FireGroup(0,12);
            Assert.That(weapons.GatlingHeat.AnyOverheated,Is.True);
            Assert.That(weapons.IsSustaining(WeaponFamily.Gatling),Is.False,"Overheated mounts cannot keep a firing loop alive.");
            Assert.That(weapons.GatlingHeat.DisplayHeatFraction,Is.EqualTo(1).Within(.00001f));
            weapons.SetGroupEnabled(0,false);weapons.ClearGroupInputs();
            Advance(3);
            Assert.That(weapons.GatlingHeat.Count,Is.EqualTo(2));
            Assert.That(weapons.GatlingHeat.AnyOverheated,Is.True);
            Assert.That(weapons.GatlingHeat.DisplayHeatFraction,Is.EqualTo(.5f).Within(.00001f));
            Advance(3);
            Assert.That(weapons.GatlingHeat.AnyOverheated,Is.False);
            Assert.That(weapons.GatlingHeat.OverheatedHeatFraction,Is.Zero);
            Assert.That(weapons.GatlingHeat.DisplayHeatFraction,Is.Zero);
        }

        [Test] public void DisplayPrioritizesAnOverheatedMountEvenWhileAnotherMountHasHigherHeat()
        {
            CreateFixture();FireGroup(0,12);weapons.SetGroupEnabled(0,false);
            FireGroup(secondGroup,5);
            var heat=weapons.GatlingHeat;
            Assert.That(heat.AnyOverheated,Is.True);
            Assert.That(heat.HeatFraction,Is.EqualTo(5f/12f).Within(.00001f));
            Assert.That(heat.OverheatedHeatFraction,Is.EqualTo(1f/6f).Within(.00001f));
            Assert.That(heat.DisplayHeatFraction,Is.EqualTo(heat.OverheatedHeatFraction));
        }

        [Test] public void BlueprintCostIsPositiveAndRetainsFullSortieValueAfterDamageDetachmentAndRecovery()
        {
            CreateFixture();
            Assert.That(runtime.BlueprintCost,Is.EqualTo(150));Assert.That(runtime.BlueprintCost,Is.GreaterThan(0));
            Assert.That(runtime.ApplyDamage(firstGun,25),Is.GreaterThan(0));
            Assert.That(runtime.BlueprintCost,Is.EqualTo(150));
            runtime.SetPartPresent(firstGun,false);
            Assert.That(runtime.Parts.Count(p=>p.present),Is.EqualTo(4));
            Assert.That(runtime.BlueprintCost,Is.EqualTo(150));
            gunModule.cost=999;runtime.Recalculate();
            Assert.That(runtime.BlueprintCost,Is.EqualTo(150),"Sortie cost is captured once, rather than following later price or surviving-part changes.");
            runtime.Respawn();Assert.That(runtime.BlueprintCost,Is.EqualTo(150));
        }

        void CreateFixture()
        {
            var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var core=Own(ScriptableObject.CreateInstance<ModuleDefinition>());
            core.shape=BlockShape.Core;core.category=ModuleCategory.Core;core.massKg=10;core.maxHp=100;core.cost=100;core.occupiedCells=new[]{Vector3Int.zero};
            var cube=Own(ScriptableObject.CreateInstance<ModuleDefinition>());
            cube.shape=BlockShape.Cube;cube.category=ModuleCategory.Hull;cube.massKg=10;cube.maxHp=100;cube.cost=0;cube.occupiedCells=new[]{Vector3Int.zero};
            gunModule=Own(ScriptableObject.CreateInstance<ModuleDefinition>());
            gunModule.shape=BlockShape.Laser;gunModule.category=ModuleCategory.Weapon;gunModule.massKg=10;gunModule.maxHp=100;gunModule.cost=25;
            gunModule.occupiedCells=new[]{Vector3Int.zero};gunModule.muzzleLocalPosition=Vector3.forward*.6f;
            var definition=Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            definition.family=WeaponFamily.Gatling;definition.shotsPerSecond=20;gunModule.weapon=definition;
            db.modules=new List<ModuleDefinition>{core,cube,gunModule};
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
            var go=Own(new GameObject("Weapon telemetry fixture"));var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(db);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(1,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(-1,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            Assert.That(assembly.Place(BlockShape.Laser,new Vector3(1,1.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            Assert.That(assembly.Place(BlockShape.Laser,new Vector3(-1,1.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
            var blueprint=assembly.Export("Weapon telemetry fixture");
            var records=blueprint.parts.Where(p=>p.shape==BlockShape.Laser).ToArray();firstGun=records[0].id;secondGun=records[1].id;
            secondGroup=WeaponGroups.AddGroup(blueprint,"Second mount",null,db).id;
            Assert.That(WeaponGroups.Assign(blueprint,records[1].id,secondGroup,db),Is.True);
            runtime=go.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);Physics.SyncTransforms();
            world=new ProjectileWorld();world.Initialize(null);
            weapons=new ShipWeapons();weapons.Initialize(runtime,world,blueprint);weapons.UseGroupedInput=true;
        }
        void FireGroup(int group,float dt){weapons.SetGroupInput(group,true,true);Advance(dt);}
        void Advance(float dt){weapons.Prepare();weapons.Execute(dt,1);}
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
