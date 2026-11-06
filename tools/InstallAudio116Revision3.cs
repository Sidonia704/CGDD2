using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;
public static class InstallAudio116Revision3
{
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop play first.");
        const string root="Assets/_Starframe/Audio/116/";
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(var name in new[]{"Gatling","GatlingStart","GatlingEnd","RailLarge","RailSmallA","RailSmallB"})
        {
            var importer=(AudioImporter)AssetImporter.GetAtPath(root+name+".wav");var s=importer.defaultSampleSettings;
            s.loadType=AudioClipLoadType.DecompressOnLoad;s.compressionFormat=AudioCompressionFormat.PCM;
            s.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;s.preloadAudioData=true;
            importer.defaultSampleSettings=s;importer.forceToMono=true;importer.loadInBackground=false;importer.SaveAndReimport();
        }
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
        var cue=tuning.Get(GameSound.Gatling);
        cue.startClip=AssetDatabase.LoadAssetAtPath<AudioClip>(root+"GatlingStart.wav");
        cue.endClip=AssetDatabase.LoadAssetAtPath<AudioClip>(root+"GatlingEnd.wav");
        cue.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(root+"Gatling.wav");
        cue.pitch=1;cue.pitchVariation=0;cue.loopWhileFiring=true;
        foreach(var sound in new[]{GameSound.RailLarge,GameSound.RailSmall}){var c=tuning.Get(sound);c.pitch=1;c.pitchVariation=0;}
        EditorUtility.SetDirty(tuning);
        var weapons=AssetDatabase.FindAssets("t:WeaponDefinition",new[]{"Assets/_Starframe/Settings/Modules"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        foreach(var w in weapons)
        {
            if(w.family==WeaponFamily.Railgun)w.fireWindupSeconds=.08f;
            // Explicit baseline makes this installer safe to repeat without multiplying damage again.
            if(w.name=="Sword1Weapon")w.damage=4500;
            if(w.name=="Sword2Weapon")w.damage=8100;
            if(w.family==WeaponFamily.Railgun||w.family==WeaponFamily.BeamLaser)EditorUtility.SetDirty(w);
        }
        AssetDatabase.SaveAssets();
        var report=new{playing=Application.isPlaying,compilationFailed=EditorUtility.scriptCompilationFailed,
            gatling=new{start=cue.startClip.length,sustain=cue.clip.length,end=cue.endClip.length},
            weapons=weapons.Where(w=>w.family==WeaponFamily.Railgun||w.family==WeaponFamily.BeamLaser||w.family==WeaponFamily.Gatling)
                .Select(w=>new{w.name,w.damage,w.fireWindupSeconds,w.gatlingHeatPerSecond,w.gatlingCoolingPerSecond}).ToArray()};
        var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        File.WriteAllText("Documentation/Audio116-Revision3/installed.json",(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report}));
        return report;
    }
}
