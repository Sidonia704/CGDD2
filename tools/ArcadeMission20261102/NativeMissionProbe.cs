using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
public static class NativeMissionProbe
{
    static Task running;
    const string Report="Documentation/ArcadeMission-2026-11-02/native-mission.txt";
    static void Log(string value)=>File.AppendAllText(Report,value+Environment.NewLine);
    public static string Start(float observationSeconds=65)
    {
        if(!Application.isPlaying)throw new Exception("Owned Play required.");
        if(running!=null&&!running.IsCompleted)throw new Exception("Probe running.");
        File.WriteAllText(Report,"");running=Run(observationSeconds);return "Started native mission verification; poll "+Report;
    }
    static async Task Until(Func<bool> p,string label,int seconds=180)
    {
        double end=Time.realtimeSinceStartupAsDouble+seconds;while(!p()){if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Timeout: "+label);await Task.Yield();}
    }
    static async Task Frame(){int frame=Time.frameCount;await Until(()=>Time.frameCount>frame,"next frame",30);}
    static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);
    static async Task Run(float observationSeconds)
    {
        bool oldSuspended=GameInput.Suspended,oldContext=AppNavigation.HasAppContext;var oldPage=AppNavigation.ReturnPage;string oldSelected=AppNavigation.SelectedMissionId;
        var oldDifficulty=AppNavigation.Difficulty;var oldDraft=FlightSession.EditorDraft;bool oldDirty=FlightSession.EditorDirty,oldReturning=FlightSession.ReturningToEditor;
        var oldScene=SceneManager.GetActiveScene();
        var storage=ShipWorkspace.UseStorageRoot(Path.GetFullPath("Temp/ArcadeMissionProbe/"+Guid.NewGuid().ToString("N")));
        string bestKey="Starframe.Arcade.Best.ice.Standard";bool hadBest=PlayerPrefs.HasKey(bestKey);int oldBest=PlayerPrefs.GetInt(bestKey);
        ArcadeMissionDefinition config=null;bool passed=false;int shots=0,hits=0;
        try{
            var template=AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>("Assets/_Starframe/Data/Enemies/citadel.asset");
            ShipWorkspace.Set(template.CreateBlueprint(),false);
            AppNavigation.HasAppContext=true;AppNavigation.Difficulty=MissionDifficulty.Standard;
            await SceneManager.LoadSceneAsync(AppNavigation.HomeScene);
            GameAppBootstrap app=null;await Until(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady;},"hangar");
            app.SelectMission("ice");if(!app.ConfirmLaunch())throw new Exception("10000-cost deployment rejected.");
            ArcadeMissionDirector director=null;RangeCombatWorld world=null;
            await Until(()=>{director=UnityEngine.Object.FindFirstObjectByType<ArcadeMissionDirector>();world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();return director&&world&&world.Player!=null&&!RangeLoadingOverlay.IsLoading;},"native mission/map ready");
            if(!world.IsMission||world.Bots.Count>0)Log("Initial bots="+world.Bots.Count+", mission="+world.IsMission);
            if(GameInput.Suspended)throw new Exception("Flight input stayed suspended.");
            Log("READY frame="+Time.frameCount+", parts="+world.Player.ship.Parts.Count+", native total="+director.Definition.TotalEnemies);
            world.Player.ship.InvulnerableUntil=float.MaxValue;
            world.Projectiles.ShotFired+=(owner,position)=>{if(owner!=world.Player.ship)shots++;};
            world.Projectiles.HitConfirmed+=owner=>{if(owner!=world.Player.ship)hits++;};
            // Real-time AI observation uses the shipped timings, map, weapons, sensors and finite shared power.
            double observe=Time.realtimeSinceStartupAsDouble;
            int startFrame=Time.frameCount;
            while(Time.realtimeSinceStartupAsDouble-observe<observationSeconds){
                if(director.State==ArcadeMissionState.Defeat)throw new Exception("Invulnerable observation player died.");
                await Frame();
            }
            Log("LIVE "+observationSeconds+"s: advancedFrames="+(Time.frameCount-startFrame)+", wave="+director.WaveIndex+", alive="+director.AliveEnemies+", enemy shots="+shots+", hits="+hits);
            if(shots==0)throw new Exception("AI never fired during real-time observation.");
            var hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();
            hud.OpenExit();await Frame();float paused=director.ElapsedSeconds;await Task.Delay(250);await Frame();
            if(Mathf.Abs(paused-director.ElapsedSeconds)>.01f)throw new Exception("Mission did not pause behind exit menu.");
            hud.CloseExit();await Frame();if(Time.timeScale==0)throw new Exception("Pause did not release.");
            Log("PASS pause/resume; F target API smoke follows.");
            var target=world.Bots.FirstOrDefault(a=>a.ship.IsAlive);if(target!=null){
                var camera=hud.WorldCamera;var p=camera.WorldToScreenPoint(target.ship.CorePosition);
                if(p.z>0){world.Targeting.ToggleLockAt(p);world.Targeting.Clear();}
            }
            // Accelerate only reinforcement scheduling for lifecycle coverage. Core hits use real damage APIs.
            config=UnityEngine.Object.Instantiate(director.Definition);config.intermissionSeconds=.1f;config.resultDelaySeconds=10000;config.maximumAlive=12;
            foreach(var w in config.waves){w.spawnInterval=.2f;foreach(var b in w.batches)b.delaySeconds=0;}
            Set(director,"definition",config);
            var frameTimes=new List<double>();double previous=Time.realtimeSinceStartupAsDouble,nextKill=previous+1;
            double end=previous+300;int wave=director.WaveIndex,peakAlive=director.AliveEnemies;
            while(director.State!=ArcadeMissionState.Victory){
                if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Timed out completing all finite waves, wave="+director.WaveIndex+", pending="+director.PendingEnemies+", alive="+director.AliveEnemies);
                await Frame();peakAlive=Mathf.Max(peakAlive,director.AliveEnemies);double now=Time.realtimeSinceStartupAsDouble;frameTimes.Add((now-previous)*1000);previous=now;
                if(director.WaveIndex!=wave){wave=director.WaveIndex;Log("WAVE "+wave+": kills="+world.BotKills+", wreckParts="+world.Debris.PartCount+", clusters="+world.Debris.Clusters.Count);}
                if(now<nextKill)continue;
                var actor=world.Bots.FirstOrDefault(a=>a.ship.IsAlive);if(actor==null)continue;
                actor.ship.ApplyDamage(actor.ship.Parts.First(x=>x.record.shape==BlockShape.Core).record.id,float.MaxValue,world.Player.ship);
                nextKill=now+.8;
            }
            var summary=director.Summary();
            if(summary.enemiesDestroyed!=67||summary.smallDestroyed!=50||summary.largeDestroyed!=15||summary.capitalDestroyed!=2||summary.wavesCompleted!=5)throw new Exception("Incorrect final counts.");
            frameTimes.Sort();Log("VICTORY 67=50/15/2; waves=5; score="+summary.score+"; remaining wreckParts="+world.Debris.PartCount+"; frameMedianMs="+frameTimes[frameTimes.Count/2]+"; frameP95Ms="+frameTimes[(int)(frameTimes.Count*.95)]+"; maxMs="+frameTimes.Last());
            Log("Peak alive enemies="+peakAlive);
            Log("Operation maxima ms: enemyCreation="+world.MaximumBotCreationMilliseconds+", wreckSpawn="+world.Debris.MaximumSpawnMilliseconds);
            if(world.Debris.PartCount<1000)throw new Exception("Intact wreckage unexpectedly disappeared.");
            var settled=new List<double>();double hold=Time.realtimeSinceStartupAsDouble+5,lastFrame=Time.realtimeSinceStartupAsDouble;while(Time.realtimeSinceStartupAsDouble<hold){await Frame();double sample=Time.realtimeSinceStartupAsDouble;settled.Add((sample-lastFrame)*1000);lastFrame=sample;}
            settled.Sort();var instancer=world.Debris.GetComponent<DebrisInstanceRenderer>();Log("SETTLED wreckage frameMedianMs="+settled[settled.Count/2]+", p95Ms="+settled[(int)(settled.Count*.95)]+", visibleWreckInstances="+instancer.VisibleInstanceCount+", batches="+instancer.SubmittedBatchCount+", allocatedBytes="+UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
            var retiredWrecks=world.Debris.Clusters.Where(c=>c&&!c.SourceShip).ToArray();
            if(retiredWrecks.Length==0)throw new Exception("No retired-source wrecks available for collision validation.");
            foreach(var c in retiredWrecks)foreach(var part in c.Parts)if(part&&part.Part.present&&part.GetComponent<MeshCollider>().sharedMesh==null)throw new Exception("A retired ship released a surviving wreck collider mesh.");
            var hitPart=retiredWrecks.SelectMany(c=>c.Parts).Where(p=>p&&p.Part.present).OrderByDescending(p=>p.GetComponent<Collider>().bounds.max.y).First();
            var shot=ScriptableObject.CreateInstance<WeaponDefinition>();shot.damage=1000000;shot.projectileSpeed=500;shot.projectileDiameter=.1f;shot.penetrationDepth=2;shot.range=10;
            var hitBounds=hitPart.GetComponent<Collider>().bounds;int oldWreckCount=world.Debris.PartCount;
            world.Projectiles.Fire(world.Player.ship,shot,new Vector3(hitBounds.center.x,hitBounds.max.y+2,hitBounds.center.z),Vector3.down);
            Physics.SyncTransforms();world.Projectiles.Step(.03f);world.Debris.Step(.02f);
            if(world.Debris.PartCount>=oldWreckCount)throw new Exception("Normal projectile could not destroy retired-source wreckage.");
            UnityEngine.Object.Destroy(shot);Log("PASS retired-source meshes survive; normal swept projectile destroys a wreck block and split processing succeeds.");
            Set(director,"finishAt",Time.unscaledTime);
            await Until(()=>MissionResults.Last!=null,"result recorded");
            await Until(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady;},"results page");
            if(MissionResults.Last.outcome!=MissionOutcome.Victory)throw new Exception("Wrong result.");
            Log("PASS results and unchanged confirmed blueprint snapshot.");
            // Return through the public deployment path once more, then exercise core-loss failure.
            app.SelectMission("ice");if(!app.ConfirmLaunch())throw new Exception("Reentry deployment rejected.");
            await Until(()=>{director=UnityEngine.Object.FindFirstObjectByType<ArcadeMissionDirector>();world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();return director&&world&&world.Player!=null&&!RangeLoadingOverlay.IsLoading;},"mission reentry");
            world.Player.ship.InvulnerableUntil=0;
            world.Player.ship.ApplyDamage(world.Player.ship.Parts.First(x=>x.record.shape==BlockShape.Core).record.id,float.MaxValue);
            if(director.State!=ArcadeMissionState.Defeat)throw new Exception("Core loss did not cause defeat.");
            await Until(()=>MissionResults.Last!=null,"defeat recorded");
            if(MissionResults.Last.outcome!=MissionOutcome.Defeat)throw new Exception("Wrong failure result.");
            passed=true;Log("PASS reentry and core-loss failure, no practice respawn.");
        }catch(Exception e){Log("FAIL "+e);}
        finally{
            Time.timeScale=1;
            var cleanup=SceneManager.CreateScene("ArcadeProbeCleanup");SceneManager.SetActiveScene(cleanup);
            for(int i=SceneManager.sceneCount-1;i>=0;i--){var s=SceneManager.GetSceneAt(i);if(s!=cleanup&&s.IsValid()&&s.isLoaded&&!s.name.StartsWith("DontDestroy"))await SceneManager.UnloadSceneAsync(s);}
            if(config)UnityEngine.Object.Destroy(config);
            storage.Dispose();MissionSession.Clear();
            if(hadBest)PlayerPrefs.SetInt(bestKey,oldBest);else PlayerPrefs.DeleteKey(bestKey);PlayerPrefs.Save();
            GameInput.Suspended=oldSuspended;AppNavigation.HasAppContext=oldContext;AppNavigation.ReturnPage=oldPage;AppNavigation.SelectedMissionId=oldSelected;AppNavigation.Difficulty=oldDifficulty;
            typeof(FlightSession).GetProperty("EditorDraft").SetValue(null,oldDraft);typeof(FlightSession).GetProperty("EditorDirty").SetValue(null,oldDirty);typeof(FlightSession).GetProperty("ReturningToEditor").SetValue(null,oldReturning);
            Log("DONE "+(passed?"PASS":"FAIL")+"; storage and preferences restored.");
        }
    }
}
