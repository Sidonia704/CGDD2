using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Profiling;
using Starframe.Flight;
using Starframe.Combat;
public static class RangeAtmosphereProbeStressV4
{
    static EditorApplication.CallbackFunction update;
    public static string Start()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play first.");
        if(update!=null)throw new InvalidOperationException("A probe is already running.");
        string dir=Path.GetFullPath("Documentation/RangeAtmosphere-2026-11-02");Directory.CreateDirectory(dir);
        double deadline=EditorApplication.timeSinceStartup+120;RangeCombatWorld world=null;RangeAtmosphere atmosphere=null;
        Volume volume=null;VolumeProfile finalProfile=null;RangeBotMode oldMode=RangeBotMode.Stationary;
        int phase=-1,warm=0,lastFrame=-1;int[] particleIds=null;var fadedSystems=new List<ParticleSystem>();var baseline=new List<double>();var enhanced=new List<double>();var gpuBaseline=new List<double>();var gpuEnhanced=new List<double>();
        var main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
        var timings=new FrameTiming[1];
        Action cleanup=()=>{EditorApplication.update-=update;update=null;main.Dispose();foreach(var ps in fadedSystems)if(ps){var fade=ps.colorOverLifetime;fade.enabled=true;ps.Clear();}if(volume&&finalProfile)volume.profile=finalProfile;if(atmosphere)atmosphere.enabled=true;if(world)world.SetBotMode(oldMode,false);};
        update=()=>{
            try
            {
                if(!Application.isPlaying||EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Atmosphere probe stopped before completion.");
                if(phase<0)
                {
                    world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();if(!world||world.Player==null||RangeLoadingOverlay.IsLoading)return;
                    atmosphere=UnityEngine.Object.FindFirstObjectByType<RangeAtmosphere>();if(!atmosphere)throw new InvalidOperationException("Atmosphere component missing.");
                    var so=new SerializedObject(atmosphere);volume=(Volume)so.FindProperty("volume").objectReferenceValue;finalProfile=volume.profile;
                    oldMode=world.BotMode;world.SetBotMode(RangeBotMode.Stationary,false);world.Projectiles.Clear();world.Player.flight.SetExternalInput(Vector3.zero,Vector2.zero);
                    atmosphere.enabled=false;RenderSettings.fog=false;volume.profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Starframe/Settings/Rendering/ConstructionPostFX.asset");
                    phase=0;warm=45;
                }
                if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
                if(phase==2)
                {
                    volume.profile=finalProfile;atmosphere.enabled=true;
                    var stressSystems=atmosphere.GetComponentsInChildren<ParticleSystem>();particleIds=stressSystems.Select(p=>p.GetInstanceID()).ToArray();
                    foreach(var ps in stressSystems){var fade=ps.colorOverLifetime;if(fade.enabled){fadedSystems.Add(ps);fade.enabled=false;}ps.Clear();ps.Emit(new ParticleSystem.EmitParams{startLifetime=60},ps.main.maxParticles);}
                    File.WriteAllText(Path.Combine(dir,"stress-initial.txt"),"V4 "+DateTime.UtcNow.ToString("O")+" "+atmosphere.DustCount+"/"+atmosphere.HazeCount);
                    phase=1;warm=45;return;
                }
                FrameTimingManager.CaptureFrameTimings();if(warm-->0)return;
                var samples=phase==0?baseline:enhanced;var gpu=phase==0?gpuBaseline:gpuEnhanced;
                samples.Add(main.Valid&&main.LastValue>0?main.LastValue/1e6:Time.unscaledDeltaTime*1000);
                if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].gpuFrameTime>0)gpu.Add(timings[0].gpuFrameTime);
                if(samples.Count<120)return;
                if(phase==0)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir,"before-atmosphere.png"));
                    phase=2;return;
                }
                ScreenCapture.CaptureScreenshot(Path.Combine(dir,"after-atmosphere.png"));
                var systems=atmosphere.GetComponentsInChildren<ParticleSystem>();bool caps=atmosphere.DustCount<=atmosphere.DustBudget&&atmosphere.HazeCount<=atmosphere.HazeBudget;
                bool physics=systems.All(p=>!p.collision.enabled&&!p.trigger.enabled&&!p.lights.enabled&&!p.trails.enabled);
                bool visible=systems.All(p=>p.GetComponent<ParticleSystemRenderer>().enabled);
                bool stable=particleIds.SequenceEqual(systems.Select(p=>p.GetInstanceID()));
                if(!caps||!physics||!visible||!stable)throw new InvalidOperationException("Atmosphere resource/enable-state/stable-system check failed.");
                var grid=atmosphere.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="ReferenceGrid"||r.name=="Boundary").ToArray();
                double b=baseline.Average(),e=enhanced.Average();
                string json=FormattableString.Invariant($"{{\"editor\":true,\"frameSamplesPerMode\":120,\"metric\":\"{(main.Valid?"Main Thread recorder milliseconds (includes Editor scheduling)":"Editor frame intervals milliseconds")}\",\"baselineMeanMs\":{b},\"enhancedMeanMs\":{e},\"differenceMs\":{e-b},\"baselineP95Ms\":{P95(baseline)},\"enhancedP95Ms\":{P95(enhanced)},\"baselineGpuMs\":{(gpuBaseline.Count>0?gpuBaseline.Average().ToString(System.Globalization.CultureInfo.InvariantCulture):"null")},\"enhancedGpuMs\":{(gpuEnhanced.Count>0?gpuEnhanced.Average().ToString(System.Globalization.CultureInfo.InvariantCulture):"null")},\"dustAlive\":{atmosphere.DustCount},\"dustCap\":{atmosphere.DustBudget},\"hazeAlive\":{atmosphere.HazeCount},\"hazeCap\":{atmosphere.HazeBudget},\"enabledGridRenderers\":{grid.Count(r=>r.enabled)},\"gridRenderers\":{grid.Length},\"particlePhysicsDisabled\":true,\"fogEnabled\":{RenderSettings.fog.ToString().ToLowerInvariant()},\"note\":\"Fixed-view Editor comparison with AI stationary; same new sparse grid in both modes; not a standalone Player FPS or GPU guarantee.\"}}");
                File.WriteAllText(Path.Combine(dir,"comparison.json"),json);cleanup();
            }
            catch(Exception error){File.WriteAllText(Path.Combine(dir,"probe-error.txt"),error.ToString());cleanup();Debug.LogException(error);}
        };
        EditorApplication.update+=update;return "Probe queued: wait for actual range readiness, then fixed-view 120-frame baseline and 120-frame enhanced capture.";
    }
    static double P95(List<double> values){var sorted=values.OrderBy(x=>x).ToArray();return sorted[(int)Math.Ceiling(sorted.Length*.95)-1];}
}
