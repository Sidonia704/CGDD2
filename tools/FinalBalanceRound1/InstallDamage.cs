using System;
using System.IO;
using Starframe.Combat;
using UnityEditor;

public static class FinalBalanceRound1Damage
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before installation.");
        const string path="Assets/_Starframe/Settings/Combat/CombatDamageTuning.asset";
        const string battlePath="Assets/_Starframe/Settings/Flight/BattleTuning.asset";
        string backup="Documentation/FinalBalanceRound1-2026-11-03/before/"+battlePath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));
        if(!File.Exists(backup))File.Copy(battlePath,backup);
        if(!AssetDatabase.IsValidFolder("Assets/_Starframe/Settings/Combat"))AssetDatabase.CreateFolder("Assets/_Starframe/Settings","Combat");
        var tuning=AssetDatabase.LoadAssetAtPath<CombatDamageTuning>(path);
        if(!tuning){tuning=UnityEngine.ScriptableObject.CreateInstance<CombatDamageTuning>();AssetDatabase.CreateAsset(tuning,path);}
        tuning.nonMissileDamageMultiplier=.6f;tuning.missileDamageMultiplier=.25f;EditorUtility.SetDirty(tuning);
        var battle=AssetDatabase.LoadAssetAtPath<BattleTuning>(battlePath);
        if(!battle)throw new InvalidOperationException("Missing battle tuning.");
        battle.damageTuning=tuning;battle.enemyWeaponDamageMultiplier=.5f;battle.missileBlastBudgetMultiplier=1.5f;EditorUtility.SetDirty(battle);
        const string flightPath="Assets/_Starframe/Settings/Flight/FlightTuning.asset";
        string flightBackup="Documentation/FinalBalanceRound1-2026-11-03/before/"+flightPath;
        if(!File.Exists(flightBackup))File.Copy(flightPath,flightBackup);
        var flight=AssetDatabase.LoadAssetAtPath<Starframe.Flight.FlightTuning>(flightPath);
        if(!flight)throw new InvalidOperationException("Missing flight tuning.");
        flight.damageTuning=tuning;EditorUtility.SetDirty(flight);
        AssetDatabase.SaveAssets();
        return "Non-missiles0.6 / missiles0.25 / enemy0.5 / blast aggregate1.5; "+path;
    }
}
