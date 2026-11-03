using NUnit.Framework;
using UnityEngine;
using Starframe.App;

namespace Starframe.Construction.Tests
{
    public sealed class IncomingMissileCadenceTests
    {
        GameAudioTuning tuning;
        [SetUp]public void SetUp(){tuning=ScriptableObject.CreateInstance<GameAudioTuning>();}
        [TearDown]public void TearDown(){Object.DestroyImmediate(tuning);}

        [TestCase(600,100,.9f)]
        [TestCase(40,100,.12f)]
        [TestCase(1000,.35f,.12f)]
        public void EitherNearbyDistanceOrShortImpactTimeRaisesTheSingleAlarm(float distance,float impactTime,float expected)
        {
            Assert.That(tuning.MissileWarningInterval(distance,impactTime),Is.EqualTo(expected).Within(.0001f));
        }

        [Test]public void ClosingThreatAcceleratesWithinBoundsAndUnknownImpactDoesNotInventUrgency()
        {
            float distant=tuning.MissileWarningInterval(600,float.PositiveInfinity);
            float middle=tuning.MissileWarningInterval(320,float.PositiveInfinity);
            float imminent=tuning.MissileWarningInterval(40,float.PositiveInfinity);
            Assert.That(distant,Is.EqualTo(.9f).Within(.0001f));
            Assert.That(middle,Is.LessThan(distant).And.GreaterThan(imminent));
            Assert.That(imminent,Is.EqualTo(.12f).Within(.0001f));
            Assert.That(tuning.MissileWarningInterval(float.NaN,float.NaN),Is.EqualTo(distant));
            Assert.That(tuning.MissileWarningInterval(0,0),Is.EqualTo(imminent));
        }

        [Test]public void ReversedInspectorThresholdsAndZeroIntervalsCannotCreateAnUnboundedAlarm()
        {
            tuning.missileWarningNearDistance=600;tuning.missileWarningFarDistance=40;
            tuning.missileWarningNearTimeToImpact=4;tuning.missileWarningFarTimeToImpact=.35f;
            tuning.missileWarningNearInterval=0;tuning.missileWarningFarInterval=0;
            Assert.That(tuning.MissileWarningInterval(0,0),Is.EqualTo(.1f));
            Assert.That(tuning.MissileWarningInterval(10000,100),Is.EqualTo(.1f));
        }
    }
}
