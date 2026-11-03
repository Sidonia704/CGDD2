using NUnit.Framework;
using Starframe.Combat;
namespace Starframe.Construction.Tests
{
    public sealed class ArcadeWaveProgressTests
    {
        [Test]public void DelayedReinforcementsPreventPrematureWaveClear()
        {
            var p=new ArcadeWaveProgress();p.Begin(3);
            Assert.IsTrue(p.Spawn(1));Assert.IsTrue(p.Defeat(1));Assert.IsFalse(p.IsCleared);
            Assert.AreEqual(2,p.Pending);Assert.AreEqual(0,p.Alive);
            Assert.IsTrue(p.Spawn(2));Assert.IsTrue(p.Spawn(3));Assert.IsFalse(p.Spawn(4));
            Assert.IsTrue(p.Defeat(2));Assert.IsFalse(p.Defeat(2));Assert.IsFalse(p.IsCleared);
            Assert.IsTrue(p.Defeat(3));Assert.IsTrue(p.IsCleared);
        }
        [Test]public void DuplicateSpawnDoesNotConsumeQueueAndNewWaveClearsOldLives()
        {
            var p=new ArcadeWaveProgress();p.Begin(2);Assert.IsTrue(p.Spawn(1));Assert.IsFalse(p.Spawn(1));Assert.AreEqual(1,p.Pending);
            p.Begin(1);Assert.IsFalse(p.Defeat(1));Assert.AreEqual(0,p.Alive);Assert.IsTrue(p.Spawn(5));Assert.IsTrue(p.Defeat(5));Assert.IsTrue(p.IsCleared);
        }
        [Test] public void AppendKeepsSurvivorsAndRequiresBothStagesToClear()
        {
            var p=new ArcadeWaveProgress();p.Begin(1);Assert.IsTrue(p.Spawn(1));
            p.Append(2);Assert.AreEqual(1,p.Alive);Assert.AreEqual(2,p.Pending);
            Assert.IsTrue(p.Spawn(2));Assert.IsTrue(p.Spawn(3));
            Assert.IsTrue(p.Defeat(2));Assert.IsTrue(p.Defeat(3));Assert.IsFalse(p.IsCleared);
            Assert.IsTrue(p.Defeat(1));Assert.IsTrue(p.IsCleared);
        }
        [Test] public void MissionNavigationLeavesCentralFlightAloneAndReturnsOutlyingShips()
        {
            var velocity=new UnityEngine.Vector3(80,0,0);
            Assert.AreEqual(velocity,RangeBotPilot.CentralCombatVelocity(UnityEngine.Vector3.zero,velocity,UnityEngine.Vector3.zero,1000,100,1));
            var outside=RangeBotPilot.CentralCombatVelocity(new UnityEngine.Vector3(1200,0,0),velocity,UnityEngine.Vector3.zero,1000,100,1);
            Assert.Less(outside.x,0);Assert.That(outside.magnitude,Is.EqualTo(100).Within(.001f));
            Assert.AreEqual(velocity,RangeBotPilot.CentralCombatVelocity(new UnityEngine.Vector3(1200,0,0),velocity,UnityEngine.Vector3.zero,0,100,1),"Practice pilots have no mission-area override.");
        }
    }
}
