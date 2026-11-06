using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;
public static class AuditWeaponVls5
{
    public static object Run()
    {
        var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var audio=Resources.Load<GameAudioTuning>("Audio/GameAudioTuning");
        var report=new{playing=Application.isPlaying,compilationFailed=EditorUtility.scriptCompilationFailed,
            modules=db.modules.Where(m=>m&&m.weapon).Select(m=>new{m.name,shape=m.shape.ToString(),cells=m.occupiedCells.Length,m.cost,m.massKg,
                m.weapon.shotsPerSecond,m.weapon.damage,m.weapon.projectileSpeed,m.weapon.shotAudioGain,m.weapon.magazineSize,m.weapon.salvoLaunchesPerSecond,
                m.weapon.missileLockSeconds,m.weapon.reloadSeconds,m.weapon.missileTurnDegrees,m.weapon.missileGuidanceSeconds,m.weapon.range,m.weapon.explosionRadius}).ToArray(),
            cues=audio.cues.Select(c=>new{sound=c.sound.ToString(),c.gain,c.pitch,c.minimumInterval}).ToArray()};
        var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        File.WriteAllText("Documentation/WeaponVls5/runtime-audit.json",(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report}));
        TraceDocsExporter.ExportForAutomation();
        return new{playing=Application.isPlaying,compilationFailed=EditorUtility.scriptCompilationFailed,weapons=report.modules.Length};
    }
}
