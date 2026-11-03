using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Starframe.App;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class FlightAudioSceneTests
    {
        const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        GameAudioRuntime runtime;
        Scene previousScene,missionScene,menuScene;
        GameObject bootstrapHost;

        [UnitySetUp]public IEnumerator SetUp()
        {
            previousScene=SceneManager.GetActiveScene();
            GameAudio.Apply();yield return null;
            runtime=UnityEngine.Object.FindFirstObjectByType<GameAudioRuntime>();
            Assert.That(runtime,Is.Not.Null);
        }

        [UnityTearDown]public IEnumerator TearDown()
        {
            if(bootstrapHost)UnityEngine.Object.Destroy(bootstrapHost);
            if(previousScene.IsValid()&&previousScene.isLoaded)Loaded(previousScene);
            if(missionScene.IsValid()&&missionScene.isLoaded)yield return SceneManager.UnloadSceneAsync(missionScene);
            if(menuScene.IsValid()&&menuScene.isLoaded)yield return SceneManager.UnloadSceneAsync(menuScene);
            yield return null;
        }

        [UnityTest]public IEnumerator MissionBootstrapSelectsFlightAudioBeforeItsWorldIsReadyAndMenusDetachIt()
        {
            missionScene=SceneManager.CreateScene("MissionAudio_"+Guid.NewGuid().ToString("N"));
            Assert.That(missionScene.name,Is.Not.EqualTo(FlightSession.RangeScene));
            bootstrapHost=new GameObject("Delayed mission bootstrap");
            SceneManager.MoveGameObjectToScene(bootstrapHost,missionScene);
            // No map or live actors are needed: the real bootstrap is present before its Start coroutine can finish.
            bootstrapHost.AddComponent<FlightRangeBootstrap>().enabled=false;
            Loaded(missionScene);
            Assert.That(Field("flightScene"),Is.True,"A mission with a separate scene name must enter deferred combat-world discovery.");
            Assert.That(Field("world"),Is.Null,"Entering flight audio must still wait for a prepared world and player.");
            Assert.That(Field("musicTarget"),Is.EqualTo(runtime.Tuning.flightMusic));
            menuScene=SceneManager.CreateScene("MenuAudio_"+Guid.NewGuid().ToString("N"));
            Loaded(menuScene);
            Assert.That(Field("flightScene"),Is.False);
            Assert.That(Field("world"),Is.Null);
            Assert.That(Field("musicTarget"),Is.EqualTo(runtime.Tuning.workbenchMusic));
            yield break;
        }

        void Loaded(Scene scene)=>typeof(GameAudioRuntime).GetMethod("SceneLoaded",PrivateInstance).Invoke(runtime,new object[]{scene,LoadSceneMode.Single});
        object Field(string name)=>typeof(GameAudioRuntime).GetField(name,PrivateInstance).GetValue(runtime);
    }
}
