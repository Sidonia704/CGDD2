using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

public static class InstallMissionCopy
{
    public static string Main()
    {
        if(UnityEditor.EditorApplication.isPlaying)throw new InvalidOperationException("Install mission copy outside Play mode.");
        var collection=LocalizationEditorSettings.GetStringTableCollection("GameUI");
        if(collection==null)throw new InvalidOperationException("GameUI collection is missing.");
        int count=0;
        foreach(string line in File.ReadAllLines("Assets/_Starframe/Localization/GameUI.csv")){
            var fields=Regex.Matches(line,"\"((?:[^\"]|\"\")*)\"").Cast<Match>().Select(m=>m.Groups[1].Value.Replace("\"\"","\"")).ToArray();
            if(fields.Length<3||(!fields[0].StartsWith("mission.arcade.",StringComparison.Ordinal)&&!fields[0].StartsWith("mission.ice.",StringComparison.Ordinal)))continue;
            foreach(string locale in new[]{"zh-Hans","en"}){
                var table=collection.GetTable(locale) as StringTable;
                if(!table)throw new InvalidOperationException("Missing locale table: "+locale);
                string value=fields[locale=="en"?2:1].Replace("\\n","\n");
                var entry=table.GetEntry(fields[0])??table.AddEntry(fields[0],value);entry.Value=value;entry.IsSmart=false;EditorUtility.SetDirty(table);count++;
            }
        }
        EditorUtility.SetDirty(collection);EditorUtility.SetDirty(collection.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(InstallMissionCopy),collection);
        AssetDatabase.SaveAssets();return "Installed "+count+" bilingual arcade mission strings.";
    }
}
