using System;
using System.IO;
using UnityEditor;
using Starframe.Combat;
using Starframe.Flight;

public static class SessionNov03WeaponInstall
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before installing weapon tuning.");
        const string scalePath="Assets/_Starframe/Settings/Combat/CombatDamageTuning.asset";
        Backup(scalePath);
        var scale=AssetDatabase.LoadAssetAtPath<CombatDamageTuning>(scalePath);
        if(!scale)throw new InvalidOperationException("Missing shared combat damage tuning.");
        scale.playerDirectDamageMultiplier=1.1f;scale.playerMissileDamageMultiplier=.85f;EditorUtility.SetDirty(scale);
        string[] names={"Railgun1Weapon","Railgun2Weapon","Railgun3Weapon"};float[] rates={1.5f,1.25f,1f};
        for(int i=0;i<names.Length;i++){
            string path="Assets/_Starframe/Settings/Modules/"+names[i]+".asset";Backup(path);
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if(!weapon)throw new InvalidOperationException("Missing "+path);
            weapon.shotsPerSecond=rates[i];EditorUtility.SetDirty(weapon);
        }
        AssetDatabase.SaveAssets();
        return "Player-only direct x1.10, missile x0.85; shared category and enemy multipliers retained. Railgun1/2/3 rates1.50/1.25/1.00. V default and missile6/s are runtime defaults.";
    }
    static void Backup(string path)
    {
        string target="Documentation/SessionNov03/weapon-before/"+Path.GetFileName(path)+".txt";
        Directory.CreateDirectory(Path.GetDirectoryName(target));if(!File.Exists(target))File.Copy(path,target);
    }
}
