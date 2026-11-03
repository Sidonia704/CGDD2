using System.IO;
using UnityEditor;
using UnityEngine;
using Starframe.Combat;
public static class InstallContinuousMissionNov03
{
    public static object Run()
    {
        const string path="Assets/_Starframe/Settings/Game/SaturnRingWaves.asset";
        var config=AssetDatabase.LoadAssetAtPath<ArcadeMissionDefinition>(path);
        if(!config)throw new System.InvalidOperationException("Missing native SaturnRingWaves");
        const string backup="Documentation/ContinuousMission-2026-11-03/before/SaturnRingWaves.json";
        if(!File.Exists(backup))File.WriteAllText(backup,EditorJsonUtility.ToJson(config,true));
        config.continuousStages=true;config.stageMinimumSeconds=35;config.stageTargetSeconds=135;
        config.stageOverlapEnemies=3;config.combatAreaFraction=.62f;config.minimumArenaHalfExtent=2200;
        EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();
        return new {config.continuousStages,config.stageMinimumSeconds,config.stageTargetSeconds,config.stageOverlapEnemies,config.combatAreaFraction,config.minimumArenaHalfExtent,config.TotalEnemies};
    }
}
