using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

public static class BattleshipLocalization
{
    public static string Apply()
    {
        var keys=new[]{"module.stat.freeHull","editor.shipInfo","editor.assemblyCost",
            "editor.performance.title","editor.performance.speed","editor.performance.forwardBack",
            "editor.performance.leftRight","editor.performance.upDown","editor.performance.stopping",
            "editor.performance.stoppingOther","editor.performance.turnRate","editor.performance.turnRadius","editor.performance.infinite"};
        var values=new Dictionary<string,string[]>(StringComparer.Ordinal);
        foreach(string line in File.ReadAllLines("Assets/_Starframe/Localization/GameUI.csv")){
            var fields=Regex.Matches(line,"\"((?:[^\"]|\"\")*)\"").Cast<Match>().Select(m=>m.Groups[1].Value.Replace("\"\"","\"")).ToArray();
            if(fields.Length<3||!keys.Contains(fields[0]))continue;
            if(values.ContainsKey(fields[0]))throw new InvalidDataException("Duplicate localization key: "+fields[0]);
            if(string.IsNullOrWhiteSpace(fields[1])||string.IsNullOrWhiteSpace(fields[2]))throw new InvalidDataException("Empty translation: "+fields[0]);
            values.Add(fields[0],fields);
        }
        foreach(var key in keys)if(!values.ContainsKey(key))throw new InvalidDataException("Missing CSV key: "+key);
        var collection=LocalizationEditorSettings.GetStringTableCollection("GameUI");
        if(collection==null)throw new InvalidOperationException("GameUI missing");
        var tables=new[]{collection.GetTable("zh-Hans") as StringTable,collection.GetTable("en") as StringTable};
        if(tables.Any(table=>!table))throw new InvalidOperationException("Missing Chinese or English GameUI table");
        int count=0;
        for(int locale=0;locale<tables.Length;locale++){
            var table=tables[locale];
            foreach(var key in keys){
                string value=values[key][locale+1].Replace("\\n","\n");
                var entry=table.GetEntry(key)??table.AddEntry(key,value);entry.Value=value;entry.IsSmart=false;count++;
            }
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(collection);EditorUtility.SetDirty(collection.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(BattleshipLocalization),collection);
        AssetDatabase.SaveAssets();return "Updated "+count+" battleship localized entries (13 keys / 2 locales), without rebuilding fonts.";
    }
}
