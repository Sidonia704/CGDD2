using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;
using Starframe.Presentation;

public static class UIIterationInstall
{
    public static string Main()
    {
        var theme=UiTheme.Current;
        theme.glassTint=new Color(.075f,.09f,.105f,.68f);
        theme.dialogTint=new Color(.055f,.065f,.075f,.86f);
        theme.buttonTint=new Color(.12f,.15f,.16f,.82f);
        theme.blurRadius=28;theme.sheenStrength=0;EditorUtility.SetDirty(theme);
        var keys=File.ReadAllLines("Documentation/UIIteration-2026-11-02/copy-keys.txt").Where(k=>!string.IsNullOrWhiteSpace(k)).ToHashSet();
        var collection=LocalizationEditorSettings.GetStringTableCollection("GameUI");
        int count=0;
        foreach(string line in File.ReadAllLines("Assets/_Starframe/Localization/GameUI.csv"))
        {
            var fields=Regex.Matches(line,"\"((?:[^\"]|\"\")*)\"").Cast<Match>().Select(m=>m.Groups[1].Value.Replace("\"\"","\"")).ToArray();
            if(fields.Length<3||!keys.Contains(fields[0]))continue;
            foreach(var locale in new[]{"zh-Hans","en"})
            {
                var table=collection.GetTable(locale) as StringTable;
                string value=fields[locale=="en"?2:1].Replace("\\n","\n");
                var entry=table.GetEntry(fields[0])??table.AddEntry(fields[0],value);
                entry.Value=value;entry.IsSmart=false;EditorUtility.SetDirty(table);count++;
            }
        }
        EditorUtility.SetDirty(collection);EditorUtility.SetDirty(collection.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(UIIterationInstall),collection);
        AssetDatabase.SaveAssets();return "Updated theme and "+count+" bilingual entries.";
    }
}
