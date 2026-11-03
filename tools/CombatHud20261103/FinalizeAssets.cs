using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Starframe.Maps;
using Starframe.Presentation;
public static class FinalizeCombatHudAssets
{
 public static string Main()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stopped Editor required");
  var setup=EditorSceneManager.GetSceneManagerSetup();int changed=0;
  try{
   foreach(string path in new[]{"Assets/_Starframe/Scenes/Testing/FlightRange.unity","Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity"}){
    string backup="Documentation/CombatHud-2026-11-03/before/"+Path.GetFileName(path)+".txt";
    if(!File.Exists(backup))File.Copy(path,backup);
    var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
    foreach(var root in scene.GetRootGameObjects())foreach(var map in root.GetComponentsInChildren<SpaceMapRuntime>(true))if(map.useBudgetedPhysics){
     map.colliderPreparationPerFrame=24;map.colliderPreparationMilliseconds=1.5f;map.lightweightDamageThreshold=128;changed++;
    }
    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   }
   var theme=AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Starframe/Resources/UI/WorkbenchTheme.asset");
   string backupTheme="Documentation/CombatHud-2026-11-03/before/WorkbenchTheme.asset.txt";
   if(!File.Exists(backupTheme))File.Copy(AssetDatabase.GetAssetPath(theme),backupTheme);
   theme.combatInstrument=new Color(.84f,1,.18f,1);EditorUtility.SetDirty(theme);AssetDatabase.SaveAssets();
  }finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
  return "Saved map budgets on "+changed+" maps and independent lime HUD instrument color.";
 }
}
