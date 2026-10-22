using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Starframe.App;

namespace Starframe.Tests
{
    public sealed class GameAudioTests
    {
        GameSettingsData saved;
        GameAudioRuntime runtime;
        [UnitySetUp]public IEnumerator SetUp()
        {
            saved=GameSettings.Current.Clone();GameAudio.Apply();yield return null;
            runtime=Object.FindFirstObjectByType<GameAudioRuntime>();
            Assert.That(runtime,Is.Not.Null);Assert.That(runtime.Tuning,Is.Not.Null);
            foreach(var source in runtime.GetComponentsInChildren<AudioSource>())if(!source.loop)source.Stop();
            yield return new WaitForSecondsRealtime(.75f);
        }
        [TearDown]public void TearDown(){GameAudio.Preview(saved);}

        [UnityTest]public IEnumerator BurstLimitDoesNotSilenceDestructionAndVoicesRemainBounded()
        {
            int accepted=0;
            for(int i=0;i<200;i++)if(runtime.Emit(GameSound.LaserShot))accepted++;
            Assert.That(accepted,Is.EqualTo(1),"Synchronized gun batteries must not sum hundreds of identical sounds.");
            foreach(GameSound sound in System.Enum.GetValues(typeof(GameSound)))runtime.Emit(sound);
            Assert.That(runtime.ActiveEffectVoices,Is.LessThanOrEqualTo(runtime.Tuning.effectVoices));
            Assert.That(runtime.Emit(GameSound.CoreDestroyed,channel:2),Is.True,"A destruction cue can replace a less important active voice.");
            yield return null;
        }

        [UnityTest]public IEnumerator PreviewRoutesVolumesAndDoesNotRewritePreferences()
        {
            string before=JsonUtility.ToJson(GameSettings.Current);
            var draft=saved.Clone();draft.masterVolume=.65f;draft.musicVolume=0;draft.sfxVolume=0;draft.uiVolume=.37f;draft.muteWhenUnfocused=false;
            GameAudio.Preview(draft);GameAudio.PlayUiClick();GameAudio.PreviewSfx();yield return null;
            var select=runtime.Tuning.Get(GameSound.Select);var shot=runtime.Tuning.Get(GameSound.LaserShot);
            var sources=runtime.GetComponentsInChildren<AudioSource>();
            Assert.That(sources.Any(s=>s.clip==select.clip&&Mathf.Abs(s.volume-select.gain*.37f)<.001f),Is.True);
            Assert.That(sources.Any(s=>s.clip==shot.clip&&s.volume==0),Is.True);
            Assert.That(AudioListener.volume,Is.EqualTo(.65f).Within(.001f));
            Assert.That(JsonUtility.ToJson(GameSettings.Current),Is.EqualTo(before));
        }
    }
}
