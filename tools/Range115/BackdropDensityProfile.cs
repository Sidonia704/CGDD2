using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using Starframe.Flight;
public static class BackdropDensityProfile
{
 static RangeAsteroidBackdrop backdrop;static FieldInfo budget;static int originalBudget,phase,frames,lastFrame;static bool originalEnabled;
 static ProfilerRecorder cpu,gpu;static readonly List<double> cpuSamples=new List<double>(),gpuSamples=new List<double>();static readonly List<string> rows=new List<string>();
 public static string Begin()
 {
  backdrop=UnityEngine.Object.FindFirstObjectByType<RangeAsteroidBackdrop>();budget=typeof(RangeAsteroidBackdrop).GetField("maximumVisibleTriangles",BindingFlags.NonPublic|BindingFlags.Instance);originalBudget=(int)budget.GetValue(backdrop);originalEnabled=backdrop.backdropEnabled;
  cpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
  try{gpu=ProfilerRecorder.StartNew(ProfilerCategory.Render,"GPU Frame Time",1);}catch{gpu=default;}
  phase=frames=0;lastFrame=-1;rows.Clear();Apply();EditorApplication.update+=Tick;return "Sampling off, 4m, and 12m for 120 frames each after warmup; restores settings automatically.";
 }
 static void Apply(){frames=0;cpuSamples.Clear();gpuSamples.Clear();backdrop.backdropEnabled=phase>0;budget.SetValue(backdrop,phase==1?4000000:12000000);}
 static void Tick()
 {
  if(!Application.isPlaying||!backdrop){Finish();return;}if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;frames++;
  if(frames>30){if(cpu.Valid&&cpu.LastValue>0)cpuSamples.Add(cpu.LastValue/1e6);if(gpu.Valid&&gpu.LastValue>0)gpuSamples.Add(gpu.LastValue/1e6);}
  if(frames<150)return;
  cpuSamples.Sort();gpuSamples.Sort();string cpuValue=cpuSamples.Count>0?cpuSamples[cpuSamples.Count/2].ToString("R",CultureInfo.InvariantCulture):"null";string gpuValue=gpuSamples.Count>0?gpuSamples[gpuSamples.Count/2].ToString("R",CultureInfo.InvariantCulture):"null";
  rows.Add("{\"phase\":\""+(phase==0?"off":phase==1?"4m":"12m")+"\",\"cpuMainMedianMs\":"+cpuValue+",\"gpuMedianMs\":"+gpuValue+",\"visible\":"+backdrop.visibleRockCount+",\"triangles\":"+backdrop.estimatedTriangles+"}");
  phase++;if(phase<3)Apply();else Finish();
 }
 static void Finish()
 {
  EditorApplication.update-=Tick;if(backdrop){backdrop.backdropEnabled=originalEnabled;budget.SetValue(backdrop,originalBudget);}cpu.Dispose();gpu.Dispose();
  File.WriteAllText("tools/Range115/backdrop-density-profile.json","["+string.Join(",",rows)+"]");
 }
}
