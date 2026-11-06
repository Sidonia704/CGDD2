using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.EditorTools;
public static class PhysicalProbe
{
 public static object Run()
 {
  return new[]{"Range115Voxels","SaturnRingVoxels"}.Select(name=>{
   var old=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>("Assets/_Starframe/Maps/"+name+"/Layouts/"+name+".asset");
   var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(UniformMapBuilder.Root+"/"+name+"/Layouts/"+name+".asset");
   return old.instances.Select((i,k)=>{var p=Vector3.zero;foreach(var c in i.asset.cells)p+=(Vector3)c.position; p=p/i.asset.cells.Count*i.asset.cellSize*i.scale;return new {before=i,after=map.instances[k],expected=p,error=(p-map.instances[k].asset.authoredPhysics.centerOfMass).magnitude};}).OrderByDescending(x=>x.error).Take(2).Select(x=>new {source=x.before.asset.name,x.before.scale,x.expected,actual=x.after.asset.authoredPhysics.centerOfMass,x.error,recapture=UniformPhysicsCapture.Capture(x.before.asset,x.before.scale).centerOfMass}).ToArray();
  }).ToArray();
 }
}
