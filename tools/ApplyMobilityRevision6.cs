using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Starframe.Flight;
using Starframe.Combat;
public static class ApplyMobilityRevision6
{
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required.");
        const string folder="Documentation/FleetRevision6/";Directory.CreateDirectory(folder+"BalanceBefore");
        var flight=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
        var damage=AssetDatabase.LoadAssetAtPath<CombatDamageTuning>("Assets/_Starframe/Settings/Combat/CombatDamageTuning.asset");
        foreach(var asset in new UnityEngine.Object[]{flight,damage})
        {string p=AssetDatabase.GetAssetPath(asset),backup=folder+"BalanceBefore/"+Path.GetFileName(p);if(!File.Exists(backup))File.Copy(p,backup);}
        flight.thrustMultiplier=1;flight.blockMassMultiplier=1;flight.heavyMassThresholdKg=100000;
        flight.heavySpeedExponent=.65f;flight.heavyManeuverExponent=.35f;flight.heavyMinimumSpeedFraction=.35f;flight.heavyMinimumManeuverFraction=.45f;
        damage.globalDamageMultiplier=1;
        foreach(var name in new[]{"VLS1Weapon","VLS2Weapon"})
        {var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+name+".asset");string p=AssetDatabase.GetAssetPath(weapon);string backup=folder+"BalanceBefore/"+name+".asset";if(!File.Exists(backup))File.Copy(p,backup);
            weapon.projectileSpeed=196;weapon.missileLaunchSpeed=24.5f;EditorUtility.SetDirty(weapon);}
        EditorUtility.SetDirty(flight);EditorUtility.SetDirty(damage);AssetDatabase.SaveAssets();
        var catalog=Resources.Load<EnemyFleetCatalog>("Enemies/FleetTemplates");
        var report=new{playing=Application.isPlaying,damage.globalDamageMultiplier,flight.thrustMultiplier,flight.blockMassMultiplier,flight.heavyMassThresholdKg,
            ships=catalog.enemies.Where(e=>e).Select(e=>{var data=e.CreateBlueprint();var states=data.parts.Select(record=>new ShipPartState{record=record,hp=flight.Get(record.shape).maxHp});var p=ShipPerformance.Calculate(states,data.parts.First(r=>r.shape==Starframe.Construction.BlockShape.Core).rotation,flight);return new{e.id,p.totalMass,p.maxSpeed,p.massManeuverScale};}).ToArray()};
        var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        File.WriteAllText(folder+"mobility-installed.json",(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report}));return report;
    }
}
