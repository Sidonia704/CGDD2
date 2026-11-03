// Outside-Assets probe. Execute NativeFinalBalanceRound1Probe.Start() in an owned Play session.
// The 30-second observation uses shipped combat settings. Forced kills below prove lifecycle only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

public static class NativeFinalBalanceRound1Probe
{
    const string Evidence = "Documentation/FinalBalanceRound1-2026-11-03";
    const string Report = Evidence + "/native-mission.txt";
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] Ids = { "needle", "viper", "wasp", "bastion", "harrier", "halberd", "citadel" };
    static readonly int[] ApprovedParts = { 111, 172, 75, 301, 293, 344, 419 };
    static readonly int[] ApprovedTotals = { 19, 8, 13, 5, 4, 3, 1 };
    static Task running;
    static string status = "Not started";
    public static string Status() => status + "; task=" + (running == null ? "none" : running.Status.ToString()) + "; report=" + Report;
    static void Log(string value) { File.AppendAllText(Report, value + Environment.NewLine); status = value; }

    public static string Start(float observationSeconds = 30)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("An owned Play session is required.");
        if (running != null && !running.IsCompleted) throw new InvalidOperationException("The native probe is already running.");
        if (float.IsNaN(observationSeconds) || float.IsInfinity(observationSeconds) || observationSeconds < 30)
            throw new ArgumentOutOfRangeException(nameof(observationSeconds), "Observe at least 30 real seconds.");
        Directory.CreateDirectory(Evidence); File.WriteAllText(Report, "");
        Log("START " + DateTime.UtcNow.ToString("O") + "; one isolated t4 reference, 53 enemies = 40 small / 12 large / 1 capital.");
        running = Run(observationSeconds);
        return "Started native first-round lifecycle probe; poll NativeFinalBalanceRound1Probe.Status() or " + Report;
    }

    static async Task Until(Func<bool> predicate, string label, int seconds = 180)
    {
        double end = Time.realtimeSinceStartupAsDouble + seconds;
        while (!predicate())
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Play ended during " + label);
            if (Time.realtimeSinceStartupAsDouble > end) throw new TimeoutException(label);
            await Task.Yield();
        }
    }
    static async Task Frame()
    {
        int frame = Time.frameCount; await Until(() => Time.frameCount > frame, "next advancing frame", 30);
    }
    static object Get(object target, string field) => target.GetType().GetField(field, InstanceFields).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, InstanceFields).SetValue(target, value);
    static object GetStatic(Type type, string field) => type.GetField(field, StaticFields).GetValue(null);
    static void SetStatic(Type type, string field, object value) => type.GetField(field, StaticFields).SetValue(null, value);
    static string Core(FlightShipRuntime ship) => ship.Parts.First(p => p.record.shape == BlockShape.Core).record.id;
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static Dictionary<string, byte[]> ProtectedPlayerFiles()
    {
        string workspace = ShipWorkspace.WorkspacePath;
        return Enumerable.Range(1, 7).Select(i => Path.Combine(ShipWorkspace.BlueprintDirectory, "t" + i + ".json"))
            .Concat(new[] { workspace, workspace + ".bak" })
            .ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
    }
    static void AssertPlayerFilesUnchanged(Dictionary<string, byte[]> originals)
    {
        foreach (var pair in originals)
            Assert(pair.Value == null ? !File.Exists(pair.Key) : File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value), "Protected player file changed: " + pair.Key);
    }

    static void ReleaseCurrentQueue(ArcadeMissionDirector director)
    {
        // The director already materialized this wave's queue before its definition was cloned.
        // Change that owned runtime queue too; otherwise its old authored release gates remain active.
        var queue = (IList)Get(director, "queue");
        for (int i = 0; i < queue.Count; i++)
        {
            object item = queue[i]; item.GetType().GetField("at", InstanceFields).SetValue(item, 0f); queue[i] = item;
        }
        Set(director, "nextSpawn", 0f); Set(director, "reinforcementAt", -1f);
    }

    static async Task Run(float observationSeconds)
    {
        var navigation = typeof(AppNavigation).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => !field.IsLiteral && !field.IsInitOnly).ToDictionary(field => field, field => field.GetValue(null));
        var flightState = typeof(FlightSession).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.GetSetMethod(true) != null).ToDictionary(property => property, property => property.GetValue(null));
        object oldRequest = GetStatic(typeof(MissionSession), "current"), oldResult = GetStatic(typeof(MissionResults), "last");
        bool oldSuspended = GameInput.Suspended, oldCursorVisible = Cursor.visible;
        CursorLockMode oldCursorLock = Cursor.lockState; float oldScale = Time.timeScale;
        var oldDatabase = BlockModules.ConfiguredDatabase; var oldRandom = UnityEngine.Random.state;
        string bestKey = "Starframe.Arcade.Best.ice.Standard";
        bool hadBest = PlayerPrefs.HasKey(bestKey); int oldBest = PlayerPrefs.GetInt(bestKey);
        var originals = ProtectedPlayerFiles();
        string isolatedRoot = Path.GetFullPath("Temp/FinalBalanceRound1Probe/" + Guid.NewGuid().ToString("N"));
        var storage = ShipWorkspace.UseStorageRoot(isolatedRoot);
        var owned = new List<UnityEngine.Object>();
        var defeated = new HashSet<int>(); var perType = Ids.ToDictionary(id => id, id => 0);
        string eventFailure = null; bool passed = false;
        try
        {
            Time.timeScale = 1; GameInput.Suspended = false;
            var template = AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>("Assets/_Starframe/Data/Enemies/bastion.asset");
            Assert(template && template.CreateBlueprint().parts.Count == 301, "The native t4 reference is missing or obsolete.");
            var playerReference = template.CreateBlueprint();
            ShipWorkspace.Set(playerReference, false);
            string expectedWorkspace = JsonUtility.ToJson(ShipWorkspace.Ship);
            string expectedBlueprint = JsonUtility.ToJson(BlueprintRepository.Upgrade(ShipWorkspace.Ship));
            AppNavigation.HasAppContext = true; AppNavigation.Difficulty = MissionDifficulty.Standard;
            AppNavigation.ReturnPage = AppPage.Home; AppNavigation.SelectedMissionId = "ice";
            AppNavigation.RangeReturnToEditor = AppNavigation.SettingsReturnToEditor = AppNavigation.WeaponGroupsReturnToEditor = AppNavigation.BlueprintsReturnToEditor = false;
            await SceneManager.LoadSceneAsync(AppNavigation.HomeScene);
            GameAppBootstrap app = null;
            await Until(() => { app = UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>(); return app && app.IsReady; }, "native hangar ready");
            app.SelectMission("ice"); Assert(app.ConfirmLaunch(), "The t4 reference was rejected by native 30000-cost deployment.");
            var request = MissionSession.Current;
            Assert(request != null && request.budget == 30000 && request.ship.parts.Count == 301, "Deployment did not capture the approved t4 ship and 30000 budget.");
            Assert(JsonUtility.ToJson(request.ship) == expectedBlueprint, "Deployment rewrote the approved t4 blueprint.");
            ArcadeMissionDirector director = null; RangeCombatWorld world = null;
            await Until(() => { director = UnityEngine.Object.FindFirstObjectByType<ArcadeMissionDirector>(); world = UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>(); return director && world && world.Player != null && !RangeLoadingOverlay.IsLoading; }, "native SaturnRing map and mission ready");
            Assert(world.IsMission && !GameInput.Suspended, "Native mission mode/input readiness failed.");
            var nativeDefinition = director.Definition; string nativeBefore = EditorJsonUtility.ToJson(nativeDefinition, true);
            Assert(nativeDefinition.TotalEnemies == 53 && nativeDefinition.maximumAlive == 10 && nativeDefinition.maximumLarge == 3 && nativeDefinition.maximumAttackers == 3, "The native mission is not the approved 53-enemy / 10-3-3 configuration.");
            var rules = (BattleTuning)Get(world, "rules");
            Assert(!rules.unlimitedRangePower, "The native observation must use finite shared power.");
            world.Player.ship.InvulnerableUntil = float.MaxValue;
            world.Player.flight.ExternalControl = true; world.Player.flight.SetExternalInput(Vector3.zero, Vector2.zero);
            int shots = 0, hits = 0, livePeak = 0; float lowestEnergy = float.MaxValue, greatestTravel = 0;
            var firstPositions = new Dictionary<int, Vector3>();
            world.Projectiles.ShotFired += (owner, position) => { if (owner != world.Player.ship) shots++; };
            world.Projectiles.HitConfirmed += owner => { if (owner != world.Player.ship) hits++; };
            world.ActorDied += actor =>
            {
                if (actor == world.Player) return;
                if (!defeated.Add(actor.spawnSlot)) eventFailure = "Duplicate defeat event for slot " + actor.spawnSlot;
                if (actor.archetype && perType.ContainsKey(actor.archetype.id)) perType[actor.archetype.id]++;
                else eventFailure = "Unexpected enemy archetype in native mission.";
            };
            Log("READY frame=" + Time.frameCount + ", player=t4/301 parts, budget=" + request.budget + ", cost=" + request.cost + ", finitePower=True, total=53.");
            double observe = Time.realtimeSinceStartupAsDouble; int firstFrame = Time.frameCount; float firstSimulationTime = director.ElapsedSeconds;
            while (Time.realtimeSinceStartupAsDouble - observe < observationSeconds)
            {
                Assert(director.State != ArcadeMissionState.Defeat, "The protected observation player died.");
                Assert(eventFailure == null, eventFailure);
                foreach (var actor in world.Bots.Where(actor => actor.ship && actor.ship.IsAlive))
                {
                    if (!firstPositions.ContainsKey(actor.spawnSlot)) firstPositions[actor.spawnSlot] = actor.body.position;
                    greatestTravel = Mathf.Max(greatestTravel, Vector3.Distance(firstPositions[actor.spawnSlot], actor.body.position));
                    lowestEnergy = Mathf.Min(lowestEnergy, actor.ship.Power.Energy);
                }
                livePeak = Mathf.Max(livePeak, director.AliveEnemies); await Frame();
            }
            Assert(Time.frameCount > firstFrame + 10, "The native game did not advance through the real-time observation.");
            Assert(shots > 0, "The actual AI never fired during the 30-second observation.");
            Log("LIVE realSeconds=" + (Time.realtimeSinceStartupAsDouble - observe).ToString("F2") + ", simulationSeconds=" + (director.ElapsedSeconds - firstSimulationTime).ToString("F2") + ", advancedFrames=" + (Time.frameCount - firstFrame) + ", wave=" + director.WaveIndex + ", peakAlive=" + livePeak + ", enemyShots=" + shots + ", confirmedDamageHits=" + hits + ", minObservedEnemyEnergy=" + lowestEnergy + ", greatestObservedTravelMetres=" + greatestTravel.ToString("F2") + ". Player invulnerable: hits are not an accuracy measurement.");

            GameAudioRuntime audio=null;
            await Until(()=>{audio=UnityEngine.Object.FindFirstObjectByType<GameAudioRuntime>();return audio&&ReferenceEquals(Get(audio,"world"),world);},"mission combat audio binding",10);
            Assert(ReferenceEquals(world.Projectiles.DamageTuning,rules.damageTuning)&&Mathf.Abs(world.Projectiles.EnemyDamageMultiplier-.5f)<.0001f,"Live damage tuning was not bound.");
            Log("PASS SaturnRing combat audio bound to actual world; damage0.6/0.25 with enemy0.5 is active.");
            var flareTarget=world.Bots.FirstOrDefault(actor=>actor.ship&&actor.ship.IsAlive&&actor.countermeasures.ReadyCount>0);
            Assert(flareTarget!=null,"No surviving native countermeasure launcher.");
            int priorBurst=world.Flares.Bursts.Count>0?world.Flares.Bursts.Max(b=>b.Id):0;
            flareTarget.ship.InvulnerableUntil=float.MaxValue;
            var incoming=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Missile1Weapon.asset"));owned.Add(incoming);
            // Controlled incoming fast launch; actual native AI, authored clear mounts and cooldowns are exercised.
            incoming.missileLaunchSpeed=incoming.projectileSpeed;
            double flareEnd=Time.realtimeSinceStartupAsDouble+12,nextLaunch=0;bool deployed=false;
            while(Time.realtimeSinceStartupAsDouble<flareEnd&&!deployed){
                if(Time.realtimeSinceStartupAsDouble>=nextLaunch){
                    var core=flareTarget.ship.CorePhysicsPosition;var origin=core-flareTarget.body.rotation*Vector3.forward*300;
                    Vector3 aim=core;
                    InterceptSolver.TrySolve(origin,core,flareTarget.flight.Velocity,incoming.projectileSpeed,incoming.range,out aim,out _);
                    world.Projectiles.Fire(world.Player.ship,incoming,origin,(aim-origin).normalized,null,default,flareTarget.ship);
                    nextLaunch=Time.realtimeSinceStartupAsDouble+2;
                }
                deployed=world.Flares.Bursts.Any(b=>b.Id>priorBurst&&b.Owner==flareTarget.ship);await Frame();
            }
            Assert(deployed,"Real native enemy did not deploy any flare against repeated incoming missiles.");
            flareTarget.ship.InvulnerableUntil=0;
            Log("PASS native authored enemy automatically emitted actual flare burst against controlled incoming missiles; physical launchers/cooldowns remain authoritative.");

            var hud = UnityEngine.Object.FindFirstObjectByType<RangeHUD>(); Assert(hud, "Native HUD missing.");
            hud.OpenExit(); await Until(() => Time.timeScale == 0, "mission exit-menu pause", 5);
            float paused = director.ElapsedSeconds; await Task.Delay(300); await Frame();
            Assert(Mathf.Abs(paused - director.ElapsedSeconds) <= .01f, "Mission time advanced behind the exit menu.");
            hud.CloseExit(); await Until(() => Time.timeScale > 0, "exit-menu resume", 5); await Frame();
            Log("PASS native pause/resume; simulation clock frozen behind the actual exit menu.");

            var config = UnityEngine.Object.Instantiate(nativeDefinition); owned.Add(config);
            config.intermissionSeconds = .1f; config.resultDelaySeconds = 10000;
            foreach (var wave in config.waves) { wave.spawnInterval = .2f; foreach (var batch in wave.batches) batch.delaySeconds = 0; }
            Set(director, "definition", config); ReleaseCurrentQueue(director);
            Assert(EditorJsonUtility.ToJson(nativeDefinition, true) == nativeBefore, "Lifecycle acceleration mutated the native mission asset.");
            Log("LIFECYCLE accelerated scheduling only: runtime clone, actual 10/3/3 caps, actual entry grace, real native ships/damage/debris; forced core kills below are not combat-duration evidence.");
            double previous = Time.realtimeSinceStartupAsDouble, nextKill = previous + 1, fillUntil = previous + 5, end = previous + 300;
            int waveIndex = director.WaveIndex, peakAlive = director.AliveEnemies, peakLarge = 0, peakCapital = 0;
            var intervals = new List<double>(); var seen = new HashSet<int>();
            while (director.State != ArcadeMissionState.Victory)
            {
                Assert(Time.realtimeSinceStartupAsDouble <= end, "Lifecycle timeout: wave=" + director.WaveIndex + ", pending=" + director.PendingEnemies + ", alive=" + director.AliveEnemies);
                Assert(eventFailure == null, eventFailure); await Frame();
                double now = Time.realtimeSinceStartupAsDouble; intervals.Add((now - previous) * 1000); previous = now;
                int large = 0, capital = 0;
                foreach (var actor in world.Bots.Where(actor => actor.ship && actor.ship.IsAlive))
                {
                    if (actor.archetype.size == EnemySize.Large) large++;
                    if (actor.archetype.size == EnemySize.Capital) capital++;
                    if (seen.Add(actor.spawnSlot))
                    {
                        int index = Array.IndexOf(Ids, actor.archetype.id);
                        Assert(index >= 0 && actor.ship.Parts.Count == ApprovedParts[index], "A native spawned ship does not match its approved final blueprint: " + actor.archetype.id);
                        if (index == 6) Assert(actor.archetype.alternateBlueprint == actor.archetype.blueprint, "Wave 5 used the obsolete capital alternate.");
                    }
                }
                peakAlive = Mathf.Max(peakAlive, director.AliveEnemies); peakLarge = Mathf.Max(peakLarge, large); peakCapital = Mathf.Max(peakCapital, capital);
                Assert(director.AliveEnemies <= 10 && large <= 3 && capital <= 1, "Native actor concurrency exceeded its configured limits.");
                if (director.WaveIndex != waveIndex)
                {
                    waveIndex = director.WaveIndex; fillUntil = now + 5;
                    Log("WAVE " + waveIndex + ": defeated=" + world.BotKills + ", wreckParts=" + world.Debris.PartCount + ", clusters=" + world.Debris.Clusters.Count);
                }
                if (director.PendingEnemies > 0 && director.AliveEnemies < config.maximumAlive && now < fillUntil) continue;
                if (now < nextKill) continue;
                var target = world.Bots.FirstOrDefault(actor => actor.ship && actor.ship.IsAlive);
                if (target == null) continue;
                target.ship.InvulnerableUntil = 0;
                Assert(target.ship.ApplyDamage(Core(target.ship), float.MaxValue, world.Player.ship) > 0, "A finite native enemy rejected the lifecycle core hit.");
                nextKill = now + .35;
            }
            var summary = director.Summary();
            Assert(summary.enemiesDestroyed == 53 && summary.smallDestroyed == 40 && summary.largeDestroyed == 12 && summary.capitalDestroyed == 1 && summary.wavesCompleted == 5, "Incorrect final lifecycle counts: " + JsonUtility.ToJson(summary));
            Assert(world.BotKills == 53 && defeated.Count == 53, "Finite enemies were duplicated, skipped or respawned.");
            for (int i = 0; i < Ids.Length; i++) Assert(perType[Ids[i]] == ApprovedTotals[i], "Incorrect defeated archetype count: " + Ids[i] + "=" + perType[Ids[i]]);
            intervals.Sort();
            Log("VICTORY lifecycle=53/40/12/1, waves=5, perType=" + string.Join(",", Ids.Select(id => id + ":" + perType[id])) + ", peakAlive/large/capital=" + peakAlive + "/" + peakLarge + "/" + peakCapital + ", score=" + summary.score + ", wreckParts=" + world.Debris.PartCount + ".");
            Log("DIAGNOSTIC forced-kill Editor frame median/p95/max ms=" + intervals[intervals.Count / 2].ToString("F2") + "/" + intervals[Mathf.Min(intervals.Count - 1, (int)(intervals.Count * .95))].ToString("F2") + "/" + intervals.Last().ToString("F2") + "; enemyCreationMaxMs=" + world.MaximumBotCreationMilliseconds + "; wreckSpawnMaxMs=" + world.Debris.MaximumSpawnMilliseconds + ". Not a Player benchmark or balance-duration test.");
            Assert(world.Debris.PartCount > 1000, "Surviving native wreckage unexpectedly disappeared.");
            double settle = Time.realtimeSinceStartupAsDouble + 3; while (Time.realtimeSinceStartupAsDouble < settle) await Frame();
            var retired = world.Debris.Clusters.Where(cluster => cluster && !cluster.SourceShip).ToArray();
            Assert(retired.Length > 0, "No retired-source native wreckage remained for validation.");
            foreach (var cluster in retired) foreach (var part in cluster.Parts)
                if (part && part.Part.present)
                {
                    var collider = part.GetComponent<MeshCollider>();
                    Assert(collider && collider.sharedMesh, "Retiring an enemy released a live wreck collider mesh.");
                }
            var hitPart = retired.SelectMany(cluster => cluster.Parts).Where(part => part && part.Part.present).OrderByDescending(part => part.GetComponent<Collider>().bounds.max.y).First();
            var shot = ScriptableObject.CreateInstance<WeaponDefinition>(); owned.Add(shot);
            shot.damage = 1000000; shot.projectileSpeed = 500; shot.projectileDiameter = .1f; shot.penetrationDepth = 2; shot.range = 10;
            var bounds = hitPart.GetComponent<Collider>().bounds; int wreckBefore = world.Debris.PartCount;
            Physics.SyncTransforms(); world.Projectiles.Fire(world.Player.ship, shot, new Vector3(bounds.center.x, bounds.max.y + 2, bounds.center.z), Vector3.down);
            world.Projectiles.Step(.03f); world.Debris.Step(.02f);
            Assert(world.Debris.PartCount < wreckBefore, "The normal projectile path could not damage retired-source wreckage.");
            Log("PASS retired-source collider ownership and real swept-projectile wreck destruction.");
            Set(director, "finishAt", Time.unscaledTime);
            await Until(() => MissionResults.Last != null && MissionResults.Last.outcome == MissionOutcome.Victory, "victory result recorded");
            await Until(() => { app = UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>(); return app && app.IsReady; }, "native results page ready");
            Assert(JsonUtility.ToJson(MissionResults.Last.request.ship) == expectedBlueprint && JsonUtility.ToJson(ShipWorkspace.Ship) == expectedWorkspace, "The result path mutated the confirmed or isolated working blueprint.");
            Log("PASS native result page and unchanged confirmed t4 snapshot.");

            app.SelectMission("ice"); Assert(app.ConfirmLaunch(), "Native 30000-budget reentry rejected the unchanged t4 reference.");
            await Until(() => { director = UnityEngine.Object.FindFirstObjectByType<ArcadeMissionDirector>(); world = UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>(); return director && world && world.Player != null && !RangeLoadingOverlay.IsLoading; }, "native mission reentry ready");
            Assert(director.State == ArcadeMissionState.Intro && director.WaveIndex == 0 && director.Score == 0 && world.BotKills == 0 && world.Debris.PartCount == 0, "Reentry retained old mission progress or wreckage.");
            var failConfig = UnityEngine.Object.Instantiate(director.Definition); owned.Add(failConfig); failConfig.resultDelaySeconds = 10000; Set(director, "definition", failConfig);
            int playerLife = world.Player.ship.LifeId; world.Player.ship.InvulnerableUntil = 0;
            world.Player.ship.ApplyDamage(Core(world.Player.ship), float.MaxValue);
            Assert(director.State == ArcadeMissionState.Defeat && !world.Player.ship.IsAlive, "Player core loss did not produce native defeat.");
            // Exercise the real world step beyond the practice respawn duration while holding the result locally.
            rules = (BattleTuning)Get(world, "rules"); world.CombatEnabled = true; world.Step(Mathf.Max(1, rules.playerRespawnSeconds + .5f)); world.CombatEnabled = false;
            Assert(!world.Player.ship.IsAlive && world.Player.ship.LifeId == playerLife, "A mission core-loss defeat incorrectly used practice respawn.");
            Set(director, "finishAt", Time.unscaledTime);
            await Until(() => MissionResults.Last != null && MissionResults.Last.outcome == MissionOutcome.Defeat, "core-loss defeat result recorded");
            await Until(() => { app = UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>(); return app && app.IsReady; }, "defeat result page ready");
            Assert(EditorJsonUtility.ToJson(nativeDefinition, true) == nativeBefore, "The probe mutated the native mission definition.");
            AssertPlayerFilesUnchanged(originals); passed = true;
            Log("PASS native reentry starts clean; core loss defeats without practice respawn; all original player files remain byte-identical.");
            Log("LIMITS: one invulnerable t4 observes AI only. The three-t4 player baseline and fifteen-minute human challenge duration were not measured; forced kills prove finite-wave lifecycle only.");
        }
        catch (Exception exception) { Log("FAIL " + exception); }
        finally
        {
            // Keep cleanup and storage restoration independent, so a scene-unload failure cannot leak the isolated root.
            try
            {
                Time.timeScale = 1;
                var cleanup = SceneManager.CreateScene("FinalBalanceRound1ProbeCleanup_" + Guid.NewGuid().ToString("N")); SceneManager.SetActiveScene(cleanup);
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene != cleanup && scene.IsValid() && scene.isLoaded && !scene.name.StartsWith("DontDestroy", StringComparison.Ordinal)) await SceneManager.UnloadSceneAsync(scene);
                }
                await Frame();
            }
            catch (Exception exception) { passed = false; Log("FAIL scene cleanup: " + exception); }
            finally
            {
                foreach (var value in owned) if (value) UnityEngine.Object.Destroy(value);
                storage.Dispose();
                SetStatic(typeof(MissionSession), "current", oldRequest); SetStatic(typeof(MissionResults), "last", oldResult);
                foreach (var pair in navigation) pair.Key.SetValue(null, pair.Value);
                foreach (var pair in flightState) pair.Key.GetSetMethod(true).Invoke(null, new[] { pair.Value });
                if (hadBest) PlayerPrefs.SetInt(bestKey, oldBest); else PlayerPrefs.DeleteKey(bestKey); PlayerPrefs.Save();
                BlockModules.Configure(oldDatabase); UnityEngine.Random.state = oldRandom;
                Time.timeScale = oldScale; GameInput.Suspended = oldSuspended; Cursor.lockState = oldCursorLock; Cursor.visible = oldCursorVisible;
                try { AssertPlayerFilesUnchanged(originals); } catch (Exception exception) { passed = false; Log("FAIL file preservation: " + exception); }
                Log("DONE " + (passed ? "PASS" : "FAIL") + "; original storage root/snapshot, navigation, flight/session/result state, best score, input/cursor/time state restored. Isolated files retained at " + isolatedRoot + ". Owned Play ends in a cleanup scene; Stop Play restores the Editor scene.");
            }
        }
    }
}
