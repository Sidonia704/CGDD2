using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;
public static class InstallAudio116Revision2
{
    const string Root="Assets/_Starframe/Audio/116/";
    static AudioClip Clip(string key)=>AssetDatabase.LoadAssetAtPath<AudioClip>(Root+key+".wav");
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string path in Directory.GetFiles(Root,"*.wav"))
        {
            if(Path.GetFileName(path)=="Music.wav")continue;
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);var s=importer.defaultSampleSettings;
            s.loadType=AudioClipLoadType.DecompressOnLoad;s.compressionFormat=AudioCompressionFormat.PCM;s.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;s.preloadAudioData=true;
            importer.defaultSampleSettings=s;importer.forceToMono=true;importer.loadInBackground=false;importer.SaveAndReimport();
        }
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
        var rapid=tuning.Get(GameSound.LaserRapid);rapid.clip=Clip("LaserRapid");rapid.loopWhileFiring=false;rapid.minimumInterval=.07f;
        tuning.Get(GameSound.DirectLaser).clip=Clip("DirectLaser");tuning.Get(GameSound.DirectLaser).loopWhileFiring=true;
        tuning.Get(GameSound.MissileShot).clip=Clip("MissileShot");
        var flare=tuning.Get(GameSound.FlareDeploy);flare.clip=Clip("FlareDeploy");
        flare.variants=new[]{"FlareDeploy","Flare02","Flare03","Flare04","Flare05","Flare06"}.Select(Clip).ToArray();
        if(flare.variants.Any(c=>!c))throw new InvalidOperationException("Missing flare unit.");
        flare.alternateClip=null;flare.loopWhileFiring=false;flare.minimumInterval=.05f;
        var alarm=tuning.Get(GameSound.MissileIncoming);alarm.clip=Clip("MissileIncoming");alarm.minimumInterval=.08f;
        if(alarm.clip.length>=tuning.missileWarningNearInterval)throw new InvalidOperationException("Alarm unit must fit the original fastest pulse cadence.");
        void Ui(GameSound sound,string key,float gain)
        {
            var cue=tuning.Get(sound);cue.clip=Clip(key);cue.gain=gain;cue.pitch=1;cue.pitchVariation=0;
            cue.bus=GameSoundBus.Interface;cue.loopWhileFiring=false;cue.minimumInterval=.08f;
        }
        Ui(GameSound.RotatePart,"SystemUIA",.30f);Ui(GameSound.InvalidPlacement,"SystemUIB",.45f);Ui(GameSound.CorePowerOn,"SystemUIB",.45f);
        var guns=AssetDatabase.FindAssets("t:WeaponDefinition",new[]{"Assets/_Starframe/Settings/Modules"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(w=>w.family==WeaponFamily.Gatling).ToArray();
        foreach(var gun in guns){gun.gatlingHeatPerSecond=.25f;gun.gatlingCoolingPerSecond=1f/6f;EditorUtility.SetDirty(gun);}
        EditorUtility.SetDirty(tuning);AssetDatabase.SaveAssets();
        var report=new{guns=guns.Select(g=>new{g.name,g.gatlingHeatPerSecond,g.gatlingCoolingPerSecond,fireSeconds=1/(g.gatlingHeatPerSecond-g.gatlingCoolingPerSecond),coolSeconds=1/g.gatlingCoolingPerSecond}),
            flareUnits=flare.variants.Select(c=>new{c.name,c.length}),missilePulse=alarm.clip.length,nearInterval=tuning.missileWarningNearInterval,
            farInterval=tuning.missileWarningFarInterval,rapidLoop=rapid.loopWhileFiring,
            cues=tuning.cues.Select(c=>new{sound=c.sound.ToString(),clip=c.clip?AssetDatabase.GetAssetPath(c.clip):null,c.gain,c.loopWhileFiring})};
        var type=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        File.WriteAllText("Documentation/Audio116-Revision2/installed.json",(string)type.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report}));
        return report;
    }
}
