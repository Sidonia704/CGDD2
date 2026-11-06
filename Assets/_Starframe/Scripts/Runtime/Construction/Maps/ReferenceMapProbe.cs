// @role Measure the standalone reference-map sandbox only when explicit command-line probe flags are supplied.
// @calls SpaceMapRuntime, VoxelStructureRuntime
// @tune captureFrames:int:60-2000:Rendered frames sampled per overview/near-station measurement phase.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Starframe.Maps
{
    public sealed class ReferenceMapProbe : MonoBehaviour
    {
        public SpaceMapRuntime map;
        public Camera view;
        public int captureFrames = 120;
        [Serializable] sealed class Phase { public string name; public int frames; public double meanMs,p95Ms,maxMs,gpuMeanMs; public int gpuSamples; }
        [Serializable] sealed class Report
        {
            public string unity,device,graphics,scene;
            public int structures,initialCells,colliders,rigidbodies,initialRecentDamage;
            public long managedBytes,allocatedBytes;
            public double loadSeconds;
            public double manualPhysicsMeanMs,manualPhysicsMaxMs;
            public int awakeBodies;
            public int nearActiveColliders;
            public int initialActiveColliders;
            public string pushedBody;
            public bool loaded,damageExpiredWithoutRestoringHole,pushedBodyMoved,conservedSurvivingCells;
            public Phase[] phases;
            public string error,note="Graphics-enabled standalone probe; CPU frame intervals include rendering/presentation. GPU samples are reported only when available. Not an Editor FPS estimate.";
        }
        string reportPath;
        readonly FrameTiming[] timings=new FrameTiming[1];
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-sfMapReport");
            if(index<0||index+1>=args.Length){enabled=false;yield break;}
            reportPath=args[index+1];Application.runInBackground=true;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;
            var controls=view?view.GetComponent<MapSandboxCamera>():null;if(controls)controls.enabled=false;
            var report=new Report{unity=Application.unityVersion,device=SystemInfo.processorType,graphics=SystemInfo.graphicsDeviceName,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path};
            double started=Time.realtimeSinceStartupAsDouble;
            Debug.Log("ReferenceMapProbe started: "+reportPath);
            while(map&&!map.BuildIsComplete&&Time.realtimeSinceStartupAsDouble-started<240)yield return null;
            report.loadSeconds=Time.realtimeSinceStartupAsDouble-started;report.loaded=map&&map.BuildIsComplete;
            if(!report.loaded){report.error="Map did not finish loading within 240 seconds.";Finish(report);yield break;}
            report.structures=map.Structures.Count(s=>s);report.initialCells=map.Structures.Where(s=>s).Sum(s=>s.AliveCount);
            report.colliders=map.Structures.Where(s=>s).Sum(s=>s.ColliderCount);report.rigidbodies=map.Structures.Count(s=>s&&s.Body);
            report.initialActiveColliders=map.ActiveMapColliderCount;
            Debug.Log("ReferenceMapProbe loaded in "+report.loadSeconds+" seconds; colliders="+report.colliders);
            bool diagnostic=args.Contains("-sfMapDiagnostic");
            var originalSimulation=Physics.simulationMode;
            if(diagnostic)
            {
                Physics.simulationMode=SimulationMode.Script;
                var physicsTimes=new List<double>();
                for(int n=0;n<8;n++){var timer=System.Diagnostics.Stopwatch.StartNew();Physics.Simulate(Time.fixedDeltaTime);physicsTimes.Add(timer.Elapsed.TotalMilliseconds);yield return null;}
                report.manualPhysicsMeanMs=physicsTimes.Average();report.manualPhysicsMaxMs=physicsTimes.Max();
                report.awakeBodies=map.Structures.Count(s=>s&&s.Body&&!s.Body.IsSleeping());
                captureFrames=16;
            }
            for(int n=0;n<5;n++)yield return null;
            var phases=new List<Phase>();
            foreach(string phase in new[]{"overview","near-station"})
            {
                if(phase=="near-station"&&view)
                {
                    view.transform.SetPositionAndRotation(new Vector3(120,95,-350),Quaternion.LookRotation(new Vector3(25,-170,-15)-new Vector3(120,95,-350)));
                }
                var cpu=new List<double>();var gpu=new List<double>();
                for(int warm=0;warm<4;warm++){RenderFrame();yield return null;}
                for(int frame=0;frame<captureFrames;frame++)
                {
                    FrameTimingManager.CaptureFrameTimings();RenderFrame();yield return null;cpu.Add(Time.unscaledDeltaTime*1000.0);
                    if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].gpuFrameTime>0)gpu.Add(timings[0].gpuFrameTime);
                }
                cpu.Sort();phases.Add(new Phase{name=phase,frames=cpu.Count,meanMs=cpu.Average(),p95Ms=cpu[Math.Min(cpu.Count-1,(int)(cpu.Count*.95))],maxMs=cpu[cpu.Count-1],gpuMeanMs=gpu.Count==0?0:gpu.Average(),gpuSamples=gpu.Count});
                if(view)Capture(Path.ChangeExtension(reportPath,null)+"-"+phase+".png");
                Debug.Log("ReferenceMapProbe phase "+phase+": "+phases.Last().meanMs+" ms");
            }
            report.nearActiveColliders=map.Structures.Where(s=>s).Sum(s=>s.GetComponents<Collider>().Count(c=>c.enabled));
            if(diagnostic)Physics.simulationMode=originalSimulation;
            report.phases=phases.ToArray();report.managedBytes=GC.GetTotalMemory(false);report.allocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var target=map.Structures.Where(s=>s&&s.Body&&s.AliveCount>=3&&s.AliveCount<=100&&!s.GetComponent<Joint>()).OrderByDescending(s=>s.transform.position.sqrMagnitude).FirstOrDefault();
            if(target)
            {
                report.pushedBody=target.name;
                var position=target.Body.position;target.Body.AddForce(Vector3.right*target.Body.mass*2,ForceMode.Impulse);
                for(int frame=0;frame<4;frame++){if(diagnostic){Physics.simulationMode=SimulationMode.Script;Physics.Simulate(Time.fixedDeltaTime);yield return null;}else yield return new WaitForFixedUpdate();}
                if(diagnostic)Physics.simulationMode=originalSimulation;
                report.pushedBodyMoved=Vector3.Distance(position,target.Body.position)>.005f;
                report.conservedSurvivingCells=map.Structures.Where(s=>s).Sum(s=>s.AliveCount)==report.initialCells;
            }
            yield return null;Finish(report);
        }
        void RenderFrame()
        {
            if(!view)return;
            var original=view.targetTexture;var target=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGBHalf);
            try{view.targetTexture=target;view.Render();}
            finally{view.targetTexture=original;RenderTexture.ReleaseTemporary(target);}
        }
        void Capture(string path)
        {
            var previous=RenderTexture.active;var cameraTarget=view.targetTexture;
            var target=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGBHalf);
            var texture=new Texture2D(1440,900,TextureFormat.RGB24,false);
            try{view.targetTexture=target;view.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1440,900),0,0);texture.Apply();if(QualitySettings.activeColorSpace==ColorSpace.Linear&&!target.sRGB){var colors=texture.GetPixels();for(int i=0;i<colors.Length;i++)colors[i]=colors[i].gamma;texture.SetPixels(colors);texture.Apply();}File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally{view.targetTexture=cameraTarget;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(texture);}
        }
        void Finish(Report report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));
            if(Environment.GetCommandLineArgs().Contains("-sfMapQuitAfterProbe"))Application.Quit(report.error==null?0:1);
        }
    }
}
