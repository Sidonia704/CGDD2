using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;
using Starframe.Combat;
using Starframe.App;

public static class CombatPolishInstall
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play before changing the balance assets.");
        string root="Documentation/CombatPolish-2026-11-02";
        Directory.CreateDirectory(root+"/before/native");Directory.CreateDirectory(root+"/after/native");
        string[] weapons={"LaserRapidCannon","Laser2Weapon","Laser3Weapon","Gun1Weapon","Gun2Weapon","Gun3Weapon","Railgun1Weapon","Railgun2Weapon","Railgun3Weapon"};
        float[] speeds={1800,2000,2200,2200,2450,2700,7500,9000,10800};
        float[] diameters={.75f,.85f,1,.5f,.6f,.7f,.3f,.34f,.38f};
        for(int i=0;i<weapons.Length;i++){
            var w=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+weapons[i]+".asset");
            if(!w)throw new InvalidOperationException("Missing weapon: "+weapons[i]);
            w.projectileSpeed=speeds[i];w.projectileDiameter=diameters[i];EditorUtility.SetDirty(w);
        }
        for(int i=0;i<3;i++){
            var w=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Missile"+(i+1)+"Weapon.asset");
            w.projectileSpeed=new[]{240f,245f,250f}[i];w.projectileDiameter=.7f;
            w.missileTurnDegrees=new[]{135f,115f,95f}[i];w.missileTurnAcceleration=new[]{450f,360f,280f}[i];
            w.missileAcceleration=new[]{120f,110f,100f}[i];w.missileSeekerConeDegrees=new[]{70f,65f,60f}[i];
            w.missileSeekerLossSeconds=.45f;w.missileGuidanceSeconds=12+i*2;EditorUtility.SetDirty(w);
        }
        foreach(string name in new[]{"Sword1Weapon","Sword2Weapon"}){
            var w=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+name+".asset");
            w.projectileDiameter=name=="Sword1Weapon"?.7f:.82f;EditorUtility.SetDirty(w);
        }
        int count=0;
        foreach(var guid in AssetDatabase.FindAssets("t:ModuleDefinition",new[]{"Assets/_Starframe/Settings/Modules"})){
            string path=AssetDatabase.GUIDToAssetPath(guid);var d=AssetDatabase.LoadAssetAtPath<ModuleDefinition>(path);
            string before=root+"/before/native/"+path.Replace('/','_')+".json";
            if(!File.Exists(before))File.WriteAllText(before,EditorJsonUtility.ToJson(d,true));
            // Reapply from the exact captured baseline; rerunning never multiplies recovery twice.
            string raw=File.ReadAllText(before);var match=System.Text.RegularExpressions.Regex.Match(raw,"\\\"recoverySeconds\\\"\\s*:\\s*([0-9.Ee+-]+)");
            float baseline=float.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
            d.recoverySeconds=Mathf.Max(20,baseline*2.5f);EditorUtility.SetDirty(d);count++;
        }
        var battle=AssetDatabase.LoadAssetAtPath<BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");
        battle.outOfCombatDelay=20;battle.botRespawnSeconds=6;battle.playerRespawnSeconds=5;EditorUtility.SetDirty(battle);
        var catalog=AssetDatabase.LoadAssetAtPath<MissionCatalog>("Assets/_Starframe/Settings/Game/Missions.asset");
        var demo=catalog.missions.First(m=>m);demo.baseBudget=10000;EditorUtility.SetDirty(demo);
        AssetDatabase.SaveAssets();
        foreach(string kind in new[]{"WeaponDefinition","ModuleDefinition","BattleTuning","MissionDefinition"})foreach(var guid in AssetDatabase.FindAssets("t:"+kind,new[]{"Assets/_Starframe/Settings"})){
            string path=AssetDatabase.GUIDToAssetPath(guid);File.WriteAllText(root+"/after/native/"+path.Replace('/','_')+".json",EditorJsonUtility.ToJson(AssetDatabase.LoadMainAssetAtPath(path),true));
        }
        return "Updated 14 weapons, "+count+" module recovery times, combat delays and demo budget=10000.";
    }
}
