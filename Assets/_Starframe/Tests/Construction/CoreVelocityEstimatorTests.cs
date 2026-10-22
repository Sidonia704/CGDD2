using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
namespace Starframe.Tests
{
    public sealed class CoreVelocityEstimatorTests
    {
        [Test]public void MultiSampleEstimateTracksConstantMotionAndIgnoresRepeatedPhysicsTime()
        {
            var tracker=new CoreVelocityEstimator();var origin=new Vector3(1500,-830,490);var velocity=new Vector3(7,2,-3);tracker.Reset(origin,0,velocity);
            for(int i=1;i<=30;i++)tracker.Sample(origin+velocity*(i*.02f),i*.02,.16f,.04f,velocity);
            Assert.That((tracker.Velocity-velocity).magnitude,Is.LessThan(.01f));Assert.That(tracker.SampleCount,Is.GreaterThan(5));
            var before=tracker.Velocity;int count=tracker.SampleCount;tracker.Sample(origin+Vector3.one*50,.6,.16f,.04f,Vector3.zero);
            Assert.That(tracker.Velocity,Is.EqualTo(before));Assert.That(tracker.SampleCount,Is.EqualTo(count));
        }
        [Test]public void SamplingSmoothsSmallFrameNoiseAndResetsForNewTarget()
        {
            var tracker=new CoreVelocityEstimator();var velocity=Vector3.right*7;tracker.Reset(Vector3.zero,0,velocity);
            for(int i=1;i<=40;i++){
                var position=velocity*(i*.02f)+Vector3.right*(i%2==0?.01f:-.01f);var before=tracker.Velocity;
                tracker.Sample(position,i*.02,.16f,.04f,velocity);
                if(i>10){Assert.That((tracker.Velocity-velocity).magnitude,Is.LessThan(.1f));Assert.That((tracker.Velocity-before).magnitude,Is.LessThan(.1f));}
            }
            tracker.Clear();Assert.That(tracker.SampleCount,Is.Zero);tracker.Reset(Vector3.one*100,5,Vector3.up*3);Assert.That(tracker.SampleCount,Is.EqualTo(1));Assert.That(tracker.Velocity,Is.EqualTo(Vector3.up*3));
        }
        [Test]public void OffsetCoreRotationContributesToItsMeasuredVelocity()
        {
            var tracker=new CoreVelocityEstimator();tracker.Reset(Vector3.right*3,0,Vector3.zero);
            for(int i=1;i<=20;i++){float t=i*.02f;tracker.Sample(new Vector3(3*Mathf.Cos(t),0,3*Mathf.Sin(t)),i*.02,.16f,.04f,Vector3.zero);}
            var tangent=new Vector3(-3*Mathf.Sin(.4f),0,3*Mathf.Cos(.4f));
            Assert.That(tracker.Velocity.magnitude,Is.InRange(2.8f,3.2f));Assert.That(Vector3.Angle(tracker.Velocity,tangent),Is.LessThan(15));
        }
    }
}
