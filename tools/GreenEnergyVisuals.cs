using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.Flight;
public static class GreenEnergyVisuals
{
    public static object Run()
    {
        var changed=new List<string>();
        foreach(var guid in AssetDatabase.FindAssets("t:WeaponDefinition",new[]{"Assets/_Starframe"}))
        {
            var path=AssetDatabase.GUIDToAssetPath(guid);var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if(weapon.family!=WeaponFamily.RapidLaser&&weapon.family!=WeaponFamily.BeamLaser&&weapon.delivery!=WeaponDelivery.Beam)continue;
            Undo.RecordObject(weapon,"Green energy weapon effects");weapon.effectColor=new Color(.08f,1f,.24f,1f);
            EditorUtility.SetDirty(weapon);AssetDatabase.SaveAssetIfDirty(weapon);changed.Add(path);
        }
        return new{changed};
    }
}
