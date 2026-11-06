using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Starframe.Flight;
using Starframe.Maps;
public static class BackdropVisibilityCheck
{
 public static string OpenMission()
 {
  foreach(var w in Resources.FindObjectsOfTypeAll<Starframe.EditorTools.SpaceMapStudioWindow>())w.Close();
  EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity");return "Mission opened";
 }
 public static string View(int direction)
 {
  var camera=Camera.main;var chase=camera.GetComponent<FlightChaseCamera>();
  if(direction==0){if(chase){chase.enabled=true;chase.ResetFollow();}}
  else
  {
   if(chase)chase.enabled=false;
   Vector3 target=direction==1?new Vector3(-4500,-1330,6000):direction==2?new Vector3(4500,-1330,6000):new Vector3(0,-1330,19000);
   camera.transform.rotation=Quaternion.LookRotation(target-camera.transform.position,Vector3.up);
  }
  return "View "+direction;
 }
 public static string Read()
 {
  var b=UnityEngine.Object.FindFirstObjectByType<RangeAsteroidBackdrop>();var map=UnityEngine.Object.FindFirstObjectByType<SpaceMapRuntime>();
  var type=typeof(RangeAsteroidBackdrop);var batches=(IEnumerable)type.GetField("renderGroups",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(b);
  var histogram=new int[8];int badScale=0,submitted=0;foreach(var batch in batches){var t=batch.GetType();int count=(int)t.GetField("submittedCount").GetValue(batch);var matrices=(Matrix4x4[])t.GetField("submittedMatrices").GetValue(batch);submitted+=count;histogram[(int)t.GetField("shape").GetValue(batch)/2]+=count;for(int i=0;i<count;i++)for(int a=0;a<3;a++)if(Mathf.Abs(matrices[i].GetColumn(a).magnitude-1)>.0001f)badScale++;}
  var composition=b.VisibleByRegionAndSize;int farLarge=0,farTotal=0;foreach(int region in new[]{2,5,8}){for(int family=0;family<3;family++)farTotal+=composition[region*3+family];farLarge+=composition[region*3+2];}
  return "frame="+Time.frameCount+", ready="+map.BuildIsComplete+", loaded="+map.Structures.Count+", enabled="+b.backdropEnabled+", considered="+b.ConsideredCount+", submitted="+submitted+", triangles="+b.estimatedTriangles+", regions="+string.Join(",",b.VisibleByRegion)+", lod="+string.Join(",",b.VisibleByLod)+", farLarge="+farLarge+"/"+farTotal+", batches="+b.visibleBatchCount+", sizes="+string.Join(",",histogram)+", nonunit="+badScale+", camera="+Camera.main.transform.position.ToString("R");
 }
}


