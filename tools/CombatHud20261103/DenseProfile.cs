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
public static class DenseRangeProfile
{
 static Task running;
 public static string Start(string label="dense-before",int rounds=4,bool shooting=false){running=Run(label,rounds,shooting);return "Started "+label;}
 static async Task Frame(){int n=Time.frameCount;while(n==Time.frameCount)await Task.Yield();}
 static async Task Run(string label,int rounds,bool shooting)
 {
  string file="Documentation/CombatHud-2026-11-03/"+label+".txt";var frames=new List<double>();var physics=new List<double>();
  var phys=ProfilerRecorder.StartNew(ProfilerCategory.Physics,"Physics.Simulate",1);
  var recorders=new List<(string,ProfilerRecorder)>();var values=new Dictionary<string,List<double>>();
  var mouse=Mouse.current;bool held=mouse!=null&&mouse.leftButton.isPressed;
  try{
   double loadStarted=Time.realtimeSinceStartupAsDouble;FlightSession.Begin(ShipWorkspace.Ship,false);RangeLoadingOverlay.LoadScene(FlightSession.RangeScene);RangeCombatWorld w=null;double end=Time.realtimeSinceStartupAsDouble+150;
   while((w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>())==null||w.Player==null||RangeLoadingOverlay.IsLoading){if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Load timeout");await Frame();}
   double loadSeconds=Time.realtimeSinceStartupAsDouble-loadStarted;
   w.Player.ship.InvulnerableUntil=float.MaxValue;w.SetBotMode(RangeBotMode.Stationary,false);
   var map=UnityEngine.Object.FindFirstObjectByType<Starframe.Maps.SpaceMapRuntime>();foreach(var body in map.Structures)body.CollectDiagnostics=true;
   for(int round=0;round<rounds;round++){
    foreach(var a in w.Bots.ToArray())if(a.ship.IsAlive){var c=a.ship.Parts.First(p=>p.record.shape==Starframe.Construction.BlockShape.Core);a.ship.ApplyDamage(c.record.id,float.MaxValue);await Frame();}
    for(int i=0;i<20;i++)await Frame();w.ResetTargets();
   }
   File.WriteAllText(file,"loadSeconds="+loadSeconds.ToString("F2")+" wreck parts="+w.Debris.PartCount+" clusters="+w.Debris.Clusters.Count+"\n");
   var start=new Vector3(0,-650,-1100);var goal=new Vector3(0,-1330,100);var heading=Quaternion.LookRotation(goal-start);
   var flight=w.Player.flight;flight.ResetMotion(start,heading);flight.SetExternalInput(Vector3.forward,Vector2.zero,0,true);
   foreach(string name in new[]{"Starframe.MapColliderPreparation","Starframe.MapDamageFlush","Starframe.WreckInstances","Starframe.WreckInstanceRebuild","Starframe.Projectiles","Starframe.SpawnWreckage"}){recorders.Add((name,ProfilerRecorder.StartNew(ProfilerCategory.Scripts,name,1)));values[name]=new List<double>();}
   end=Time.realtimeSinceStartupAsDouble+22;double previous=Time.realtimeSinceStartupAsDouble;
   while(Time.realtimeSinceStartupAsDouble<end){if(shooting&&mouse!=null)InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});await Frame();double now=Time.realtimeSinceStartupAsDouble;frames.Add((now-previous)*1000);previous=now;if(phys.Valid)physics.Add(phys.LastValue/1e6);foreach(var pair in recorders)if(pair.Item2.Valid)values[pair.Item1].Add(pair.Item2.LastValue/1e6);}
   frames.Sort();File.AppendAllText(file,$"p50={frames[frames.Count/2]:F2} p95={frames[(int)(frames.Count*.95)]:F2} max={frames.Last():F2}\nphysicsMean={physics.DefaultIfEmpty().Average():F2} physicsMax={physics.DefaultIfEmpty().Max():F2}\nposition={w.Player.body.position}\nDONE");
   flight.ResetMotion(start,heading);flight.SetExternalInput(Vector3.zero,Vector2.zero,0,false);
   foreach(var pair in values)if(pair.Value.Count>0)File.AppendAllText(file,$"\n{pair.Key} mean={pair.Value.Average():F3} max={pair.Value.Max():F3}");
   foreach(var body in map.Structures.Where(x=>x&&x.GeometryBuildCount>1).OrderByDescending(x=>x.GeometryMs).Take(8))File.AppendAllText(file,$"\n{body.name} cells={body.AliveCount} geom={body.GeometryMs:F2} connect={body.ConnectivityMs:F2} coll={body.ColliderMs:F2} flush={body.LastFlushMilliseconds:F2} light={body.UsesLightweightDamageVisuals}");
  }catch(Exception e){File.AppendAllText(file,"FAIL "+e);}finally{phys.Dispose();foreach(var pair in recorders)pair.Item2.Dispose();if(mouse!=null)InputSystem.QueueStateEvent(mouse,new MouseState{buttons=(ushort)(held?1:0)});}
 }
}
