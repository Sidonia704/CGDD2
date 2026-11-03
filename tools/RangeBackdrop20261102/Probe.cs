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
public static class RangeBackdropProbe
{
    public static string Start()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play required.");
        string directory=Path.GetFullPath("Documentation/RangeBackdrop-2026-11-02");Directory.CreateDirectory(directory);
        var baseline=new List<double>();var enhanced=new List<double>();var gpuBase=new List<double>();var gpuEnhanced=new List<double>();var timing=new FrameTiming[1];
        var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);RangeAsteroidBackdrop backdrop=null;RangeCombatWorld world=null;
        int phase=-1,warm=40,last=-1;double deadline=EditorApplication.timeSinceStartup+120;
        EditorApplication.CallbackFunction update=null;
        Action cleanup=()=>{EditorApplication.update-=update;recorder.Dispose();if(backdrop)backdrop.backdropEnabled=true;};
        update=()=>
        {
            try
            {
                if(!Application.isPlaying||EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Backdrop probe timed out.");
                if(phase<0)
                {
                    world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();if(!world||world.Player==null||RangeLoadingOverlay.IsLoading)return;
                    backdrop=UnityEngine.Object.FindFirstObjectByType<RangeAsteroidBackdrop>();if(!backdrop||backdrop.totalRockCount<1000)throw new InvalidOperationException("Backdrop not configured/built.");
                    var hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();
                    var volume=UnityEngine.Object.FindFirstObjectByType<Volume>();if(volume.profile.TryGet<DepthOfField>(out var dof)&&dof.mode.value!=DepthOfFieldMode.Off)throw new InvalidOperationException("Depth of field must be off.");
                    world.SetBotMode(RangeBotMode.Stationary,false);world.Projectiles.Clear();world.Player.flight.SetExternalInput(Vector3.zero,Vector2.zero);backdrop.backdropEnabled=false;phase=0;
                }
                if(last==Time.frameCount)return;last=Time.frameCount;
                if(phase==2){backdrop.backdropEnabled=true;phase=1;warm=40;return;}
                FrameTimingManager.CaptureFrameTimings();if(warm-->0)return;
                var sample=phase==0?baseline:enhanced;var gpu=phase==0?gpuBase:gpuEnhanced;
                sample.Add(recorder.Valid?recorder.LastValue/1e6:Time.unscaledDeltaTime*1000);
                if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].gpuFrameTime>0)gpu.Add(timing[0].gpuFrameTime);
                if(sample.Count<120)return;
                if(phase==0){ScreenCapture.CaptureScreenshot(Path.Combine(directory,"flight-baseline.png"));phase=2;return;}
                ScreenCapture.CaptureScreenshot(Path.Combine(directory,"flight-backdrop.png"));
                var camera=UnityEngine.Object.FindFirstObjectByType<RangeHUD>().WorldCamera;
                if(backdrop.visibleRockCount==0||backdrop.visibleBatchCount==0)throw new InvalidOperationException("No backdrop reached world view.");
                string report=FormattableString.Invariant($"{{\"editor\":true,\"resolution\":\"{Screen.width}x{Screen.height}\",\"gpu\":\"{SystemInfo.graphicsDeviceName}\",\"framesPerMode\":120,\"baselineMainMs\":{baseline.Average()},\"backdropMainMs\":{enhanced.Average()},\"deltaMainMs\":{enhanced.Average()-baseline.Average()},\"baselineGpuMs\":{Number(gpuBase)},\"backdropGpuMs\":{Number(gpuEnhanced)},\"totalRocks\":{backdrop.totalRockCount},\"visibleRocks\":{backdrop.visibleRockCount},\"batches\":{backdrop.visibleBatchCount},\"triangles\":{backdrop.estimatedTriangles},\"farClip\":{camera.farClipPlane},\"nearClip\":{camera.nearClipPlane},\"backdropColliders\":{backdrop.GetComponents<Collider>().Length},\"backdropRigidbodies\":{backdrop.GetComponents<Rigidbody>().Length},\"note\":\"Fixed-input Editor view; new planet/no DOF in both modes. Not a standalone Player performance guarantee.\"}}");
                File.WriteAllText(Path.Combine(directory,"performance.json"),report);cleanup();
            }
            catch(Exception error){File.WriteAllText(Path.Combine(directory,"probe-error.txt"),error.ToString());cleanup();Debug.LogException(error);}
        };
        EditorApplication.update+=update;return "Queued 120-frame backdrop off/on comparison after range loading.";
    }
    static string Number(List<double> values)=>values.Count>0?values.Average().ToString(System.Globalization.CultureInfo.InvariantCulture):"null";
}
