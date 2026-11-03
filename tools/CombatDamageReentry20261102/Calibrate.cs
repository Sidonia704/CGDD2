using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;
public static class CalibrateSaturation
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stopped editor required.");
        var weapons=AssetDatabase.FindAssets("t:WeaponDefinition",new[]{"Assets/_Starframe/Settings/Modules"}).Select(g=>AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        float[] damage=weapons.Select(w=>w.damage).ToArray();
        var probe=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("SaturationProbe")).Last(t=>t!=null).GetMethod("Run");
        string output="";string root="Documentation/CombatDamageReentry-2026-11-02/Saturation/FinalWindow";Directory.CreateDirectory(root);
        try {
            foreach(float scale in new[]{1f,1.3333333f,1.6666666f}){
                for(int i=0;i<weapons.Length;i++)weapons[i].damage=damage[i]*scale;
                foreach(string profile in new[]{"Maneuver","Assault","Heavy"}){
                    string path="Documentation/Battleship-2026-11-02/Blueprints/Battleship-"+profile+".json";
                    string json=(string)probe.Invoke(null,new object[]{path,path,12f,600f,12f,0f,0f,true,0f,261102,true,180f,0f,.25f});
                    File.WriteAllText(root+"/"+profile+"-scale"+scale+".json",json);
                    string death=Regex.Match(json,"\"coreDeathAt\"\\s*:\\s*([-0-9.Ee]+)").Groups[1].Value;
                    output+=profile+" scale="+scale+" coreDeathAt="+death+"; ";
                }
            }
        }finally{for(int i=0;i<weapons.Length;i++)weapons[i].damage=damage[i];}
        return output+"Temporary in-memory scales restored; no authoring assets saved.";
    }
}