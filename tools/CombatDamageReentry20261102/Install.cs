using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Starframe.Flight;
public static class InstallCombatDamage
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop owned Play session before balancing assets.");
        string root="Documentation/CombatDamageReentry-2026-11-02";Directory.CreateDirectory(root+"/after/native");
        string[] names={"LaserRapidCannon","Laser2Weapon","Laser3Weapon","Gun1Weapon","Gun2Weapon","Gun3Weapon","Railgun1Weapon","Railgun2Weapon","Railgun3Weapon","Missile1Weapon","Missile2Weapon","Missile3Weapon","Sword1Weapon","Sword2Weapon"};
        float[] damage={420,690,1140,180,255,360,3600,6000,10400,11200,19200,32000,750,1350};
        for(int i=0;i<names.Length;i++){
            var asset=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+names[i]+".asset");
            if(!asset)throw new Exception("Missing "+names[i]);
            string before=root+"/before/native/"+names[i]+".asset.json";
            if(!File.Exists(before))File.WriteAllText(before,EditorJsonUtility.ToJson(asset,true));
            asset.damage=damage[i];
            if(asset.delivery==WeaponDelivery.GuidedMissile)asset.missileLockSeconds=names[i]=="Missile1Weapon"?1:names[i]=="Missile2Weapon"?1.2f:1.4f;
            EditorUtility.SetDirty(asset);
        }
        foreach(var guid in AssetDatabase.FindAssets("t:ModuleDefinition",new[]{"Assets/_Starframe/Settings/Modules"})){
            string path=AssetDatabase.GUIDToAssetPath(guid);var module=AssetDatabase.LoadAssetAtPath<ModuleDefinition>(path);
            string before=root+"/before/native/"+Path.GetFileName(path)+".json";
            if(!File.Exists(before))File.WriteAllText(before,EditorJsonUtility.ToJson(module,true));
            var s=module.shape;
            bool basic=s==Starframe.Construction.BlockShape.Cube||s==Starframe.Construction.BlockShape.Wedge||s==Starframe.Construction.BlockShape.Slab||s==Starframe.Construction.BlockShape.Pyramid||s==Starframe.Construction.BlockShape.Block5||s==Starframe.Construction.BlockShape.Block6||s==Starframe.Construction.BlockShape.Block7||s==Starframe.Construction.BlockShape.Block8;
            module.recoverySeconds=basic?4:s==Starframe.Construction.BlockShape.Core||module.shieldCapacity>0?8:6;
            if(module.shieldCapacity>0){module.shieldRecoveryDelay=6;module.shieldRecoveryPerSecond=module.shieldCapacity/4;}
            EditorUtility.SetDirty(module);
        }
        var battle=AssetDatabase.LoadAssetAtPath<Starframe.Combat.BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");
        string battleBefore=root+"/before/native/BattleTuning.asset.json";
        if(!File.Exists(battleBefore))File.WriteAllText(battleBefore,EditorJsonUtility.ToJson(battle,true));
        battle.outOfCombatDelay=6;EditorUtility.SetDirty(battle);
        AssetDatabase.SaveAssets();
        foreach(string type in new[]{"ModuleDefinition","BattleTuning"})foreach(var guid in AssetDatabase.FindAssets("t:"+type,new[]{"Assets/_Starframe/Settings"})){
            string path=AssetDatabase.GUIDToAssetPath(guid);File.WriteAllText(root+"/after/native/"+Path.GetFileName(path)+".json",EditorJsonUtility.ToJson(AssetDatabase.LoadMainAssetAtPath(path),true));
        }
        foreach(string name in names)File.WriteAllText(root+"/after/native/"+name+".asset.json",EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+name+".asset"),true));
        return "Updated shared saturation damage, 6s quiet gate, 4/6/8s module recovery and 4s full shield recharge; missile acquisition 1/1.2/1.4s; cadence, energy, HP, AI and death respawn unchanged.";
    }
}