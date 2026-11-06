using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;

public static class InstallWeaponBalanceVls5
{
    const string Modules="Assets/_Starframe/Settings/Modules/";
    const string Evidence="Documentation/WeaponVls5/";
    static WeaponDefinition Load(string name)=>AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Modules+name+".asset");
    static string Json(object value)
    {
        var type=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        return (string)type.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
    }
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before balancing native assets.");
        Directory.CreateDirectory(Evidence+"BalanceBefore");
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
        var all=AssetDatabase.FindAssets("t:WeaponDefinition",new[]{Modules.TrimEnd('/')})
            .Select(g=>AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        foreach(var asset in all.Cast<UnityEngine.Object>().Append(tuning))
        {
            string path=AssetDatabase.GetAssetPath(asset),backup=Evidence+"BalanceBefore/"+Path.GetFileName(path);
            if(!File.Exists(backup))File.Copy(path,backup);
        }
        // Explicit approved pre-change baselines make this installer repeatable without cumulative scaling.
        var rows=new[]{
            new {name="Gun1Weapon",oldRate=16f,oldDamage=540f,rate=12f,speed=2700f,gain=.6f},
            new {name="Gun2Weapon",oldRate=20f,oldDamage=765f,rate=24f,speed=2700f,gain=.8f},
            new {name="Gun3Weapon",oldRate=24f,oldDamage=1080f,rate=32f,speed=2700f,gain=1f},
            new {name="LaserRapidCannon",oldRate=8f,oldDamage=1260f,rate=10f,speed=6600f,gain=.6f},
            new {name="Laser2Weapon",oldRate=9f,oldDamage=2070f,rate=8f,speed=6600f,gain=.8f},
            new {name="Laser3Weapon",oldRate=9f,oldDamage=3420f,rate=6f,speed=6600f,gain=1f},
            new {name="Railgun1Weapon",oldRate=1.5f,oldDamage=10800f,rate=1.5f,speed=10800f,gain=.6f},
            new {name="Railgun2Weapon",oldRate=1.25f,oldDamage=18000f,rate=1f,speed=10800f,gain=.8f},
            new {name="Railgun3Weapon",oldRate=1f,oldDamage=31200f,rate=.75f,speed=10800f,gain=1f}
        };
        foreach(var row in rows)
        {
            var weapon=Load(row.name);if(!weapon)throw new InvalidOperationException("Missing "+row.name);
            weapon.shotsPerSecond=row.rate;weapon.damage=row.oldRate*row.oldDamage/row.rate;
            weapon.projectileSpeed=row.speed;weapon.shotAudioGain=row.gain;EditorUtility.SetDirty(weapon);
        }
        Load("Laser3Weapon").visualWidth=1.05f*1.2f;
        foreach(var name in new[]{"Missile1Weapon","Sword1Weapon"}){var w=Load(name);w.shotAudioGain=.6f;EditorUtility.SetDirty(w);}
        Load("Missile2Weapon").shotAudioGain=.8f;EditorUtility.SetDirty(Load("Missile2Weapon"));
        foreach(var name in new[]{"Missile3Weapon","Sword2Weapon"}){var w=Load(name);w.shotAudioGain=1;EditorUtility.SetDirty(w);}
        var detected=tuning.Get(GameSound.EnemyDetected);detected.gain=.4f*.6f;detected.pitch=.8f;detected.pitchVariation=0;
        var missile=tuning.Get(GameSound.MissileShot);missile.gain=.43f*.7f;missile.minimumInterval=.06f;
        // Both rail sizes use one waveform and one base level; the profile supplies size loudness.
        tuning.Get(GameSound.RailSmall).gain=tuning.Get(GameSound.RailLarge).gain=.6f;
        EditorUtility.SetDirty(tuning);AssetDatabase.SaveAssets();
        var report=new{
            compilationFailed=EditorUtility.scriptCompilationFailed,
            weapons=rows.Select(r=>{var w=Load(r.name);return new{w.name,w.shotsPerSecond,w.damage,w.projectileSpeed,w.shotAudioGain,previousDps=r.oldRate*r.oldDamage,currentDps=w.shotsPerSecond*w.damage,w.visualWidth};}).ToArray(),
            enemyDetected=new{detected.gain,detected.pitch},missile=new{missile.gain,missile.minimumInterval}
        };
        File.WriteAllText(Evidence+"balance-installed.json",Json(report));return report;
    }
}
