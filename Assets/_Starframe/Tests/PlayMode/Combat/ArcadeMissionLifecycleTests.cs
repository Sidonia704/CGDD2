using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Starframe.Tests
{
    // Uses the real assembly, damage, debris, sensors and wave controller, without constructing the reference map.
    public sealed class ArcadeMissionLifecycleTests
    {
        Scene fixtureScene, previousScene;
        ModuleDatabase previousDatabase;
        bool previousSuspended, previousCursorVisible;
        float previousScale;
        CursorLockMode previousCursorLock;
        UnityEngine.Random.State previousRandom;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        GameObject host;
        RangeCombatWorld world;
        ArcadeMissionDirector director;

        [UnitySetUp] public IEnumerator SetUp()
        {
            previousDatabase = BlockModules.ConfiguredDatabase; previousScale = Time.timeScale;
            previousSuspended = GameInput.Suspended; previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState; previousRandom = UnityEngine.Random.state;
            previousScene = SceneManager.GetActiveScene();
            fixtureScene = SceneManager.CreateScene("ArcadeLifecycle_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(fixtureScene);
            var oldScenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != fixtureScene && scene.path.StartsWith("Assets/_Starframe/Scenes/", StringComparison.OrdinalIgnoreCase))
                    oldScenes.Add(scene);
            }
            foreach (var scene in oldScenes) if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            Time.timeScale = 1; GameInput.Suspended = false; yield return null;
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) UnityEngine.Object.Destroy(owned[i]);
            owned.Clear(); yield return null;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            else SceneManager.SetActiveScene(SceneManager.CreateScene("ArcadeLifecycleCleanup_" + Guid.NewGuid().ToString("N")));
            if (fixtureScene.IsValid() && fixtureScene.isLoaded) yield return SceneManager.UnloadSceneAsync(fixtureScene);
            BlockModules.Configure(previousDatabase); Time.timeScale = previousScale;
            GameInput.Suspended = previousSuspended; Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLock; UnityEngine.Random.state = previousRandom;
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator TwoRealWavesPreserveWrecksAndCountEachFiniteEnemyExactlyOnce()
        {
            CreateFixture();
            var first = default(RangeCombatWorld.Actor);
            yield return WaitForEnemy(a => { first = a; });
            Assert.That(director.WaveIndex, Is.EqualTo(1));
            int deaths = 0; world.ActorDied += a => { if (a != world.Player) deaths++; };
            int coreLife = first.ship.LifeId;
            string coreId = Core(first.ship).record.id;
            float amount = first.ship.ApplyDamage(coreId, float.MaxValue, world.Player.ship);
            Assert.That(amount, Is.GreaterThan(0));
            Assert.That(first.ship.IsAlive, Is.False);
            Assert.That(first.ship.ApplyDamage(coreId, float.MaxValue, world.Player.ship), Is.Zero);
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(director.Summary().enemiesDestroyed, Is.EqualTo(1));
            int firstScore = director.Score;
            int wreckParts = world.Debris.PartCount;
            Assert.That(wreckParts, Is.GreaterThan(0), "A core kill must transfer intact living blocks into physical debris.");
            Assert.That(world.Debris.Clusters.Count, Is.GreaterThan(0));
            foreach (var wreck in world.Debris.Clusters)
            {
                Assert.That(wreck.Body, Is.Not.Null);
                Assert.That(wreck.GetComponentsInChildren<Collider>().Length, Is.GreaterThan(0));
            }
            // Much longer than the fixture's .01-second practice respawn delay: mission actors must stay dead.
            world.Step(.02f);
            Assert.That(first.ship.IsAlive, Is.False); Assert.That(first.ship.LifeId, Is.EqualTo(coreLife));
            world.Step(.4f); yield return null;
            Assert.That(world.BotKills, Is.EqualTo(1)); Assert.That(director.Score, Is.EqualTo(firstScore));
            Assert.That(world.Debris.PartCount, Is.EqualTo(wreckParts), "Retiring an empty enemy shell must not retire its wreckage.");
            var second = default(RangeCombatWorld.Actor);
            yield return WaitForEnemy(a => { second = a; });
            Assert.That(second, Is.Not.SameAs(first)); Assert.That(director.WaveIndex, Is.EqualTo(2));
            Assert.That(second.ship.IsAlive, Is.True);
            Assert.That(second.ship.ApplyDamage(Core(second.ship).record.id, float.MaxValue, world.Player.ship), Is.GreaterThan(0));
            yield return null; yield return null;
            Assert.That(director.State, Is.EqualTo(ArcadeMissionState.Victory));
            Assert.That(director.Summary().wavesCompleted, Is.EqualTo(2));
            Assert.That(director.Summary().enemiesDestroyed, Is.EqualTo(2));
            Assert.That(world.BotKills, Is.EqualTo(2)); Assert.That(deaths, Is.EqualTo(2));
            Assert.That(world.CombatEnabled, Is.False);
            Assert.That(world.Debris.PartCount, Is.GreaterThan(wreckParts));
        }

        [UnityTest] public IEnumerator ContinuousStagesOverlapWithoutResettingSurvivingContactsOrLockingPlayerControl()
        {
            CreateFixture();var config=director.Definition;
            config.continuousStages=true;config.stageMinimumSeconds=.1f;config.stageTargetSeconds=.3f;
            config.maximumAlive=2;config.stageOverlapEnemies=1;
            RangeCombatWorld.Actor first=null;yield return WaitForEnemy(a=>first=a);
            Assert.That(world.Player.flight.MenuCursorMode,Is.False);
            float until=Time.realtimeSinceStartup+8;
            while(director.WaveIndex<2&&Time.realtimeSinceStartup<until)yield return null;
            Assert.That(director.WaveIndex,Is.EqualTo(2));
            Assert.That(director.AliveEnemies,Is.EqualTo(2),"Entering a stage must retain living contacts from the previous stage.");
            Assert.That(first.ship.IsAlive,Is.True);Assert.That(director.State,Is.EqualTo(ArcadeMissionState.Combat));
            Assert.That(world.Player.flight.MenuCursorMode,Is.False);
            Assert.That(director.Summary().wavesCompleted,Is.Zero,"A new stage is not a completed stage while its predecessor survives.");
            var live=new List<RangeCombatWorld.Actor>(world.Bots);
            foreach(var actor in live)if(actor.ship.IsAlive)actor.ship.ApplyDamage(Core(actor.ship).record.id,float.MaxValue,world.Player.ship);
            yield return null;yield return null;
            Assert.That(director.State,Is.EqualTo(ArcadeMissionState.Victory));
            Assert.That(director.Summary().enemiesDestroyed,Is.EqualTo(2));
            Assert.That(director.Summary().wavesCompleted,Is.EqualTo(2));
        }

        [UnityTest] public IEnumerator PlayerCoreLossEndsTheMissionAndFreshEntryStartsWithCleanProgress()
        {
            CreateFixture(); yield return WaitForEnemy(_ => { });
            int oldLife = world.Player.ship.LifeId;
            world.Player.ship.InvulnerableUntil = 0;
            world.Player.ship.ApplyDamage(Core(world.Player.ship).record.id, float.MaxValue, world.Bot.ship);
            Assert.That(director.State, Is.EqualTo(ArcadeMissionState.Defeat));
            Assert.That(world.CombatEnabled, Is.False);
            Assert.That(world.Player.ship.IsAlive, Is.False);
            Assert.That(world.Debris.PartCount, Is.GreaterThan(0));
            // Even explicitly re-enabling stepping cannot apply the practice respawn branch in mission mode.
            world.CombatEnabled = true; world.Step(2);
            Assert.That(world.Player.ship.IsAlive, Is.False); Assert.That(world.Player.ship.LifeId, Is.EqualTo(oldLife));
            world.CombatEnabled = false;
            UnityEngine.Object.Destroy(host); yield return null;
            CreateFixture();
            Assert.That(director.State, Is.EqualTo(ArcadeMissionState.Intro));
            Assert.That(director.WaveIndex, Is.Zero); Assert.That(director.Score, Is.Zero);
            Assert.That(world.Player.ship.IsAlive, Is.True); Assert.That(world.BotKills, Is.Zero);
            Assert.That(world.Debris.PartCount, Is.Zero);
            yield return WaitForEnemy(_ => { });
            Assert.That(director.State, Is.EqualTo(ArcadeMissionState.Combat));
            Assert.That(director.WaveIndex, Is.EqualTo(1)); Assert.That(director.AliveEnemies, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator IntermissionKeepsTheClearedWaveAndFreezesComboExpiry()
        {
            CreateFixture(); director.Definition.intermissionSeconds=60;
            RangeCombatWorld.Actor enemy=null; yield return WaitForEnemy(a=>enemy=a);
            enemy.ship.ApplyDamage(Core(enemy.ship).record.id,float.MaxValue,world.Player.ship);
            yield return null;yield return null;
            Assert.That(director.State,Is.EqualTo(ArcadeMissionState.Intermission));
            Assert.That(director.Summary().wavesCompleted,Is.EqualTo(1));
            Assert.That(world.Player.flight.MenuCursorMode,Is.True,"The actual next-wave button requires a released cursor.");
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var deadline=typeof(ArcadeMissionDirector).GetField("comboUntil",flags);
            deadline.SetValue(director,director.ElapsedSeconds+4f);
            float before=(float)deadline.GetValue(director)-director.ElapsedSeconds;
            yield return new WaitForSeconds(.2f);
            Assert.That((float)deadline.GetValue(director)-director.ElapsedSeconds,Is.EqualTo(before).Within(.03f));
            typeof(ArcadeMissionDirector).GetMethod("Finish",flags).Invoke(director,new object[]{ArcadeMissionState.Aborted});
            Assert.That(director.Summary().wavesCompleted,Is.EqualTo(1),"Aborting after a cleared wave must retain that wave in the report.");
        }

        [UnityTest] public IEnumerator EmptyFieldAnnouncesAndAdvancesTheNextFormationWithoutClearingTheWave()
        {
            CreateFixture();var config=director.Definition;var enemy=config.waves[0].batches[0].enemy;
            config.entryGraceSeconds=2;
            config.waves=new[]{new ArcadeWave{spawnInterval=.1f,batches=new[]{
                new ArcadeEnemyBatch{enemy=enemy,count=1},new ArcadeEnemyBatch{enemy=enemy,count=1,delaySeconds=60}
            }}};
            RangeCombatWorld.Actor first=null;yield return WaitForEnemy(a=>first=a);
            first.ship.ApplyDamage(Core(first.ship).record.id,float.MaxValue,world.Player.ship);
            float clearedAt=director.ElapsedSeconds;yield return null;yield return null;
            Assert.That(director.State,Is.EqualTo(ArcadeMissionState.Combat));Assert.That(director.WaveIndex,Is.EqualTo(1));
            Assert.That(director.AliveEnemies,Is.Zero);Assert.That(director.PendingEnemies,Is.EqualTo(1));
            Assert.That(director.ReinforcementsIncoming,Is.True);Assert.That(director.ReinforcementRemaining,Is.InRange(2.7f,3f));
            Assert.That(director.Summary().wavesCompleted,Is.Zero);Assert.That(world.Player.flight.MenuCursorMode,Is.False);
            yield return new WaitForSeconds(1);
            Assert.That(director.AliveEnemies,Is.Zero,"An advanced formation must retain its short warning.");
            RangeCombatWorld.Actor second=null;yield return WaitForEnemy(a=>second=a);
            Assert.That(director.ElapsedSeconds-clearedAt,Is.InRange(2.9f,5f),"Do not force the original 60-second release gate on a cleared field.");
            Assert.That(director.ReinforcementsIncoming,Is.False);Assert.That(director.PendingEnemies,Is.Zero);
            Assert.That(second.attackReadyAt-world.CombatTime,Is.EqualTo(2).Within(.01f),"Advancing a formation must retain its entry grace.");
            Assert.That(second,Is.Not.SameAs(first));Assert.That(director.WaveIndex,Is.EqualTo(1));
            second.ship.ApplyDamage(Core(second.ship).record.id,float.MaxValue,world.Player.ship);
            yield return null;yield return null;
            Assert.That(director.State,Is.EqualTo(ArcadeMissionState.Victory));Assert.That(director.Summary().enemiesDestroyed,Is.EqualTo(2));
        }

        [UnityTest] public IEnumerator ALivingContactRetainsTheLaterFormationReleaseTime()
        {
            CreateFixture();var config=director.Definition;var enemy=config.waves[0].batches[0].enemy;
            config.maximumAlive=2;
            config.waves=new[]{new ArcadeWave{spawnInterval=.1f,batches=new[]{
                new ArcadeEnemyBatch{enemy=enemy,count=1},new ArcadeEnemyBatch{enemy=enemy,count=1,delaySeconds=60}
            }}};
            yield return WaitForEnemy(_=>{});yield return new WaitForSeconds(3.2f);
            Assert.That(director.AliveEnemies,Is.EqualTo(1));Assert.That(director.PendingEnemies,Is.EqualTo(1));
            Assert.That(director.ReinforcementsIncoming,Is.False,"Only a completely cleared field may pull a later formation forward.");
            Assert.That(director.State,Is.EqualTo(ArcadeMissionState.Combat));Assert.That(director.Summary().wavesCompleted,Is.Zero);
        }

        IEnumerator WaitForEnemy(Action<RangeCombatWorld.Actor> receive)
        {
            float until = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < until)
            {
                foreach (var actor in world.Bots) if (actor.ship && actor.ship.IsAlive)
                {
                    actor.flight.enabled = false; receive(actor); yield break;
                }
                yield return null;
            }
            Assert.Fail("A real native enemy did not arrive from the wave queue. State=" + director.State
                + ", wave=" + director.WaveIndex + ", pending=" + director.PendingEnemies);
        }

        void CreateFixture()
        {
            var tuning = Own(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset")));
            var rules = Own(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset")));
            var prefab = AssetDatabase.LoadAssetAtPath<ShipAssembly>("Assets/_Starframe/Prefabs/Ships/FlightShipAssembly.prefab");
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>("Assets/_Starframe/Data/Enemies/harrier.asset");
            Assert.That(tuning, Is.Not.Null); Assert.That(prefab, Is.Not.Null); Assert.That(enemy, Is.Not.Null);
            tuning.arenaHalfExtent = 2000; rules.playerRespawnSeconds = rules.botRespawnSeconds = .01f;
            host = Own(new GameObject("Native finite arcade fixture"));
            if (!UnityEngine.Object.FindFirstObjectByType<AudioListener>()) host.AddComponent<AudioListener>();
            var cameraRoot = new GameObject("FixtureCamera"); cameraRoot.transform.SetParent(host.transform);
            var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(0, 5, -20);
            var playerRoot = new GameObject("FixturePlayer"); playerRoot.transform.SetParent(host.transform);
            var body = playerRoot.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var assembly = UnityEngine.Object.Instantiate(prefab, playerRoot.transform);
            var blueprint = FlightRangeBootstrap.DefaultShip(); assembly.Configure(tuning.database); assembly.Import(blueprint);
            var runtime = playerRoot.AddComponent<FlightShipRuntime>(); runtime.Initialize(assembly, blueprint, tuning, rules);
            var inverse = Quaternion.Inverse(runtime.CoreRotation);
            assembly.transform.localRotation = inverse; assembly.transform.localPosition = -(inverse * runtime.CenterOfMass());
            var flight = playerRoot.AddComponent<ShipFlightController>(); flight.Initialize(body, runtime, tuning, camera, runtime.HullRadius);
            flight.ExternalControl = true; flight.enabled = false;
            world = host.AddComponent<RangeCombatWorld>(); world.Initialize(runtime, flight, prefab, tuning, rules, camera, null, mission: true);
            world.enabled = false; runtime.InvulnerableUntil = 100000;
            var config = Own(ScriptableObject.CreateInstance<ArcadeMissionDefinition>());
            config.continuousStages=false; // Explicitly exercise the preserved legacy intermission path.
            config.introSeconds = config.intermissionSeconds = 0; config.entryGraceSeconds = 1000;
            config.resultDelaySeconds = 10000; config.maximumAlive = 1; config.maximumAttackers = 1;
            config.spawnDistance = 700; config.spawnDistanceVariation = 1;
            config.waves = new[]
            {
                new ArcadeWave { nameZh="一",nameEn="One",spawnInterval=.1f,batches=new[]{ new ArcadeEnemyBatch {enemy=enemy,count=1,delaySeconds=0} } },
                new ArcadeWave { nameZh="二",nameEn="Two",spawnInterval=.1f,batches=new[]{ new ArcadeEnemyBatch {enemy=enemy,count=1,delaySeconds=0} } }
            };
            director = host.AddComponent<ArcadeMissionDirector>(); director.Initialize(world, config, null);
            world.SetBotMode(RangeBotMode.Stationary, false); Physics.SyncTransforms();
        }

        static ShipPartState Core(FlightShipRuntime runtime)
            => runtime.Parts.Find(p => p.record.shape == BlockShape.Core);
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
#endif
    }
}
