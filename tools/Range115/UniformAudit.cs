using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.EditorTools;
public static class UniformAudit
{
 public static object Samples()
 {
  var assets=AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{UniformMapBuilder.Root+"/Samples/Assets"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>).ToArray();
  return Check(assets,true);
 }
 static object Check(VoxelAssetDefinition[] assets,bool checkConnectivity)
 {
  var errors=new List<string>();long cells=0,tris=0;int connected=0;var components=new List<object>();
  foreach(var a in assets)
  {
   if(a.cellSize!=1)errors.Add(a.name+": unit");if(!a.Validate(out var e))errors.Add(a.name+": "+e);
   if(!VoxelAssemblyBaker.IsVoxelAssemblyCache(a))errors.Add(a.name+": cache");
   if(a.authoredPhysics==null||!a.authoredPhysics.IsValid)errors.Add(a.name+": physics");
   if(a.cells.Any(c=>c.surface<0||c.surface>=a.SurfaceCount))errors.Add(a.name+": palette");
   cells+=a.cells.Count;tris+=VoxelAssemblyBaker.TriangleCount(a.initialLodMesh);
   if(checkConnectivity)
   {
    var left=new HashSet<Vector3Int>(a.cells.Select(c=>c.position));int count=0,smallest=int.MaxValue;
    var offsets=new[]{Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,Vector3Int.forward,Vector3Int.back};
    while(left.Count>0){count++;var first=left.First();left.Remove(first);var queue=new Queue<Vector3Int>();queue.Enqueue(first);int n=0;while(queue.Count>0){var p=queue.Dequeue();n++;foreach(var d in offsets)if(left.Remove(p+d))queue.Enqueue(p+d);}smallest=Math.Min(smallest,n);}
    if(count==1)connected++;else components.Add(new{name=a.name,count,smallest});
   }
  }
  return new{assets=assets.Length,cells,triangles=tris,connected,components,errors};
 }
 public static object Maps()
 {
  var errors=new List<string>();var stats=new List<object>();
  foreach(var name in new[]{"Range115Voxels","SaturnRingVoxels"})
  {
   var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(UniformMapBuilder.Root+"/"+name+"/Layouts/"+name+".asset");
   var old=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>("Assets/_Starframe/Maps/"+name+"/Layouts/"+name+".asset");
   if(!map){errors.Add(name+": missing");continue;}if(!map.Validate(out var e))errors.Add(e);
   if(map.instances.Count!=old.instances.Count||map.assemblyLinks.Count!=old.assemblyLinks.Count)errors.Add(name+": counts");
   double maxMass=0;float maxCOM=0;int chunked=0;
   for(int k=0;k<old.instances.Count;k++)
   {
    var before=old.instances[k];var after=map.instances[k];var physics=after.asset.authoredPhysics;
    if(after.id!=before.id||after.position!=before.position||after.eulerAngles!=before.eulerAngles||after.driftVelocity!=before.driftVelocity||after.angularVelocity!=before.angularVelocity||after.scale!=1)errors.Add(name+": transform "+k);
    double expected=Mathf.Clamp(before.asset.cells.Count*before.asset.massPerCell*Mathf.Pow(before.asset.cellSize,3)*Mathf.Pow(before.scale,3),.01f,1e8f);
    maxMass=Math.Max(maxMass,Math.Abs(physics.mass-expected)/expected);
    double x=0,y=0,z=0;foreach(var c in before.asset.cells){x+=c.position.x;y+=c.position.y;z+=c.position.z;}
    var com=new Vector3((float)(x/before.asset.cells.Count),(float)(y/before.asset.cells.Count),(float)(z/before.asset.cells.Count))*before.asset.cellSize*before.scale;
    maxCOM=Mathf.Max(maxCOM,(com-physics.centerOfMass).magnitude);
   }
   for(int k=0;k<old.assemblyLinks.Count;k++){var a=old.assemblyLinks[k];var b=map.assemblyLinks[k];if(a.worldAnchor!=b.worldAnchor||a.instanceIdA!=b.instanceIdA||a.instanceIdB!=b.instanceIdB||a.createFixedJoint!=b.createFixedJoint||a.ignoreMutualCollision!=b.ignoreMutualCollision||!b.hasLegacyAnchorBounds)errors.Add(name+": link "+k);}
   if(maxMass>1e-4||maxCOM>1e-3)errors.Add(name+": physical drift");
   var assets=map.instances.Select(i=>i.asset).Distinct().ToArray();chunked=assets.Count(a=>a.cells.Count>VoxelAssetDefinition.MaximumCells);
   stats.Add(new{map=name,instances=map.instances.Count,assets=assets.Length,links=map.assemblyLinks.Count,uniqueCells=assets.Sum(a=>(long)a.cells.Count),expandedCells=map.instances.Sum(i=>(long)i.asset.cells.Count),chunked,maxMassRelativeError=maxMass,maxCOMError=maxCOM,checks=Check(assets,false)});
  }
  return new{stats,errors};
 }
}
