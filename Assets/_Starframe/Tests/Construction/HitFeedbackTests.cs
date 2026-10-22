using NUnit.Framework;
using Starframe.Presentation;

namespace Starframe.Tests
{
    public sealed class HitFeedbackTests
    {
        [Test]public void DenseHitsAreCappedAndDoNotQueueDelayedFlashes()
        {
            var feedback=new ReticleHitFeedback();
            Assert.That(feedback.TryTrigger(0,8),Is.True);
            for(int i=0;i<12;i++)Assert.That(feedback.TryTrigger(.01*i,8),Is.False);
            Assert.That(feedback.TryTrigger(.125,8),Is.True);
            feedback.Sample(1,.24f,out float strength,out _);
            Assert.That(strength,Is.Zero,"Suppressed hits must not become a delayed backlog.");
            Assert.That(feedback.TryTrigger(1,8),Is.True);
        }
        [Test]public void HitSegmentsGrowWhileCrossContractsThenReturnToIdle()
        {
            var feedback=new ReticleHitFeedback();
            feedback.Sample(0,.24f,out float idle,out _);Assert.That(idle,Is.Zero);
            feedback.TryTrigger(0,8);
            feedback.Sample(.01,.24f,out float early,out float shortLine);
            feedback.Sample(.06,.24f,out float peak,out float longLine);
            Assert.That(peak,Is.GreaterThan(.9f));Assert.That(peak,Is.GreaterThan(early));
            Assert.That(longLine,Is.GreaterThan(shortLine+.5f),"Animation must change segment length, not just alpha.");
            feedback.Sample(.20,.24f,out float fading,out _);Assert.That(fading,Is.LessThan(peak*.2f));
            feedback.Sample(.25,.24f,out float ended,out _);Assert.That(ended,Is.Zero);
            feedback.Reset();Assert.That(feedback.TryTrigger(.26,8),Is.True);
        }
    }
}
