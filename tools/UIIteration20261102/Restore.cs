using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Starframe.Presentation;

// Run through Pipeline run_script with args ["frontend"|"assembly"|"combat"|"glass",false].
// Preview is the default. Never overwrite later edits: every selected source must match the final snapshot.
public static class UIIterationRestore
{
    [Serializable] public sealed class Entry{public string group,path,before,beforeSha256,afterSha256;}
    [Serializable] public sealed class Manifest{public Entry[] entries;}
    public static string Main(string group,bool apply=false)
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode before restoring interface sources.");
        if(!new[]{"frontend","assembly","combat","glass"}.Contains(group))throw new ArgumentException("Unknown UI module.");
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,".."))+Path.DirectorySeparatorChar;
        // JsonUtility cannot deserialize nested DTOs from ephemeral run_script assemblies.
        var jsonType=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var entries=(Entry[])jsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{File.ReadAllText(root+"Documentation/UIIteration-2026-11-02/source-manifest.json"),typeof(Entry[])});
        // Navigation IDs/return contracts are retained so newer independent pages still compile.
        var selected=entries.Where(e=>e.path.EndsWith("ShipWorkspace.cs",StringComparison.Ordinal)?false:e.group==group||(group=="glass"&&e.group=="shared"&&new[]{"UiTheme.cs","GlassUiPalette.cs","UIBackdropBlur.shader"}.Contains(Path.GetFileName(e.path)))).ToArray();
        foreach(var entry in selected)
        {
            foreach(string relative in new[]{entry.path,entry.before})
                if(!Path.GetFullPath(root+relative).StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Path escaped project.");
            if(Hash(root+entry.before)!=entry.beforeSha256)throw new InvalidDataException("Original snapshot changed: "+entry.before);
            if(Hash(root+entry.path)!=entry.afterSha256)throw new InvalidOperationException("Later source edits detected; compare manually before restoring: "+entry.path);
        }
        if(!apply)return "Preview only: "+string.Join("\n",selected.Select(e=>e.path));
        foreach(var entry in selected)File.Copy(root+entry.before,root+entry.path,true);
        if(group=="glass")
        {
            JsonUtility.FromJsonOverwrite(File.ReadAllText(root+"Documentation/UIIteration-2026-11-02/theme-before.json"),UiTheme.Current);
            EditorUtility.SetDirty(UiTheme.Current);AssetDatabase.SaveAssets();
        }
        AssetDatabase.Refresh();return "Restored "+selected.Length+" files in "+group+"; user blueprints and gameplay values were untouched.";
    }
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
}
