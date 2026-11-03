using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Starframe.App;

namespace Starframe.Tests
{
    public sealed class IncomingMissileVoiceTests
    {
        GameAudioRuntime runtime;
        [UnitySetUp]public IEnumerator SetUp()
        {
            GameAudio.Apply();yield return null;
            runtime=Object.FindFirstObjectByType<GameAudioRuntime>();
            Assert.That(runtime,Is.Not.Null);
            foreach(var source in runtime.GetComponentsInChildren<AudioSource>())if(!source.loop)source.Stop();
            yield return new WaitForSecondsRealtime(.75f);
        }
        [TearDown]public void TearDown(){GameAudio.StopMissileWarning();}

        [UnityTest]public IEnumerator IncomingVoiceSurvivesFullHigherPriorityGenericPoolWithoutExceedingBudget()
        {
            var cue=runtime.Tuning.Get(GameSound.CoreDestroyed);
            int priority=cue.priority;
            try
            {
                cue.priority=0;
                for(int i=0;i<64;i++)runtime.Emit(GameSound.CoreDestroyed,channel:1000+i);
                Assert.That(runtime.Emit(GameSound.MissileIncoming),Is.True,"The dedicated alarm must remain available under generic voice saturation.");
                var alarm=runtime.GetComponentsInChildren<AudioSource>().Single(source=>source.name=="Incoming missile warning");
                for(int i=0;i<64;i++)runtime.Emit(GameSound.CoreDestroyed,channel:2000+i);
                Assert.That(alarm.isPlaying,Is.True,"Even higher-priority generic cues cannot steal the single incoming alarm source.");
                Assert.That(runtime.ActiveEffectVoices,Is.LessThanOrEqualTo(runtime.Tuning.effectVoices));
                Assert.That(runtime.Emit(GameSound.MissileIncoming),Is.False,"Immediate repeats are still rate limited.");
                GameAudio.StopMissileWarning();
                Assert.That(alarm.isPlaying,Is.False);
                Assert.That(runtime.ActiveEffectVoices,Is.GreaterThan(0),"Stopping the alarm must retain independent combat sounds.");
            }
            finally{cue.priority=priority;}
            yield break;
        }

        [UnityTest]public IEnumerator AlarmStopAndWorldDetachLeaveEnemyLockCueIndependent()
        {
            Assert.That(runtime.Emit(GameSound.EnemyLockWarning),Is.True);
            Assert.That(runtime.Emit(GameSound.MissileIncoming),Is.True);
            var sources=runtime.GetComponentsInChildren<AudioSource>();
            var alarm=sources.Single(source=>source.name=="Incoming missile warning");
            var lockClip=runtime.Tuning.Get(GameSound.EnemyLockWarning).clip;
            GameAudio.StopMissileWarning();
            Assert.That(alarm.isPlaying,Is.False);
            Assert.That(sources.Any(source=>source.isPlaying&&source.clip==lockClip),Is.True);
            Assert.That(runtime.Emit(GameSound.MissileIncoming),Is.True,"A fresh threat can sound immediately after lifecycle cleanup.");
            runtime.GetComponent<ThreatAudioDriver>().Bind(null);
            Assert.That(alarm.isPlaying,Is.False,"Scene/world detach must remove the alarm tail and pending beat.");
            yield break;
        }
    }
}
