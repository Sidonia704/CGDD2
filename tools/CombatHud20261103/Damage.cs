using System;
using System.IO;
using System.Globalization;
using UnityEditor;
using Starframe.Flight;
public static class DoubleDirectDamage
{
 public static string Main()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stopped editor required");
  string root="Documentation/CombatHud-2026-11-03/before/";int count=0;
  foreach(var guid in AssetDatabase.FindAssets("t:WeaponDefinition",new[]{"Assets/_Starframe/Settings/Modules"})){
   var path=AssetDatabase.GUIDToAssetPath(guid);var w=AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
   if(w.delivery==WeaponDelivery.GuidedMissile)continue;
   string backup=root+w.name+".damage.txt";
   if(!File.Exists(backup))File.WriteAllText(backup,w.damage.ToString("R",CultureInfo.InvariantCulture));
   w.damage=float.Parse(File.ReadAllText(backup),CultureInfo.InvariantCulture)*2;EditorUtility.SetDirty(w);count++;
  }
  AssetDatabase.SaveAssets();return "Doubled "+count+" direct-fire damage profiles, guided missiles unchanged; idempotent against saved baseline.";
 }
}
