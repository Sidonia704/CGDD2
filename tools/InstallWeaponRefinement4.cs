using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;
public static class InstallWeaponRefinement4
{
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play first.");
        const string path="Assets/_Starframe/Audio/116/RailShared.wav";
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer=(AudioImporter)AssetImporter.GetAtPath(path);var s=importer.defaultSampleSettings;
        s.loadType=AudioClipLoadType.DecompressOnLoad;s.compressionFormat=AudioCompressionFormat.PCM;
        s.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;s.preloadAudioData=true;
        importer.defaultSampleSettings=s;importer.forceToMono=true;importer.loadInBackground=false;importer.SaveAndReimport();
        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);var samples=new float[clip.samples*clip.channels];
        if(!clip.GetData(samples,0))throw new InvalidOperationException("Audio PCM unavailable.");
        int peak=0;for(int i=1;i<samples.Length;i++)if(Mathf.Abs(samples[i])>Mathf.Abs(samples[peak]))peak=i;
        float delay=peak/(float)(clip.frequency*clip.channels);
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
        foreach(var sound in new[]{GameSound.RailLarge,GameSound.RailSmall})
        {var cue=tuning.Get(sound);cue.clip=clip;cue.alternateClip=null;cue.variants=Array.Empty<AudioClip>();cue.pitch=1;cue.pitchVariation=0;}
        EditorUtility.SetDirty(tuning);
        var weapons=AssetDatabase.FindAssets("t:WeaponDefinition",new[]{"Assets/_Starframe/Settings/Modules"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        foreach(var w in weapons)
        {
            if(w.family==WeaponFamily.Railgun){w.fireWindupSeconds=delay;EditorUtility.SetDirty(w);}
            if(w.family==WeaponFamily.Gatling){w.gatlingHeatPerSecond=1f/8f+1f/6f;w.gatlingCoolingPerSecond=1f/6f;EditorUtility.SetDirty(w);}
        }
        AssetDatabase.SaveAssets();
        foreach(string old in new[]{"RailLarge","RailSmallA","RailSmallB"})AssetDatabase.DeleteAsset("Assets/_Starframe/Audio/116/"+old+".wav");
        var report=new{compilationFailed=EditorUtility.scriptCompilationFailed,clip=clip.name,clip.samples,clip.length,peakSeconds=delay,
            weapons=weapons.Where(w=>w.family==WeaponFamily.Railgun||w.family==WeaponFamily.Gatling)
            .Select(w=>new{w.name,w.fireWindupSeconds,w.gatlingHeatPerSecond,w.gatlingCoolingPerSecond}).ToArray()};
        var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        File.WriteAllText("Documentation/WeaponRefinement4/installed.json",(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report}));
        return report;
    }
}
