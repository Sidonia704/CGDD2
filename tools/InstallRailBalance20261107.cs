using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;

public static class InstallRailBalance20261107
{
    const string Modules="Assets/_Starframe/Settings/Modules/";
    const string Evidence="Documentation/RailBalance-2026-11-07/";
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before balancing native assets.");
        string[] names={"Railgun1Weapon","Railgun2Weapon","Railgun3Weapon"};
        float[] damage={10800f,22500f,41600f},depth={2f,3f,4f},oldRetained={.35f,.35f,.4f},retained={.5f,.45f,.45f};
        var weapons=names.Select(name=>AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Modules+name+".asset")).ToArray();
        // Validate the full batch before mutation; do not overwrite an intervening balance edit.
        for(int i=0;i<weapons.Length;i++)
        {
            var w=weapons[i];
            bool original=w&&Mathf.Approximately(w.damage,damage[i])&&Mathf.Approximately(w.penetrationDepth,depth[i])&&Mathf.Approximately(w.secondLayerDamageMultiplier,oldRetained[i]);
            bool installed=w&&Mathf.Approximately(w.damage,damage[i]*.8f)&&Mathf.Approximately(w.penetrationDepth,depth[i]*2)&&Mathf.Approximately(w.secondLayerDamageMultiplier,retained[i]);
            if(!original&&!installed)throw new InvalidOperationException("Unexpected rail baseline: "+names[i]);
        }
        Directory.CreateDirectory(Evidence+"Before");
        for(int i=0;i<weapons.Length;i++)
        {
            string source=Modules+names[i]+".asset",backup=Evidence+"Before/"+names[i]+".asset";
            if(!File.Exists(backup))
            {
                if(!Mathf.Approximately(weapons[i].damage,damage[i])||!Mathf.Approximately(weapons[i].penetrationDepth,depth[i])||!Mathf.Approximately(weapons[i].secondLayerDamageMultiplier,oldRetained[i]))
                    throw new InvalidOperationException("Missing original baseline backup: "+names[i]);
                File.Copy(source,backup);
            }
        }
        for(int i=0;i<weapons.Length;i++)
        {
            weapons[i].damage=damage[i]*.8f;
            weapons[i].penetrationDepth=depth[i]*2;
            weapons[i].secondLayerDamageMultiplier=retained[i];
            EditorUtility.SetDirty(weapons[i]);
        }
        AssetDatabase.SaveAssets();
        var report=new {scope="Rail base damage -20%, doubled penetration, increased retained fraction; approximately 150% original aggregate damage on thick aligned unit-cube hulls; shared by both factions", baseDamageMultiplier=.8f,depthMultiplier=2f,
            weapons=weapons.Select((w,i)=>new {w.name,previousDamage=damage[i],w.damage,w.shotsPerSecond,rawDps=w.damage*w.shotsPerSecond,previousDepth=depth[i],w.penetrationDepth,previousSecondLayerMultiplier=oldRetained[i],w.secondLayerDamageMultiplier,previousThickHullBudget=damage[i]*(1+(depth[i]-1)*oldRetained[i]),currentThickHullBudget=w.damage*(1+(w.penetrationDepth-1)*w.secondLayerDamageMultiplier),w.projectileSpeed,w.fireWindupSeconds}).ToArray()};
        var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        File.WriteAllText(Evidence+"installed.json",(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report}));
        return report;
    }
}
