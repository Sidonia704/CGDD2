// Optional read-only telemetry for an already running battle, after test execution finishes.
// Run WreckageProbe.Snapshot("wave5") or Start("wave5",240); no ships, input or physics settings are changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using Starframe.Combat;

public static class WreckageProbe
{
    const string Folder="Documentation/ArcadeMission-2026-11-02/Performance";
    static object Counts()
    {
        var world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
        if(!world||!world.Debris)throw new InvalidOperationException("A running combat world is required.");
        var debris=world.Debris;
        // DebrisWorld shares the combat root with ship actors; scan only its actual island roots.
        var islands=debris.Clusters.Where(c=>c).ToArray();
        var renderers=islands.SelectMany(c=>c.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var colliders=islands.SelectMany(c=>c.GetComponentsInChildren<Collider>(true)).ToArray();
        var bodies=islands.Select(c=>c.Body).Where(b=>b).ToArray();
        return new {
            frame=Time.frameCount, combatSeconds=world.CombatTime,
            livingActors=world.Actors.Count(a=>a.ship&&a.ship.IsAlive),liveShipParts=world.Actors.Where(a=>a.ship&&a.ship.IsAlive).Sum(a=>a.ship.Parts.Count(p=>p.present)),
            wreckParts=debris.PartCount,wreckClusters=islands.Length,farPhysicsClusters=debris.FarPhysicsClusterCount,
            totalMeshRenderers=renderers.Length,enabledMeshRenderers=renderers.Count(r=>r.enabled&&r.gameObject.activeInHierarchy),
            totalColliders=colliders.Length,enabledColliders=colliders.Count(c=>c.enabled&&c.gameObject.activeInHierarchy),convexMeshColliders=colliders.Count(c=>c is MeshCollider m&&m.convex),
            rigidbodies=bodies.Length,awakeDynamicBodies=bodies.Count(b=>!b.isKinematic&&!b.IsSleeping()),
            ccdBodies=bodies.Count(b=>b.collisionDetectionMode!=CollisionDetectionMode.Discrete),interpolatedBodies=bodies.Count(b=>b.interpolation!=RigidbodyInterpolation.None),
            zeroSleepThresholdBodies=bodies.Count(b=>b.sleepThreshold==0),
            allocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),managedBytes=GC.GetTotalMemory(false)
        };
    }
    public static string Snapshot(string label="snapshot")
    {
        string result=Json(Counts());Write(label+"-counts.json",result);return result;
    }
    public static string Start(string label="observed",int frames=240)
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play required.");
        frames=Mathf.Clamp(frames,30,1200);
        var delta=new List<double>();var main=new List<double>();var draw=new List<double>();
        var cpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
        var draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
        int last=Time.frameCount,warm=15;double deadline=EditorApplication.timeSinceStartup+90;
        EditorApplication.CallbackFunction tick=null;
        Action cleanup=()=>{EditorApplication.update-=tick;cpu.Dispose();draws.Dispose();};
        tick=()=>
        {
            try
            {
                if(!Application.isPlaying||EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Wreckage telemetry stopped before enough advancing frames.");
                if(last==Time.frameCount)return;last=Time.frameCount;if(warm-->0)return;
                delta.Add(Time.unscaledDeltaTime*1000);if(cpu.Valid)main.Add(cpu.LastValue/1e6);if(draws.Valid)draw.Add(draws.LastValue);
                if(delta.Count<frames)return;
                var sorted=delta.OrderBy(x=>x).ToArray();
                var report=new {counts=Counts(),resolution=Screen.width+"x"+Screen.height,gpu=SystemInfo.graphicsDeviceName,editor=true,samples=delta.Count,
                    frameMeanMs=delta.Average(),frameP50Ms=sorted[sorted.Length/2],frameP95Ms=sorted[Mathf.Min(sorted.Length-1,Mathf.FloorToInt(sorted.Length*.95f))],frameMaxMs=sorted[sorted.Length-1],
                    mainThreadMeanMs=main.Count>0?(double?)main.Average():null,drawCallsMean=draw.Count>0?(double?)draw.Average():null,
                    note="Observational advancing-frame Editor capture; combat may change during sampling. Not a controlled comparison or standalone Player benchmark."};
                Write(label+"-frames.json",Json(report));cleanup();
            }
            catch(Exception error){Write(label+"-error.txt",error.ToString());cleanup();}
        };
        EditorApplication.update+=tick;return "Read-only observational wreckage telemetry queued.";
    }
    static string Json(object value)
    {
        var type=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        return (string)type.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
    }
    static void Write(string file,string text)
    {Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,Path.GetFileName(file)),text);}
}
