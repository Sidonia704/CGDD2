using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
public static class RockRayCheck
{
 public static object Run()
 {
  var rows=new List<object>();int misses=0;
  foreach(var a in AssetDatabase.FindAssets("t:VoxelAssetDefinition",new[]{"Assets/_Starframe/Maps/UniformVoxels/Samples/Assets","Assets/_Starframe/Maps/UniformVoxels/Backdrop/Assets"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>).Where(a=>a.name.Contains("Rock_")||a.name.StartsWith("field_")||a.name.StartsWith("Backdrop_")))
  {
   var g=new GameObject("Temporary shell probe"){hideFlags=HideFlags.HideAndDontSave};int failed=0;var b=a.initialLodMesh.bounds;float distance=b.size.magnitude*2+10;var center=b.center;
   try
   {
    var c=g.AddComponent<MeshCollider>();c.sharedMesh=a.initialLodMesh;
    for(int i=0;i<512;i++){float y=1-2*(i+.5f)/512;float angle=i*2.39996323f;var d=new Vector3(Mathf.Sqrt(1-y*y)*Mathf.Cos(angle),y,Mathf.Sqrt(1-y*y)*Mathf.Sin(angle));if(!c.Raycast(new Ray(center+d*distance,-d),out var hit,distance*1.01f))failed++;}
   }finally{UnityEngine.Object.DestroyImmediate(g);}
   misses+=failed;rows.Add(new{name=a.name,rays=512,misses=failed});
  }
  return new{rows,misses};
 }
}
