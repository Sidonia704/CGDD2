using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponImmediateCadenceTests
    {
        readonly List<Object> owned=new List<Object>();
        ProjectileWorld world;
        WeaponDefinition definition;
        [SetUp]public void SetUp()
        {
            definition=Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            definition.activationMode=WeaponActivationMode.Hold;
            definition.firstShotImmediate=true;definition.shotsPerSecond=5;
        }
        [TearDown]public void TearDown()
        {
            world?.Clear();BlockModules.Configure(null);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(1f)] [TestCase(2.5f)] [TestCase(5f)]
        public void ContinuousImmediateHoldSustainsAuthoredRate(float rate)
        {
            definition.shotsPerSecond=rate;var state=new WeaponActivationState();
            state.Prepare(definition,new WeaponInputState(true,true),true);
            Assert.That(state.Advance(definition,.01f,1),Is.EqualTo(1));
            int followups=0;
            for(int i=0;i<1000;i++){
                state.Prepare(definition,new WeaponInputState(true),true);
                followups+=state.Advance(definition,.01f,1);
            }
            Assert.That(followups,Is.EqualTo(Mathf.RoundToInt(rate*10)),"The immediate first shot must not halve steady firing rate.");
        }

        [Test]public void ImmediateFirstShotWaitsForPowerWithoutBeingConsumed()
        {
            var state=new WeaponActivationState();state.Prepare(definition,new WeaponInputState(true,true),true);
            Assert.That(state.Advance(definition,1,0),Is.Zero);
            Assert.That(state.Advance(definition,.01f,float.NaN),Is.Zero);
            state.Prepare(definition,new WeaponInputState(true),true);
            Assert.That(state.Advance(definition,.01f,1),Is.EqualTo(1));
            Assert.That(state.Advance(definition,.01f,1),Is.Zero);
        }

        [Test]public void ImmediateSustainedCadenceScalesByAllocatedPower()
        {
            var state=new WeaponActivationState();state.Prepare(definition,new WeaponInputState(true,true),true);
            Assert.That(state.Advance(definition,.01f,.5f),Is.EqualTo(1));
            int followups=0;
            for(int i=0;i<400;i++)followups+=state.Advance(definition,.01f,.5f);
            Assert.That(followups,Is.EqualTo(10));
        }

        [Test]public void ReleasingAndCancellingPreserveTheRemainingCycle()
        {
            definition.shotsPerSecond=2;var state=new WeaponActivationState();
            state.Prepare(definition,new WeaponInputState(true,true),true);Assert.That(state.Advance(definition,.01f,1),Is.EqualTo(1));
            state.Prepare(definition,new WeaponInputState(false,false,true),true);Assert.That(state.Advance(definition,.1f,1),Is.Zero);
            state.Prepare(definition,new WeaponInputState(true,true),true);Assert.That(state.Advance(definition,.1f,1),Is.Zero);
            state.Cancel();state.AdvanceInactive(.2f);
            state.Prepare(definition,new WeaponInputState(true,true),true);Assert.That(state.Advance(definition,.09f,1),Is.Zero);
            Assert.That(state.Advance(definition,.01f,1),Is.EqualTo(1));
        }

        [Test]public void IdleTimeDoesNotBankAdditionalImmediateShots()
        {
            var state=new WeaponActivationState();state.Prepare(definition,new WeaponInputState(true,true),true);state.Advance(definition,.01f,1);
            state.Cancel();state.AdvanceInactive(100);
            state.Prepare(definition,new WeaponInputState(true,true),true);
            Assert.That(state.Advance(definition,.01f,1),Is.EqualTo(1));
        }

        [Test]public void RuntimeReleasedTriggerLetsImmediateWeaponCool()
        {
            var weapons=CreateWeapons();Fire(weapons,.01f);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            weapons.SetGroupInput(0,false,false,true);Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(.2f,0);
            Fire(weapons,.01f);Assert.That(weapons.ShotsFired,Is.EqualTo(2));
        }

        [Test]public void RuntimeGroupSwitchAndInputClearCannotGrantAnExtraImmediateShot()
        {
            var weapons=CreateWeapons();Fire(weapons,.01f);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            weapons.SetGroupEnabled(0,false);weapons.SetGroupEnabled(0,true);
            Fire(weapons,.01f);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            weapons.ClearGroupInputs();Fire(weapons,.01f);Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            weapons.SetGroupEnabled(0,false);Assert.That(weapons.Prepare(),Is.Zero);weapons.Execute(.2f,0);
            Assert.That(weapons.ShotsFired,Is.EqualTo(1));
            weapons.SetGroupEnabled(0,true);Fire(weapons,.01f);Assert.That(weapons.ShotsFired,Is.EqualTo(2));
        }

        static void Fire(ShipWeapons weapons,float dt)
        {
            weapons.SetGroupInput(0,true,true);weapons.Prepare();weapons.Execute(dt,1);
        }
        ShipWeapons CreateWeapons()
        {
            var database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            database.modules=new List<ModuleDefinition>();
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Cube,BlockShape.Laser}){
                var module=Own(ScriptableObject.CreateInstance<ModuleDefinition>());module.shape=shape;module.massKg=10;module.maxHp=100;
                module.occupiedCells=new[]{Vector3Int.zero};
                module.category=shape==BlockShape.Laser?ModuleCategory.Weapon:shape==BlockShape.Core?ModuleCategory.Core:ModuleCategory.Hull;
                if(shape==BlockShape.Laser){module.weapon=definition;module.muzzleLocalPosition=Vector3.forward*.6f;}
                database.modules.Add(module);
            }
            var go=Own(new GameObject("Immediate cadence fixture"));var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(1,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Laser,new Vector3(1,1.5f,0),Quaternion.identity),Is.True);
            var blueprint=assembly.Export("Immediate cadence fixture");
            var runtime=go.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);Physics.SyncTransforms();
            world=new ProjectileWorld();world.Initialize(null);
            var weapons=new ShipWeapons();weapons.Initialize(runtime,world);weapons.UseGroupedInput=true;return weapons;
        }
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
