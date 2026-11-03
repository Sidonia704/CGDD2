using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Starframe.App;
using Starframe.Combat;
using Starframe.Flight;
public static class RangeProfile
{
 static Task running;
 public static string Start(string label="before"){running=Run(label);return "Started "+label;}
 static async Task Frame(){int f=Time.frameCount;while(Time.frameCount==f)await Task.Yield();}
 static async Task Run(string label)
 {
  string path="Documentation/CombatHud-2026-11-03/profile-"+label+".txt";
  var rec=new List<(string,ProfilerRecorder)>();var sums=new Dictionary<string,List<double>>();
  var mouse=Mouse.current;bool pressed=mouse!=null&&mouse.leftButton.isPressed;
  try{
   FlightSession.Begin(ShipWorkspace.Ship,ShipWorkspace.Dirty);AppNavigation.HasAppContext=true;
   RangeLoadingOverlay.LoadScene(FlightSession.RangeScene);double deadline=Time.realtimeSinceStartupAsDouble+120;RangeCombatWorld world=null;
   while((world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>())==null||world.Player==null||RangeLoadingOverlay.IsLoading){if(Time.realtimeSinceStartupAsDouble>deadline)throw new Exception("Range load timeout");await Frame();}
   world.Player.ship.InvulnerableUntil=float.MaxValue;
   foreach(string name in new[]{"Main Thread","Physics.Simulate","GC.Alloc","Starframe.Projectiles","Starframe.RangeAI","Starframe.PowerAndWeapons","Starframe.ShipInstances","Starframe.Connectivity","Starframe.Performance","Starframe.SpawnWreckage"}){
    var r=ProfilerRecorder.StartNew(name=="Main Thread"?ProfilerCategory.Internal:name=="Physics.Simulate"?ProfilerCategory.Physics:name=="GC.Alloc"?ProfilerCategory.Memory:ProfilerCategory.Scripts,name,1);rec.Add((name,r));sums[name]=new List<double>();
   }
   double start=Time.realtimeSinceStartupAsDouble;var delta=new List<double>();
   while(Time.realtimeSinceStartupAsDouble-start<25){if(mouse!=null&&Time.realtimeSinceStartupAsDouble-start>5)InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});await Frame();delta.Add(Time.unscaledDeltaTime*1000);foreach(var p in rec)if(p.Item2.Valid)sums[p.Item1].Add(p.Item2.LastValue/(p.Item1=="GC.Alloc"?1.0:1000000.0));}
   delta.Sort();string report=$"frame p50={delta[delta.Count/2]:F2} p95={delta[(int)(delta.Count*.95)]:F2} max={delta.Last():F2}\nactors={world.Actors.Count} parts={world.Actors.Sum(a=>a.ship.Parts.Count)} wreck={world.Debris.PartCount} projectiles={world.Projectiles.ActiveCount}\n";
   foreach(var p in sums)if(p.Value.Count>0)report+=$"{p.Key}: mean={p.Value.Average():F3} max={p.Value.Max():F3}\n";
   File.WriteAllText(path,report+"DONE");
  }catch(Exception e){File.WriteAllText(path,"FAIL "+e);}finally{foreach(var p in rec)p.Item2.Dispose();if(mouse!=null)InputSystem.QueueStateEvent(mouse,new MouseState{buttons=(ushort)(pressed?1:0)});}
 }
}
