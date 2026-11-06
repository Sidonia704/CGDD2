using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Flight;
using Starframe.Combat;
namespace Starframe.Construction.Tests
{
    public sealed class GlobalBalanceMultiplierTests
    {
        [TestCase(WeaponDelivery.Projectile)][TestCase(WeaponDelivery.Beam)][TestCase(WeaponDelivery.GuidedMissile)]
        public void GlobalDamageComposesOnceWithFactionAndCategory(WeaponDelivery delivery)
        {
            var d=ScriptableObject.CreateInstance<CombatDamageTuning>();
            try{float enemy=d.GetMultiplier(delivery),player=d.GetPlayerMultiplier(delivery);d.globalDamageMultiplier=2;
                Assert.That(d.GetMultiplier(delivery),Is.EqualTo(enemy*2));Assert.That(d.GetPlayerMultiplier(delivery),Is.EqualTo(player*2));}
            finally{Object.DestroyImmediate(d);}
        }
        [Test]public void ModuleMultipliersDoNotMutateNativeStatsAndReachMassInertiaAndThrust()
        {
            var t=ScriptableObject.CreateInstance<FlightTuning>();float originalMass=t.Get(BlockShape.Thruster1).massKg;
            var parts=new[]{new ShipPartState{record=new BlockRecord{shape=BlockShape.Thruster1,position=Vector3.zero,rotation=Quaternion.identity},hp=100}};
            try{
                var p=ShipPerformance.Calculate(parts,Quaternion.identity,t);t.blockMassMultiplier=2;t.thrustMultiplier=3;
                var q=ShipPerformance.Calculate(parts,Quaternion.identity,t);
                Assert.That(q.totalMass,Is.EqualTo(p.totalMass*2));Assert.That(q.totalThrust,Is.EqualTo(p.totalThrust*3));
                Assert.That(q.inertiaTensorKgM2.x,Is.EqualTo(p.inertiaTensorKgM2.x*2).Within(.001f));
                Assert.That(originalMass,Is.EqualTo(t.Get(BlockShape.Thruster1).massKg/2));
                Assert.That(t.modules[2].massKg,Is.EqualTo(600));
            }finally{Object.DestroyImmediate(t);}
        }
        [TestCase(50000,1)][TestCase(100000,1)][TestCase(500000,.3513f)]
        public void HeavySpeedIsContinuousAndSubstantiallyReduced(float mass,float expected)
        {
            var t=ScriptableObject.CreateInstance<FlightTuning>();
            try{Assert.That(t.HeavySpeedScale(mass),Is.EqualTo(expected).Within(.002f));Assert.That(t.HeavyManeuverScale(mass),Is.LessThanOrEqualTo(1));}
            finally{Object.DestroyImmediate(t);}
        }
        [Test]public void HeavyPublishedAxesMatchActualAllocationAndStoppingDistance()
        {
            var t=ScriptableObject.CreateInstance<FlightTuning>();t.blockMassMultiplier=100;
            var parts=new List<ShipPartState>{new ShipPartState{record=new BlockRecord{shape=BlockShape.Core},hp=100},
                new ShipPartState{record=new BlockRecord{shape=BlockShape.Thruster2,position=Vector3.left*3},hp=100},
                new ShipPartState{record=new BlockRecord{shape=BlockShape.Thruster2,position=Vector3.right*3,rotation=Quaternion.Euler(180,0,0)},hp=100}};
            try{
                var p=ShipPerformance.Calculate(parts,Quaternion.identity,t);Assert.That(p.maxSpeed,Is.LessThan(t.maxSpeed*.5f));
                var actual=p.AllocateWrench(Vector3.forward,Vector3.zero,t,out _);
                Assert.That(actual.z,Is.EqualTo(p.accelerationPositive.z).Within(.001f));
                Assert.That(p.maxSpeedStoppingDistancePositive.z,Is.EqualTo(p.maxSpeed*p.maxSpeed/(2*p.accelerationNegative.z)).Within(.01f));
            }finally{Object.DestroyImmediate(t);}
        }
    }
}
