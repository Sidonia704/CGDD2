using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;

public static class InstallAudioEnvironment
{
    static readonly string[] Clips={"Flare_Deploy","Enemy_Detected","Enemy_Lock_Warning","UI_Select","Blueprint_Save","Warning_Confirm","Rotate_Servo","Core_Power_On","Lock_Acquired","Boundary_Warning","Respawn_Reboot"};
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop owned Play validation before installing audio.");
        const string root="Assets/_Starframe/Audio/Clips/";
        foreach(string name in Clips)
        {
            string path=root+name+".wav";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);
            var settings=importer.defaultSampleSettings;
            settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;
            settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;settings.preloadAudioData=true;
            importer.defaultSampleSettings=settings;importer.forceToMono=true;importer.loadInBackground=false;importer.SaveAndReimport();
        }
        var audio=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
        if(!audio)throw new InvalidOperationException("Missing live audio tuning");
        var flare=audio.Get(GameSound.FlareDeploy);
        if(flare==null)throw new InvalidOperationException("Missing actual flare cue");
        flare.gain=.42f;flare.pitch=1;flare.pitchVariation=.015f;flare.minimumInterval=.12f;
        // Losing lock remains the same mechanical confirmation played lower, as requested previously.
        var locked=audio.Get(GameSound.LockAcquired);var lost=audio.Get(GameSound.TargetLost);
        if(locked!=null&&lost!=null){lost.clip=locked.clip;lost.pitch=.75f;lost.pitchVariation=0;}
        EditorUtility.SetDirty(audio);
        foreach(var path in new[]{"Assets/_Starframe/Art/SimulationTrial/SimulationAtmosphere.asset","Assets/_Starframe/Settings/Rendering/RangeAtmosphereTuning.asset"})
        {
            var atmosphere=AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>(path);if(!atmosphere)continue;
            string backup="Documentation/SessionNov03/audio-environment/before/"+Path.GetFileName(path)+".txt";
            if(!File.Exists(backup))File.Copy(path,backup);
            atmosphere.starDirection=new Vector3(.62f,.52f,.58f);EditorUtility.SetDirty(atmosphere);
        }
        AssetDatabase.SaveAssets();
        return new{clips=Clips.Length,flareGain=flare.gain,starDirection=new Vector3(.62f,.52f,.58f),noSceneLoad=true};
    }
}
