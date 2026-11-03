using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class PowerBoostTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void TearDown()
        {
            BlockModules.Configure(null);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]public void GeneratorBalancesLaserAndProportionallySharesBoostAfterBatteryEmpties()
        {
            var bus=new ShipPowerBus();bus.Configure(500,60,true);
            bus.Allocate(1,50);Assert.That(bus.Energy,Is.EqualTo(500).Within(.001f));
            bus.Allocate(1,100);Assert.That(bus.Energy,Is.EqualTo(460).Within(.001f));
            bus.Allocate(1,150);Assert.That(bus.Energy,Is.EqualTo(370).Within(.001f));
            bus.SetEnergy(0);
            Assert.That(bus.Allocate(1,150),Is.EqualTo(.4f).Within(.001f));
            Assert.That(bus.Energy,Is.Zero);
            bus.Allocate(1,0);Assert.That(bus.Energy,Is.EqualTo(60).Within(.001f));
            bus.Configure(0,0);Assert.That(bus.Allocate(1,150),Is.Zero);
        }

        [TestCase(2f)][TestCase(5f)][TestCase(20f)]
        public void LaserDrawsFiftyWattsIndependentOfItsConfiguredFireRate(float shotsPerSecond)
        {
            CreateFlight(out var runtime,out var weapon);weapon.shotsPerSecond=shotsPerSecond;
            var world=new ProjectileWorld();world.Initialize(null);
            var weapons=new ShipWeapons();weapons.Initialize(runtime,world);weapons.FireRequested=true;
            Physics.SyncTransforms();
            Assert.That(weapons.Prepare(),Is.EqualTo(50).Within(.001f));
            weapons.Execute(1,.4f);
            Assert.That(weapons.ShotsFired,Is.EqualTo(Mathf.FloorToInt(shotsPerSecond*.4f+.000001f)));
            weapons.FireRequested=false;Assert.That(weapons.Prepare(),Is.Zero);world.Clear();
        }

        [Test]public void ZeroEnergyKeepsBaseFlightAndBrakingWhileBoostScalesOnlyWithItsAllocation()
        {
            var flight=CreateFlight(out var runtime,out _);
            runtime.Power.Configure(0,0);
            flight.SetExternalInput(Vector3.forward,Vector2.zero,boost:true);flight.SetBoostPowerRatio(0);
            Tick(flight);float baseline=flight.CurrentSpeed;
            Assert.That(baseline,Is.GreaterThan(0));Assert.That(flight.IsBoosting,Is.False);
            Assert.That(flight.BoostScale,Is.EqualTo(1));
            flight.SetExternalInput(Vector3.zero,Vector2.zero);Tick(flight);
            Assert.That(flight.CurrentSpeed,Is.LessThan(baseline),"Ordinary braking must remain available with no electricity.");
            flight.ResetMotion(Vector3.zero,Quaternion.identity);
            flight.SetExternalInput(Vector3.forward,Vector2.zero,boost:true);flight.SetBoostPowerRatio(1);Tick(flight);
            Assert.That(flight.CurrentSpeed,Is.EqualTo(baseline*1.5f).Within(.001f));
            Assert.That(flight.IsBoosting,Is.True);
            flight.ResetMotion(Vector3.zero,Quaternion.identity);
            flight.SetExternalInput(Vector3.forward,Vector2.zero,boost:true);flight.SetBoostPowerRatio(.4f);Tick(flight);
            Assert.That(flight.CurrentSpeed,Is.EqualTo(baseline*1.2f).Within(.001f));
            Assert.That(flight.Energy,Is.Zero);Assert.That(flight.MaxEnergy,Is.Zero);
            flight.Tuning.maxSpeed=baseline*.5f;runtime.Recalculate();
            flight.ResetMotion(Vector3.zero,Quaternion.identity);
            flight.SetExternalInput(Vector3.forward,Vector2.zero,boost:true);flight.SetBoostPowerRatio(1);Tick(flight);
            Assert.That(flight.CurrentSpeed,Is.EqualTo(flight.Tuning.maxSpeed).Within(.001f),"Boost must respect the ordinary speed limit.");
        }

        [Test]public void AShipWithoutReverseNozzlesKeepsItsMomentumWhenThePilotReleases()
        {
            var flight=CreateFlight(out _,out _,false,false,false);flight.SetExternalInput(Vector3.forward,Vector2.zero);Tick(flight);
            float speed=flight.CurrentSpeed;Assert.That(speed,Is.GreaterThan(0));
            flight.SetExternalInput(Vector3.zero,Vector2.zero);Tick(flight);
            Assert.That(flight.CurrentSpeed,Is.EqualTo(speed).Within(.001f),"Without reverse thrust there is no hidden base braking force.");
        }

        [Test]public void LosingAllTorqueNozzlesDoesNotEraseStoredAngularMomentum()
        {
            var flight=CreateFlight(out var runtime,out _,true,true,false);flight.SetExternalInput(Vector3.zero,new Vector2(.3f,0));
            for(int i=0;i<4;i++)Tick(flight);
            var angularField=typeof(ShipFlightController).GetField("angularVelocity",BindingFlags.Instance|BindingFlags.NonPublic);
            float before=((Vector3)angularField.GetValue(flight)).y;Assert.That(Mathf.Abs(before),Is.GreaterThan(.01f));
            foreach(var part in runtime.Parts)if(part.record.shape==BlockShape.Thruster1&&part.present)runtime.SetPartPresent(part.record.id,false);
            flight.SetExternalInput(Vector3.zero,Vector2.zero);Tick(flight);
            float after=((Vector3)angularField.GetValue(flight)).y;
            Assert.That(after,Is.EqualTo(before).Within(.0001f),"Removing engine authority cannot instantaneously remove angular momentum.");
        }

        [Test]public void LowerMassAndHigherThrustRemainDistinctBeyondTheFormerAngularHardCaps()
        {
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());
            tuning.maxAngularAcceleration=10000;tuning.maxAngularRate=1000;tuning.maxRollRate=1000;
            var parts=TorqueAuthorityFixture();
            var light=ShipPerformance.Calculate(parts,Quaternion.identity,tuning);
            for(int x=-3;x<=3;x+=6)for(int y=-2;y<=2;y+=4)for(int z=-3;z<=3;z+=6)
                parts.Add(new ShipPartState{record=new BlockRecord{shape=BlockShape.Cube,position=new Vector3(x,y+.5f,z),rotation=Quaternion.identity},hp=60});
            var heavy=ShipPerformance.Calculate(parts,Quaternion.identity,tuning);
            Assert.That(light.angularRate,Is.GreaterThan(heavy.angularRate*1.25f));
            Assert.That(light.pitchAcceleration,Is.GreaterThan(heavy.pitchAcceleration*1.25f));
            Assert.That(light.rollSpeed,Is.GreaterThan(heavy.rollSpeed*1.25f));
            var additional=TorqueAuthorityFixture(4);parts.AddRange(additional.FindAll(p=>p.record.shape==BlockShape.Thruster2));
            var moreThrust=ShipPerformance.Calculate(parts,Quaternion.identity,tuning);
            Assert.That(moreThrust.angularRate,Is.GreaterThan(heavy.angularRate));
            Assert.That(moreThrust.angularRate,Is.LessThan(tuning.maxAngularRate));
        }

        [Test]public void BoostPreservesVirtualStickOffsetAndScalesAngularMotionOnce()
        {
            var flight=CreateFlight(out _,out _,true,true,false);var input=new Vector2(.3f,0);
            flight.SetExternalInput(Vector3.zero,input);Tick(flight);
            var angularField=typeof(ShipFlightController).GetField("angularVelocity",BindingFlags.Instance|BindingFlags.NonPublic);
            var baseline=(Vector3)angularField.GetValue(flight);
            flight.ResetMotion(Vector3.zero,Quaternion.identity);
            flight.SetExternalInput(Vector3.zero,input,boost:true);flight.SetBoostPowerRatio(1);Tick(flight);
            Assert.That(flight.AimInput,Is.EqualTo(input));
            var boosted=(Vector3)angularField.GetValue(flight);
            Assert.That(boosted.x,Is.EqualTo(baseline.x*1.5f).Within(.0001f));
            Assert.That(boosted.y,Is.EqualTo(baseline.y*1.5f).Within(.0001f));
            flight.SetBoostPowerRatio(0);Tick(flight);Assert.That(flight.AimInput,Is.EqualTo(input));
        }

        ShipFlightController CreateFlight(out FlightShipRuntime runtime,out WeaponDefinition weapon,bool addReverseEngine=true,bool addYawPair=true,bool addLaser=true)
        {
            var database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            database.modules=new List<ModuleDefinition>();
            foreach(var shape in new[]{BlockShape.Core,BlockShape.Thruster1,BlockShape.Laser}){
                var definition=Own(ScriptableObject.CreateInstance<ModuleDefinition>());definition.shape=shape;
                definition.massKg=100;definition.maxHp=100;definition.occupiedCells=new[]{Vector3Int.zero};
                definition.category=shape==BlockShape.Core?ModuleCategory.Core:shape==BlockShape.Laser?ModuleCategory.Weapon:ModuleCategory.Propulsion;
                if(shape==BlockShape.Thruster1)definition.thrustNewtons=6000;
                if(shape==BlockShape.Laser){definition.weapon=weapon;definition.muzzleLocalPosition=Vector3.forward*.6f;}
                database.modules.Add(definition);
            }
            var go=Own(new GameObject("Power flight fixture"));var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();
            typeof(ShipAssembly).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(assembly,null);
            assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Thruster1,new Vector3(1,.5f,0),Quaternion.Euler(-90,0,0)),Is.True);
            if(addYawPair)Assert.That(assembly.Place(BlockShape.Thruster1,new Vector3(-1,.5f,0),Quaternion.Euler(-90,0,0)),Is.True);
            if(addReverseEngine)Assert.That(assembly.Place(BlockShape.Thruster1,new Vector3(0,.5f,1),Quaternion.Euler(90,0,0)),Is.True);
            if(addLaser)Assert.That(assembly.Place(BlockShape.Laser,new Vector3(0,1.5f,0),Quaternion.identity),Is.True);
            runtime=go.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,assembly.Export("power flight"),tuning);
            var flight=go.AddComponent<ShipFlightController>();flight.Initialize(body,runtime,tuning,null,1);
            flight.SetExternalInput(Vector3.zero,Vector2.zero);return flight;
        }
        static List<ShipPartState> TorqueAuthorityFixture(int offset=2)
        {
            var parts=new List<ShipPartState>{new ShipPartState{record=new BlockRecord{shape=BlockShape.Core,position=new Vector3(0,.5f,0),rotation=Quaternion.identity},hp=300}};
            Add(new Vector3(0,.5f,offset),Vector3.up,"pitch+"+offset);Add(new Vector3(0,.5f,-offset),Vector3.up,"pitch-"+offset);
            Add(new Vector3(offset,.5f,0),Vector3.back,"yaw+"+offset);Add(new Vector3(-offset,.5f,0),Vector3.back,"yaw-"+offset);
            Add(new Vector3(0,.5f+offset,0),Vector3.left,"roll+"+offset);Add(new Vector3(0,.5f-offset,0),Vector3.left,"roll-"+offset);
            return parts;
            void Add(Vector3 position,Vector3 exhaust,string id)=>parts.Add(new ShipPartState{record=new BlockRecord{id=id,shape=BlockShape.Thruster2,position=position,rotation=BlockPlacement.FaceRotation(exhaust,0)},hp=200});
        }
        static void Tick(ShipFlightController flight)=>typeof(ShipFlightController).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(flight,null);
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
