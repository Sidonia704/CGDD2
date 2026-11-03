using System.Collections.Generic;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class BalancedFlightAxisTests
    {
        FlightTuning tuning;readonly List<ShipPartState> parts=new List<ShipPartState>();
        [SetUp]public void SetUp(){
            BlockModules.Configure(null);tuning=ScriptableObject.CreateInstance<FlightTuning>();
            tuning.maxLinearAcceleration=tuning.maxAngularAcceleration=1000000;
            parts.Clear();
            parts.Add(Part(BlockShape.Core,Vector3.zero));
            // An intentionally asymmetric layout, not a specially balanced test ship.
            parts.Add(Part(BlockShape.Thruster2,new Vector3(5,2,-3)));
            parts.Add(Part(BlockShape.Thruster2,new Vector3(-2,-1,4),Quaternion.Euler(180,0,0)));
            parts.Add(Part(BlockShape.Thruster1,new Vector3(-4,1,2),Quaternion.Euler(0,0,90)));
            parts.Add(Part(BlockShape.Thruster1,new Vector3(2,-3,-2),Quaternion.Euler(0,0,-90)));
        }
        [TearDown]public void TearDown(){Object.DestroyImmediate(tuning);BlockModules.Configure(null);}
        static ShipPartState Part(BlockShape shape,Vector3 position,Quaternion? rotation=null)=>new ShipPartState{record=new BlockRecord{shape=shape,position=position,rotation=rotation??Quaternion.identity},hp=100};
        [Test]public void EachSingleTranslationMatchesItsPublishedCapacityAndNeverInjectsTorque(){
            var p=ShipPerformance.Calculate(parts,Quaternion.identity,tuning);
            for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2){
                Vector3 command=Vector3.zero;command[axis]=sign;
                var actual=p.AllocateWrench(command,Vector3.zero,tuning,out var angular);
                Assert.That(angular,Is.EqualTo(Vector3.zero),"Off-centre engines must not rotate an arcade ship during translation.");
                for(int other=0;other<3;other++)if(other!=axis)Assert.That(actual[other],Is.Zero);
                Assert.That(Mathf.Abs(actual[axis]),Is.EqualTo((sign>0?p.accelerationPositive:p.accelerationNegative)[axis]).Within(.001f));
            }
        }
        [Test]public void RotationHasEqualOpposingAuthorityAndNoOtherMotionChannels(){
            var p=ShipPerformance.Calculate(parts,Quaternion.identity,tuning);
            Assert.That(p.angularCapacityPositive,Is.EqualTo(p.angularCapacityNegative));
            for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2){
                Vector3 command=Vector3.zero;command[axis]=sign;
                var linear=p.AllocateWrench(Vector3.zero,command,tuning,out var actual);
                Assert.That(linear,Is.EqualTo(Vector3.zero));
                for(int other=0;other<3;other++)if(other!=axis)Assert.That(actual[other],Is.Zero);
                Assert.That(Mathf.Abs(actual[axis]),Is.EqualTo(p.angularAccelerationPositive[axis]).Within(.001f));
                foreach(var part in parts){float load=0;foreach(var n in p.nozzles)if(n.part==part)load+=n.allocatedThrottle;Assert.That(load,Is.LessThanOrEqualTo(1.00001f));}
            }
            p.AllocateWrench(Vector3.one,Vector3.one,tuning,out _);
            foreach(var part in parts){float load=0;foreach(var n in p.nozzles)if(n.part==part)load+=n.allocatedThrottle;Assert.That(load,Is.LessThanOrEqualTo(1.00001f));}
        }
        [Test]public void DamageRecalculationCannotIntroduceUncommandedRotation(){
            parts[1].present=false;var p=ShipPerformance.Calculate(parts,Quaternion.identity,tuning);
            p.AllocateWrench(Vector3.forward,Vector3.zero,tuning,out var angular);
            Assert.That(angular,Is.EqualTo(Vector3.zero));
            Assert.That(p.angularCapacityPositive,Is.EqualTo(p.angularCapacityNegative));
        }
    }
}
