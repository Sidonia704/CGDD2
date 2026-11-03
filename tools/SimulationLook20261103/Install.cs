using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Starframe.Presentation;
using Starframe.Flight;
public static class InstallSimulationLook
{
 const string Folder="Assets/_Starframe/Art/SimulationTrial";
 static readonly string[] Scenes={"Assets/_Starframe/Scenes/Frontend/Hangar.unity","Assets/_Starframe/Scenes/Construction/ShipEditor.unity","Assets/_Starframe/Scenes/Testing/FlightRange.unity","Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity"};
 public static string Main()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stop Play first.");
  for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Unsaved scene; refusing to replace it.");
  Directory.CreateDirectory(Folder);Directory.CreateDirectory(Folder+"/Before");AssetDatabase.Refresh();
  var shader=Shader.Find("Starframe/Environment/Simulation Sky");if(!shader)throw new Exception("Simulation shader not imported.");
  Material Mat(string name,bool planet){string p=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,p);}m.SetFloat("_ShowPlanet",planet?1:0);m.SetColor("_Background",planet?new Color(.128f,.272f,.52f):new Color(.16f,.34f,.65f));m.SetColor("_GridColor",new Color(.04f,.10f,.22f));m.SetColor("_PlanetColor",new Color(.9f,.36f,.04875f));EditorUtility.SetDirty(m);return m;}
  var bay=Mat("SimulationBay",false);var space=Mat("SimulationSpace",true);
  var oldSky=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/Materials/Environment/SaturnRangeSky.mat");
  foreach(string prop in new[]{"_PlanetWorldRadius","_PlanetCenterWS"})if(oldSky&&oldSky.HasProperty(prop)){if(prop=="_PlanetCenterWS")space.SetVector(prop,oldSky.GetVector(prop));else space.SetFloat(prop,oldSky.GetFloat(prop));}
  var atmosphere=AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>(Folder+"/SimulationAtmosphere.asset");
  if(!atmosphere){atmosphere=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>("Assets/_Starframe/Settings/Rendering/RangeAtmosphereTuning.asset"));AssetDatabase.CreateAsset(atmosphere,Folder+"/SimulationAtmosphere.asset");}
  atmosphere.dustEnabled=false;atmosphere.hazeEnabled=false;atmosphere.fogColor=new Color(.012f,.032f,.075f);atmosphere.fogDensity=.00008f;EditorUtility.SetDirty(atmosphere);AssetDatabase.SaveAssets();
  var setup=EditorSceneManager.GetSceneManagerSetup();
  try{foreach(var path in Scenes){
   string backup=Folder+"/Before/"+Path.GetFileName(path);if(!File.Exists(backup)&&!AssetDatabase.CopyAsset(path,backup))throw new Exception("Backup failed: "+path);
   var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
   bool flight=path.Contains("FlightRange")||path.Contains("SaturnRing");
   RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Folder+(flight?"/SimulationSpace.mat":"/SimulationBay.mat"));
   RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.27f,.39f,.62f);RenderSettings.ambientEquatorColor=new Color(.14f,.22f,.38f);RenderSettings.ambientGroundColor=new Color(.06f,.10f,.19f);
   RenderSettings.fogColor=new Color(.012f,.032f,.075f);
   foreach(var a in UnityEngine.Object.FindObjectsByType<RangeAtmosphere>(FindObjectsSortMode.None)){var so=new SerializedObject(a);so.FindProperty("tuning").objectReferenceValue=AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>(Folder+"/SimulationAtmosphere.asset");so.ApplyModifiedPropertiesWithoutUndo();}
   foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)){camera.clearFlags=CameraClearFlags.Skybox;camera.backgroundColor=new Color(.035f,.10f,.23f);}
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  }}finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
  AssetDatabase.SaveAssets();return "Installed reversible blue simulation trial in four scenes; old HDRI assets and native scene backups retained. No pixel filter.";
 }
 public static string Restore()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stop Play first.");
  for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Unsaved scene.");
  var setup=EditorSceneManager.GetSceneManagerSetup();try{foreach(var path in Scenes){var s=EditorSceneManager.OpenScene(Folder+"/Before/"+Path.GetFileName(path),OpenSceneMode.Single);EditorSceneManager.SaveScene(s,path,true);}}finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
  AssetDatabase.SaveAssets();return "Original scene look restored.";
 }
}
