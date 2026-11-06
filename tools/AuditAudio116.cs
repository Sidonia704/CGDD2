using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
public static class AuditAudio116
{
    public static object Run()
    {
        var t=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
        var clips=AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/_Starframe/Audio/116"}).Select(g=>{
            string p=AssetDatabase.GUIDToAssetPath(g);var c=AssetDatabase.LoadAssetAtPath<AudioClip>(p);var a=(AudioImporter)AssetImporter.GetAtPath(p);
            return new{name=c.name,path=p,c.length,c.channels,c.frequency,loadType=a.defaultSampleSettings.loadType.ToString(),format=a.defaultSampleSettings.compressionFormat.ToString()};
        }).ToArray();
        if(clips.Length!=16)throw new InvalidOperationException("Expected 16 imported clips");
        foreach(var c in clips)if(c.length<=0||c.channels!=(c.name=="Music"?2:1))throw new InvalidOperationException("Invalid clip "+c.name);
        var report=new{clips,cues=t.cues.Select(c=>new{sound=c.sound.ToString(),clip=c.clip?AssetDatabase.GetAssetPath(c.clip):null,
            alternate=c.alternateClip?AssetDatabase.GetAssetPath(c.alternateClip):null,c.gain,c.pitch,c.minimumInterval,c.loopWhileFiring,c.releaseSeconds}),
            music=new{menu=AssetDatabase.GetAssetPath(t.workbenchMusic),flight=AssetDatabase.GetAssetPath(t.flightMusic),t.musicGain},
            engine=t.thrusterLoop?"enabled":"disabled",playing=Application.isPlaying,compilationFailed=EditorUtility.scriptCompilationFailed};
        var jsonType=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        string json=(string)jsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report});
        File.WriteAllText("Documentation/Audio116-2026-11-06/installed.json",json);
        TraceDocsExporter.ExportForAutomation();
        return report;
    }
}
