using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponGroupRuntimeTests
    {
        readonly List<Object> owned=new List<Object>();ProjectileWorld world;
        [TearDown]public void TearDown()
        {
            world?.Clear();BlockModules.Configure(null);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();
        }

        [Test]public void IdenticalWeaponPartsCanFireIndependentlyOrTogether()
        {
            var weapons=Create(out _,out _,out _,out _);weapons.UseGroupedInput=true;
            weapons.SetGroupInput(0,true,true);Assert.That(weapons.Prepare(),Is.EqualTo(50));weapons.Execute(.2f,1);
            Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            weapons.SetGroupInput(0,false,false,true);weapons.SetGroupInput(1,true,true);
            Assert.That(weapons.Prepare(),Is.EqualTo(50));weapons.Execute(.2f,1);Assert.That(weapons.ShotsFired,Is.EqualTo(2));
            weapons.SetGroupInput(0,true,true);Assert.That(weapons.Prepare(),Is.EqualTo(100));weapons.Execute(.2f,1);
            Assert.That(weapons.ShotsFired,Is.EqualTo(4));Assert.That(world.TotalSpawned,Is.EqualTo(4));
        }

        [Test]public void LegacyAiInputStillFiresEveryWeaponRegardlessOfAssignments()
        {
            var weapons=Create(out _,out _,out _,out _);Assert.That(weapons.UseGroupedInput,Is.False);
            weapons.FireRequested=true;Assert.That(weapons.Prepare(),Is.EqualTo(100));weapons.Execute(.2f,1);
            Assert.That(weapons.ShotsFired,Is.EqualTo(2));weapons.FireRequested=false;Assert.That(weapons.Prepare(),Is.Zero);
        }

        [Test]public void RuntimeUsesSnapshotAndDestroyedGroupWeaponKeepsItsAssignmentForRecovery()
        {
            var weapons=Create(out var runtime,out var blueprint,out var rightId,out _);weapons.UseGroupedInput=true;
            WeaponGroups.Assign(blueprint,rightId,0);WeaponGroups.FindGroup(blueprint,1).binding.source=WeaponInputSource.MouseMiddle;
            Assert.That(weapons.GetGroupId(rightId),Is.EqualTo(1));Assert.That(weapons.Groups[1].binding.source,Is.EqualTo(WeaponInputSource.MouseRight));
            runtime.SetPartPresent(rightId,false);weapons.SetGroupInput(1,true,true);
            Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(1,1);Assert.That(weapons.ShotsFired,Is.Zero);
            Assert.That(weapons.GetGroupGunCount(1,true),Is.Zero);Assert.That(weapons.GetGroupId(rightId),Is.EqualTo(1));
            runtime.SetPartPresent(rightId,true);Assert.That(weapons.Prepare(),Is.EqualTo(50));weapons.Execute(.2f,1);
            Assert.That(weapons.ShotsFired,Is.EqualTo(1));
        }

        [Test]public void LockRequiredWeaponDrawsNothingUntilLockAndCannotFireAfterLockLost()
        {
            var weapons=Create(out _,out _,out _,out var definition);definition.requireTargetLock=true;
            weapons.UseGroupedInput=true;weapons.SetGroupInput(1,true,true);
            Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(1,1);Assert.That(weapons.ShotsFired,Is.Zero);
            weapons.HasValidTargetLock=true;Assert.That(weapons.Prepare(),Is.EqualTo(50));
            weapons.HasValidTargetLock=false;weapons.Execute(.2f,1);Assert.That(weapons.ShotsFired,Is.Zero);
            weapons.HasValidTargetLock=true;weapons.Prepare();weapons.Execute(.2f,1);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
        }

        [Test]public void PressEdgesSurviveMultipleRenderUpdatesAndAreConsumedOnce()
        {
            var weapons=Create(out _,out _,out _,out var definition);definition.activationMode=WeaponActivationMode.Press;
            weapons.UseGroupedInput=true;weapons.SetGroupInput(1,true,true);weapons.SetGroupInput(1,false,false,true);
            Assert.That(weapons.Prepare(),Is.EqualTo(50));weapons.Execute(.2f,1);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(1,1);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
        }

        [Test]public void ClearingInputsStopsLatchedToggleAndRouterBlockedStateStopsLegacyRequest()
        {
            var weapons=Create(out _,out var blueprint,out _,out var definition);definition.activationMode=WeaponActivationMode.Toggle;
            weapons.UseGroupedInput=true;weapons.SetGroupInput(1,true,true);weapons.Prepare();weapons.Execute(.2f,1);
            weapons.SetGroupInput(1,false,false,true);weapons.Prepare();weapons.Execute(.2f,1);Assert.That(weapons.ShotsFired,Is.EqualTo(2));
            weapons.FireRequested=true;WeaponGroupInputRouter.Apply(weapons,blueprint,false,false);
            Assert.That(weapons.FireRequested,Is.False);Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(1,1);
            Assert.That(weapons.ShotsFired,Is.EqualTo(2));
        }

        [Test]public void AimProfilesFilterToHeldGroupAndRecoverAllWhenReleased()
        {
            var weapons=Create(out _,out _,out var rightId,out _);weapons.UseGroupedInput=true;
            var profiles=new List<WeaponAimProfile>();weapons.Prepare();weapons.GetAimProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(1));Assert.That(profiles[0].gunCount,Is.EqualTo(2));
            weapons.SetGroupInput(1,true,true);weapons.GetAimProfiles(profiles);
            Assert.That(profiles.Count,Is.EqualTo(1));Assert.That(profiles[0].gunCount,Is.EqualTo(1));Assert.That(profiles[0].partId,Is.EqualTo(rightId));
            weapons.GetAimProfiles(profiles,filterToHeld:false);
            Assert.That(profiles[0].gunCount,Is.EqualTo(2),"HUD profiles must remain visible when a different fire group is held.");
            weapons.SetGroupInput(1,false,false,true);weapons.GetAimProfiles(profiles);Assert.That(profiles[0].gunCount,Is.EqualTo(2));
            weapons.GetAimProfiles(profiles,1);Assert.That(profiles[0].partId,Is.EqualTo(rightId));
        }

        [Test]public void GroupsStartEnabledAndDisabledGroupsDoNotFireDrawPowerOrProduceLeadPoints()
        {
            var weapons=Create(out var runtime,out var blueprint,out _,out _);weapons.UseGroupedInput=true;
            Assert.That(weapons.IsGroupEnabled(0)&&weapons.IsGroupEnabled(1),Is.True);
            weapons.SetGroupEnabled(1,false);weapons.SetGroupInput(0,true,true);weapons.SetGroupInput(1,true,true);
            Assert.That(weapons.Prepare(),Is.EqualTo(50));weapons.Execute(.2f,1);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            var profiles=new List<WeaponAimProfile>();weapons.GetAimProfiles(profiles);Assert.That(profiles[0].gunCount,Is.EqualTo(1));
            weapons.GetAimProfiles(profiles,1);Assert.That(profiles,Is.Empty);
            weapons.SetGroupEnabled(0,false);Assert.That(weapons.Prepare(),Is.Zero);weapons.GetAimProfiles(profiles);Assert.That(profiles,Is.Empty);
            weapons.ClearGroupInputs();Assert.That(weapons.IsGroupEnabled(0),Is.False,"Opening a menu must preserve selection");
            Assert.That(weapons.ToggleGroup(1),Is.True);Assert.That(weapons.IsGroupEnabled(0),Is.False);
            weapons.Initialize(runtime,world,blueprint);Assert.That(weapons.IsGroupEnabled(0)&&weapons.IsGroupEnabled(1),Is.True,"A new sortie starts with all groups enabled");
            Assert.That(blueprint.weaponGroups.Count,Is.EqualTo(2));Assert.That(weapons.ToggleGroup(999),Is.False);
        }

        [TestCase(WeaponActivationMode.Toggle)]
        [TestCase(WeaponActivationMode.Press)]
        [TestCase(WeaponActivationMode.ChargeRelease)]
        public void DisablingCancelsLatchedOrChargingWeapon(WeaponActivationMode mode)
        {
            var weapons=Create(out _,out _,out var partId,out var definition);definition.activationMode=mode;definition.chargeSeconds=1;
            weapons.UseGroupedInput=true;weapons.SetGroupInput(1,true,true);weapons.Prepare();weapons.Execute(.1f,1);
            weapons.SetGroupEnabled(1,false);Assert.That(weapons.GetChargeProgress(partId),Is.Zero);
            weapons.SetGroupInput(1,false,false,true);Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(2,1);
            weapons.SetGroupEnabled(1,true);Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(2,1);
            Assert.That(weapons.ShotsFired,Is.Zero,"Switching back on must not release a stored shot or toggle");
        }

        [Test]public void DisablingBetweenPowerPreparationAndExecutionStopsTheShot()
        {
            var weapons=Create(out _,out _,out _,out _);weapons.UseGroupedInput=true;weapons.SetGroupInput(1,true,true);
            Assert.That(weapons.Prepare(),Is.EqualTo(50));weapons.SetGroupEnabled(1,false);weapons.Execute(.2f,1);
            Assert.That(weapons.ShotsFired,Is.Zero);
        }

        [Test]public void AlternatingAutomaticGunsSpreadPulsesWithoutChangingSustainedRateAndCanResynchronize()
        {
            var weapons=Create(out _,out _,out _,out var definition);definition.firstShotImmediate=true;
            weapons.AlternatingDirectFire=true;weapons.FireRequested=true;
            int dualTicks=0;for(int i=0;i<200;i++){int before=weapons.ShotsFired;weapons.Prepare();weapons.Execute(.01f,1);if(weapons.ShotsFired-before>1)dualTicks++;}
            Assert.That(dualTicks,Is.Zero);Assert.That(weapons.ShotsFired,Is.InRange(17,21));
            weapons.AlternatingDirectFire=false;bool salvo=false;
            for(int i=0;i<50;i++){int before=weapons.ShotsFired;weapons.Prepare();weapons.Execute(.01f,1);salvo|=weapons.ShotsFired-before==2;}
            Assert.That(salvo,Is.True,"Returning to salvo mode must align existing barrel clocks.");
        }
        [TestCase(WeaponActivationMode.Press)]
        [TestCase(WeaponActivationMode.ChargeRelease)]
        public void AlternatingEdgeWeaponsKeepTheirInputAndChargeButStaggerDischarges(WeaponActivationMode mode)
        {
            var weapons=Create(out _,out _,out _,out var definition);definition.activationMode=mode;definition.chargeSeconds=.1f;
            weapons.AlternatingDirectFire=true;weapons.FireRequested=true;
            if(mode==WeaponActivationMode.ChargeRelease){for(int i=0;i<15;i++){weapons.Prepare();weapons.Execute(.01f,1);}Assert.That(weapons.ShotsFired,Is.Zero);weapons.FireRequested=false;}
            var ticks=new List<int>();int previous=weapons.ShotsFired;
            for(int i=0;i<60;i++){weapons.Prepare();weapons.Execute(.01f,1);int count=weapons.ShotsFired-previous;Assert.That(count,Is.LessThanOrEqualTo(1));if(count>0)ticks.Add(i);previous=weapons.ShotsFired;}
            Assert.That(ticks.Count,Is.EqualTo(2));Assert.That(ticks[1]-ticks[0],Is.InRange(9,11));
        }
        [Test]public void MissileRacksStaggerEveryPhysicalTubeAcrossShipAndStopOnLockLoss()
        {
            var weapons=Create(out var owner,out _,out _,out var definition);
            definition.delivery=WeaponDelivery.GuidedMissile;definition.missileLockSeconds=0;definition.reloadSeconds=10;
            definition.missileLaunchOffsets=new[]{Vector3.left*.1f,Vector3.right*.1f};definition.range=2000;
            var host=Own(new GameObject("Stagger target"));host.transform.position=Vector3.forward*1000;
            var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(owner.Tuning.database);
            assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity);
            var target=host.AddComponent<FlightShipRuntime>();target.Initialize(assembly,assembly.Export("target"),owner.Tuning);target.Team=1;
            Physics.SyncTransforms();weapons.Target=target;weapons.HasValidTargetLock=true;weapons.FireRequested=true;
            var launchTicks=new List<int>();int previous=0;
            for(int i=0;i<100;i++){weapons.Prepare();weapons.Execute(.01f,1);int count=world.TotalSpawned-previous;Assert.That(count,Is.InRange(0,1));if(count>0)launchTicks.Add(i);previous=world.TotalSpawned;}
            Assert.That(launchTicks.Count,Is.EqualTo(4));
            for(int i=1;i<launchTicks.Count;i++)Assert.That(launchTicks[i]-launchTicks[i-1],Is.InRange(12,13));
            weapons.Reset();weapons.Target=target;weapons.HasValidTargetLock=true;weapons.FireRequested=true;
            weapons.Prepare();weapons.Execute(.01f,1);int beforeLoss=world.TotalSpawned;
            weapons.HasValidTargetLock=false;
            for(int i=0;i<100;i++){weapons.Prepare();weapons.Execute(.01f,1);}
            Assert.That(world.TotalSpawned,Is.EqualTo(beforeLoss));
        }

        ShipWeapons Create(out FlightShipRuntime runtime,out BlueprintData blueprint,out string rightId,out WeaponDefinition weapon)
        {
            var database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());weapon.shotsPerSecond=5;weapon.sustainedPowerWatts=50;
            database.modules=new List<ModuleDefinition>();
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Cube,BlockShape.Laser}){
                var definition=Own(ScriptableObject.CreateInstance<ModuleDefinition>());definition.shape=shape;definition.massKg=10;definition.maxHp=100;
                definition.occupiedCells=new[]{Vector3Int.zero};definition.category=shape==BlockShape.Laser?ModuleCategory.Weapon:shape==BlockShape.Core?ModuleCategory.Core:ModuleCategory.Hull;
                if(shape==BlockShape.Laser){definition.weapon=weapon;definition.muzzleLocalPosition=Vector3.forward*.6f;}
                database.modules.Add(definition);
            }
            var go=Own(new GameObject("Weapon group fixture"));var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True);
            // Separate hull pedestals retain two independent barrels under the bottom-mount rule.
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(-1,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(1,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Laser,new Vector3(-1,1.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Laser,new Vector3(1,1.5f,0),Quaternion.identity),Is.True);
            blueprint=assembly.Export("group fixture");rightId=blueprint.parts.Find(p=>p.shape==BlockShape.Laser&&p.position.x>0).id;
            // New blueprints already contain the two default mouse groups.
            Assert.That(WeaponGroups.ConfigureGroup(blueprint,1,"Secondary",new WeaponInputBinding{source=WeaponInputSource.MouseRight},database),Is.True);
            var group=WeaponGroups.FindGroup(blueprint,1);
            Assert.That(WeaponGroups.Assign(blueprint,rightId,group.id,database),Is.True);
            runtime=go.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);Physics.SyncTransforms();
            world=new ProjectileWorld();world.Initialize(null);var weapons=new ShipWeapons();weapons.Initialize(runtime,world);return weapons;
        }
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
