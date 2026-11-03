using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

public static class BalanceLocalization
{
    public static string Apply()
    {
        var keys=new[]{"module.stat.cost","editor.assemblyCost","app.rangePowerSummary","app.rangeWarnings","app.continueRange","module.stat.lock"};
        var collection=LocalizationEditorSettings.GetStringTableCollection("GameUI");int count=0;
        if(collection==null)throw new InvalidOperationException("GameUI missing");
        foreach(string line in File.ReadAllLines("Assets/_Starframe/Localization/GameUI.csv")){
            var fields=Regex.Matches(line,"\"((?:[^\"]|\"\")*)\"").Cast<Match>().Select(m=>m.Groups[1].Value.Replace("\"\"","\"")).ToArray();
            if(fields.Length<3||!keys.Contains(fields[0]))continue;
            foreach(string locale in new[]{"zh-Hans","en"}){
                var table=collection.GetTable(locale) as StringTable;if(!table)throw new InvalidOperationException("Missing locale "+locale);
                table.AddEntry(fields[0],locale=="en"?fields[2]:fields[1]);EditorUtility.SetDirty(table);count++;
            }
        }
        EditorUtility.SetDirty(collection.SharedData);AssetDatabase.SaveAssets();return "Updated "+count+" localized entries without rebuilding fonts.";
    }
}
