using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
public static class ExportEditableEnemyFleet
{
 public static string Main()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stopped Editor required");
  var ids=new[]{"needle","viper","wasp","bastion","harrier","halberd","citadel"};
  var fleet=EnemyFleetLibrary.LoadTemplates().ToDictionary(e=>e.id);
  var repo=ShipWorkspace.Repository;var copies=new List<BlueprintData>();var result=new List<string>();
  var previous=BlockModules.ConfiguredDatabase;
  var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
  string workspace=File.ReadAllText(ShipWorkspace.WorkspacePath);
  try{
   BlockModules.Configure(database);
   for(int i=0;i<ids.Length;i++){
    string name="t"+(i+1);if(repo.NameExists(name))throw new IOException("Refusing to overwrite existing blueprint "+name);
    var copy=fleet[ids[i]].CreateBlueprint();copy.name=name;copies.Add(BlueprintRepository.Upgrade(copy));
   }
   for(int i=0;i<copies.Count;i++){
    string file=repo.Save(copies[i]);var loaded=repo.Load(Path.GetFileName(file));
    if(loaded.parts.Count!=copies[i].parts.Count||loaded.weaponAssignments.Count!=copies[i].weaponAssignments.Count)throw new Exception("Roundtrip mismatch "+file);
    result.Add($"t{i+1}\t{ids[i]}\t{fleet[ids[i]].nameEn}\t{loaded.parts.Count} parts\t{file}");
   }
   if(File.ReadAllText(ShipWorkspace.WorkspacePath)!=workspace)throw new Exception("Working ship changed");
   File.WriteAllLines("Documentation/LockRepair-2026-11-03/enemy-blueprints.tsv",result);
  }finally{BlockModules.Configure(previous);}
  return string.Join("\n",result);
 }
}
