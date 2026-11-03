using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class Thruster6Tests
    {
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        ModuleDatabase database;FlightTuning tuning;
        static readonly Vector3[] Exhausts={Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};
        [SetUp]public void Setup()
        {
            database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var core=Definition(BlockShape.Core,100,0);var cube=Definition(BlockShape.Cube,100,0);var engine=Definition(BlockShape.Thruster6,400,45000);
            engine.localExhaustDirections=(Vector3[])Exhausts.Clone();engine.localNozzlePositions=Exhausts.Select(d=>d*.49f).ToArray();
            var power=Definition(BlockShape.Power,100,0);var laser=Definition(BlockShape.Laser,100,0);
            laser.occupiedCells=new[]{Vector3Int.zero,Vector3Int.forward,new Vector3Int(0,0,2)};
            database.modules=new List<ModuleDefinition>{core,cube,engine,power,laser};BlockModules.Configure(database);
            tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;tuning.maxLinearAcceleration=1000000;
        }
        [TearDown]public void Cleanup(){BlockModules.Configure(null);for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.Clear();}
        T Own<T>(T item)where T:UnityEngine.Object{owned.Add(item);return item;}
        ModuleDefinition Definition(BlockShape shape,float mass,float thrust)
        {var d=Own(ScriptableObject.CreateInstance<ModuleDefinition>());d.shape=shape;d.massKg=mass;d.maxHp=80;d.thrustNewtons=thrust;return d;}
        static BlockRecord Part(BlockShape shape,Vector3Int cell,Quaternion rotation)=>new BlockRecord{id=Guid.NewGuid().ToString("N"),shape=shape,position=(Vector3)cell+Vector3.up*.5f,rotation=rotation};
        ShipPartState Engine(Quaternion rotation)=>new ShipPartState{record=Part(BlockShape.Thruster6,Vector3Int.zero,rotation),hp=80,maxHp=80};
        [Test]public void FiveDirectionsRotateWithTheModuleAndLeaveOnlyItsMountingAxisWithoutMainThrust()
        {
            foreach(var normal in new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.left,Vector3.forward,Vector3.back})for(int turn=0;turn<4;turn++)
            {
                var rotation=BlockPlacement.FaceRotation(normal,turn);var performance=ShipPerformance.Calculate(new[]{Engine(rotation)},Quaternion.identity,tuning);
                var missing=Vector3Int.RoundToInt(rotation*Vector3.up);
                for(int axis=0;axis<3;axis++)
                {
                    // Rotation leaves a sub-millimetre epsilon on the mounting axis; it is not usable thrust.
                    Assert.That(performance.accelerationPositive[axis]>1e-3f,Is.EqualTo(missing[axis]!=1),normal+" "+turn+" +"+axis);
                    Assert.That(performance.accelerationNegative[axis]>1e-3f,Is.EqualTo(missing[axis]!=-1),normal+" "+turn+" -"+axis);
                }
            }
        }
        [Test]public void EachRotatedNozzleKeepsItsRuntimeCellWhilePlacementUsesAHalfCellJudgement()
        {
            foreach(var normal in new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.left,Vector3.forward,Vector3.back})for(int turn=0;turn<4;turn++)
            {
                var rotation=BlockPlacement.FaceRotation(normal,turn);var engine=Part(BlockShape.Thruster6,Vector3Int.zero,rotation);
                Assert.That(BlockModules.BodyCells(engine).Count,Is.EqualTo(1));Assert.That(BlockModules.PlacementCells(engine).Count,Is.EqualTo(1));
                Assert.That(BlockModules.ExhaustCells(engine).Count,Is.EqualTo(5));
                for(int nozzle=0;nozzle<5;nozzle++)
                {
                    var cell=Vector3Int.RoundToInt(rotation*Exhausts[nozzle]);
                    CollectionAssert.AreEquivalent(new[]{cell},BlockModules.ExhaustCells(engine,nozzle));
                    var cube=Part(BlockShape.Cube,cell,Quaternion.identity);
                    Assert.That(BlockModules.CellsOverlap(engine,cube),Is.False,"A flush block is allowed by the half-cell placement judgement.");
                    Assert.That(BlockModules.CellsOverlap(cube,engine),Is.False);
                }
                var mount=Vector3Int.RoundToInt(rotation*Vector3.down);
                Assert.That(BlockModules.CellsOverlap(engine,Part(BlockShape.Cube,mount,Quaternion.identity)),Is.False);
                Assert.That(BlockConnectivity.Touches(engine,Part(BlockShape.Cube,mount,Quaternion.identity)),Is.True);
            }
            foreach(var direction in new[]{Vector3Int.up,Vector3Int.right,Vector3Int.left,Vector3Int.forward,Vector3Int.back})
                Assert.That(BlockModules.HasAttachmentSurface(BlockShape.Thruster6,Vector3Int.zero,direction),Is.False);
        }
        [Test]public void FiveAlternativeNozzlesContributeOnlyOneThrustRating()
        {
            var part=Engine(Quaternion.identity);var performance=ShipPerformance.Calculate(new[]{part},Quaternion.identity,tuning);
            Assert.That(performance.totalThrust,Is.EqualTo(45000));Assert.That(performance.assistAcceleration,Is.EqualTo(45000*tuning.maneuverThrustFraction/400).Within(.001f));
            part.availableExhaustMask=1;var one=ShipPerformance.Calculate(new[]{part},Quaternion.identity,tuning);
            Assert.That(one.totalThrust,Is.EqualTo(performance.totalThrust));Assert.That(one.assistAcceleration,Is.EqualTo(performance.assistAcceleration));
            part.availableExhaustMask=0;var none=ShipPerformance.Calculate(new[]{part},Quaternion.identity,tuning);Assert.That(none.totalThrust,Is.Zero);Assert.That(none.assistAcceleration,Is.Zero);
        }
        [Test]public void MultiNozzleModuleSharesItsRatedBudgetAcrossSimultaneousAxes()
        {
            var part=Engine(Quaternion.identity);var performance=ShipPerformance.Calculate(new[]{part},Quaternion.identity,tuning);
            var acceleration=performance.AllocateWrench(new Vector3(1,0,1),Vector3.zero,tuning,out _);
            float allocated=0;foreach(var nozzle in performance.nozzles)allocated+=nozzle.allocatedThrottle;
            Assert.That(allocated,Is.EqualTo(1).Within(.001f));
            Assert.That(acceleration.x,Is.EqualTo(acceleration.z).Within(.001f));
            Assert.That(acceleration.x,Is.EqualTo(45000f/400*.5f).Within(.01f));
        }
        [Test]public void EccentricNozzlesCannotMultiplyOneModulesBudgetForTheSameTorqueAxis()
        {
            var core=new ShipPartState{record=Part(BlockShape.Core,Vector3Int.zero,Quaternion.identity),hp=80,maxHp=80};
            var engine=Engine(Quaternion.identity);engine.record.position=new Vector3(1,.5f,1);
            var performance=ShipPerformance.Calculate(new[]{core,engine},Quaternion.identity,tuning);
            performance.AllocateWrench(Vector3.zero,Vector3.up,tuning,out _);
            float pureYaw=0;foreach(var nozzle in performance.nozzles)pureYaw+=nozzle.allocatedThrottle;
            Assert.That(pureYaw,Is.LessThanOrEqualTo(1.001f));
            performance.AllocateWrench(new Vector3(1,0,1),Vector3.up,tuning,out _);
            float combined=0;foreach(var nozzle in performance.nozzles)combined+=nozzle.allocatedThrottle;
            Assert.That(combined,Is.LessThanOrEqualTo(1.001f));
        }
        [Test]public void IndependentEnginesAddTheirFullThrustToTheAllocatedAcceleration()
        {
            var first=Engine(Quaternion.identity);var second=Engine(Quaternion.identity);second.record.position=new Vector3(2,.5f,0);
            var hull=new List<ShipPartState>();for(int i=0;i<200;i++)hull.Add(new ShipPartState{record=Part(BlockShape.Cube,new Vector3Int(i,2,0),Quaternion.identity),hp=80,maxHp=80});
            var oneParts=new List<ShipPartState>(hull){first};var twoParts=new List<ShipPartState>(hull){first,second};
            var one=ShipPerformance.Calculate(oneParts,Quaternion.identity,tuning);
            var two=ShipPerformance.Calculate(twoParts,Quaternion.identity,tuning);
            var oneAccel=one.AllocateWrench(Vector3.right,Vector3.zero,tuning,out _);
            var twoAccel=two.AllocateWrench(Vector3.right,Vector3.zero,tuning,out _);
            Assert.That(twoAccel.x,Is.GreaterThan(oneAccel.x*1.8f));
            Assert.That(twoAccel.x,Is.EqualTo(two.accelerationPositive.x).Within(.01f));
            Assert.That(two.AllocatedThrottle(first,2),Is.EqualTo(1).Within(.001f));
            Assert.That(two.AllocatedThrottle(second,2),Is.EqualTo(1).Within(.001f));
        }
        [Test]public void OffCentreThrustProducesTorqueAndBodyMassChangesTheInertia()
        {
            var centred=Engine(Quaternion.identity);var atOrigin=ShipPerformance.Calculate(new[]{centred},Quaternion.identity,tuning);
            var core=new ShipPartState{record=Part(BlockShape.Core,Vector3Int.zero,Quaternion.identity),hp=80,maxHp=80};
            var offset=Engine(Quaternion.identity);offset.record.position=new Vector3(2,.5f,0);
            var offCentre=ShipPerformance.Calculate(new[]{core,offset},Quaternion.identity,tuning);
            Assert.That(atOrigin.angularAccelerationPositive.sqrMagnitude+atOrigin.angularAccelerationNegative.sqrMagnitude,Is.EqualTo(0));
            Assert.That(offCentre.angularAccelerationPositive.sqrMagnitude+offCentre.angularAccelerationNegative.sqrMagnitude,Is.GreaterThan(0));
            float before=offCentre.inertiaTensorKgM2.z;
            var cargo=new ShipPartState{record=Part(BlockShape.Cube,new Vector3Int(8,0,0),Quaternion.identity),hp=80,maxHp=80};
            var heavier=ShipPerformance.Calculate(new[]{core,offset,cargo},Quaternion.identity,tuning);
            Assert.That(heavier.inertiaTensorKgM2.z,Is.GreaterThan(before));
        }
        (GameObject host,ShipAssembly assembly,FlightShipRuntime runtime,ShipPartState engine,ShipPartState blocker) Fixture(bool flush=false)
        {
            var host=Own(new GameObject("Five nozzle regression"));var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,-.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Thruster6,Vector3.up*.5f,Quaternion.identity),Is.True,assembly.PlacementFailure);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(1,-.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(2,-.5f,0),Quaternion.identity),Is.True);
            Assert.That(assembly.Place(BlockShape.Cube,new Vector3(2,.5f,0),Quaternion.identity),Is.True);
            if(flush)Assert.That(assembly.Place(BlockShape.Cube,new Vector3(1,.5f,0),Quaternion.identity),Is.True,"The half-cell judgement allows a flush block in the +X exhaust cell.");
            var blueprint=assembly.Export("Five nozzle fixture");var runtime=host.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);
            return(host,assembly,runtime,runtime.Parts.Single(p=>p.record.shape==BlockShape.Thruster6),runtime.Parts.Single(p=>(p.record.position-new Vector3(2,.5f,0)).sqrMagnitude<.001f));
        }
        [Test]public void AFlushBlockIsPlaceableButStillClosesOnlyItsNozzle()
        {
            var fixture=Fixture(true);
            Assert.That(fixture.engine.availableExhaustMask,Is.EqualTo(31&~(1<<1)));
            for(int nozzle=0;nozzle<5;nozzle++)Assert.That(fixture.engine.ExhaustAvailable(nozzle),Is.EqualTo(nozzle!=1));
        }
        [Test]public void OneObstructedNozzleLosesOnlyItsAxisAndRemovingOrRestoringTheBlockerRefreshesThatNozzle()
        {
            var fixture=Fixture();var before=fixture.runtime.Performance;Assert.That(fixture.engine.availableExhaustMask,Is.EqualTo(31));
            // Inject a transient obstruction into the runtime occupancy index; placement now allows a flush block.
            var owners=(Dictionary<Vector3Int,int>)typeof(FlightShipRuntime).GetField("cellOwners",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fixture.runtime);
            owners[Vector3Int.right]=fixture.runtime.Parts.IndexOf(fixture.blocker);fixture.runtime.Recalculate();
            Assert.That(fixture.engine.ExhaustAvailable(1),Is.False);
            Assert.That(fixture.engine.availableExhaustMask,Is.EqualTo(31&~(1<<1)));
            Assert.That(fixture.runtime.Performance.accelerationNegative.x,Is.Zero);
            Assert.That(fixture.runtime.Performance.accelerationPositive.x,Is.EqualTo(before.accelerationPositive.x));
            Assert.That(fixture.runtime.Performance.accelerationNegative.y,Is.EqualTo(before.accelerationNegative.y));
            Assert.That(fixture.runtime.Performance.accelerationPositive.z,Is.EqualTo(before.accelerationPositive.z));
            Assert.That(fixture.runtime.Performance.assistAcceleration,Is.EqualTo(before.assistAcceleration));
            fixture.runtime.SetPartPresent(fixture.blocker.record.id,false);Assert.That(fixture.engine.availableExhaustMask,Is.EqualTo(31));
            fixture.runtime.SetPartPresent(fixture.blocker.record.id,true);Assert.That(fixture.engine.ExhaustAvailable(1),Is.False);
            owners.Remove(Vector3Int.right);fixture.runtime.Recalculate();Assert.That(fixture.engine.availableExhaustMask,Is.EqualTo(31));
        }
        [Test]public void AssemblyPreviewUsesTheSameNozzleOcclusionAndMassAsFlightRuntime()
        {
            var fixture=Fixture();var runtime=fixture.runtime.Performance;var preview=ShipPerformance.CalculateAssembly(fixture.assembly,tuning);
            Assert.That(preview.totalMass,Is.EqualTo(runtime.totalMass).Within(.001f));
            Assert.That(preview.accelerationPositive,Is.EqualTo(runtime.accelerationPositive));
            Assert.That(preview.accelerationNegative,Is.EqualTo(runtime.accelerationNegative));
        }
        [Test]public void DefaultFlightSampleIsConnectedHasClearPlumesAndProvidesSixAxisAuthority()
        {
            database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            Assert.That(database,Is.Not.Null,"The default sample must be checked against the production module geometry.");
            BlockModules.Configure(database);tuning.database=database;
            var blueprint=FlightRangeBootstrap.DefaultShip();
            Assert.That(BlockConnectivity.IsConnected(blueprint),Is.True);
            var occupied=new HashSet<Vector3Int>();
            foreach(var part in blueprint.parts)
                foreach(var cell in BlockModules.PlacementCells(part))Assert.That(occupied.Add(cell),Is.True,"Overlapping placement cell at "+cell+" on "+part.id);

            var bodyOwners=new Dictionary<Vector3Int,string>();
            foreach(var part in blueprint.parts)foreach(var cell in BlockModules.BodyCells(part))bodyOwners[cell]=part.id;
            var states=new List<ShipPartState>();
            foreach(var part in blueprint.parts)
            {
                int mask=0;var directions=BlockModules.LocalExhaustDirections(part.shape);
                for(int nozzle=0;nozzle<directions.Length&&nozzle<31;nozzle++)
                {
                    bool clear=true;
                    foreach(var cell in BlockModules.ExhaustCells(part,nozzle))
                        if(bodyOwners.TryGetValue(cell,out var owner)&&owner!=part.id){clear=false;break;}
                    Assert.That(clear,Is.True,"Blocked nozzle "+nozzle+" on "+part.id);
                    mask|=1<<nozzle;
                }
                var stats=tuning.Get(part.shape);
                states.Add(new ShipPartState{record=part,hp=stats.maxHp,maxHp=stats.maxHp,availableExhaustMask=mask,thrustAvailable=mask!=0});
            }
            var performance=ShipPerformance.Calculate(states,Quaternion.identity,tuning);
            for(int axis=0;axis<3;axis++)
            {
                Assert.That(performance.accelerationPositive[axis],Is.GreaterThan(0),"positive translation axis "+axis);
                Assert.That(performance.accelerationNegative[axis],Is.GreaterThan(0),"negative translation axis "+axis);
                Assert.That(performance.angularAccelerationPositive[axis],Is.GreaterThan(0),"positive rotation axis "+axis);
                Assert.That(performance.angularAccelerationNegative[axis],Is.GreaterThan(0),"negative rotation axis "+axis);
            }
        }
        [Test]public void FivePlumesUsePhysicalMouthsAndDirectionsAndStayInsideTheSingleReservedCell()
        {
            var fixture=Fixture();var body=fixture.host.AddComponent<Rigidbody>();body.useGravity=false;
            var camera=Own(new GameObject("Five nozzle camera")).AddComponent<Camera>();camera.enabled=false;
            var flight=fixture.host.AddComponent<ShipFlightController>();flight.Initialize(body,fixture.runtime,tuning,camera,4);
            tuning.exhaustLength=9;tuning.exhaustMaxLength=20;tuning.exhaustIdle=.2f;
            var exhaust=fixture.host.AddComponent<ThrusterExhaust>();exhaust.Initialize(fixture.runtime,flight,tuning,camera,5);
            var list=(IList)typeof(ThrusterExhaust).GetField("engines",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(exhaust);Assert.That(list.Count,Is.EqualTo(5));
            foreach(var item in list)item.GetType().GetField("throttle").SetValue(item,1f);
            typeof(ThrusterExhaust).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(exhaust,null);
            for(int nozzle=0;nozzle<5;nozzle++)
            {
                var item=list[nozzle];var transform=(Transform)item.GetType().GetField("nozzle").GetValue(item);
                Assert.That(Vector3.Distance(transform.localPosition,Exhausts[nozzle]*.505f),Is.LessThan(.0001f));
                Assert.That(Vector3.Dot(transform.localRotation*Vector3.up,Exhausts[nozzle]),Is.GreaterThan(.999f));
                Assert.That(transform.localScale.y,Is.EqualTo(1f).Within(.001f),"The five-way plume renders one cell.");
                Assert.That(transform.localScale.x,Is.LessThanOrEqualTo(.3001f));
                Assert.That(transform.GetComponent<MeshRenderer>().enabled,Is.True);
            }
            fixture.engine.availableExhaustMask&=~(1<<1);
            typeof(ThrusterExhaust).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(exhaust,null);
            for(int nozzle=0;nozzle<5;nozzle++)
            {
                var renderer=(MeshRenderer)list[nozzle].GetType().GetField("renderer").GetValue(list[nozzle]);
                Assert.That(renderer.enabled,Is.EqualTo(nozzle!=1),"One obstructed nozzle must not extinguish its siblings.");
            }
            var accents=fixture.engine.view.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="ModuleGlowAccent").ToArray();Assert.That(accents.Length,Is.EqualTo(5));
        }
    }
}
