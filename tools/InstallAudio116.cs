using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;
public static class InstallAudio116
{
    const string Root="Assets/_Starframe/Audio/116/";
    const string Tuning="Assets/_Starframe/Resources/Audio/GameAudioTuning.asset";
    static AudioClip Clip(string key)=>AssetDatabase.LoadAssetAtPath<AudioClip>(Root+key+".wav");
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before installing audio.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string path in Directory.GetFiles(Root,"*.wav"))
        {
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);
            bool music=Path.GetFileNameWithoutExtension(path)=="Music";
            var settings=importer.defaultSampleSettings;
            settings.loadType=music?AudioClipLoadType.Streaming:AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=music?AudioCompressionFormat.Vorbis:AudioCompressionFormat.PCM;
            settings.quality=.9f;settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;settings.preloadAudioData=!music;
            importer.defaultSampleSettings=settings;importer.forceToMono=!music;importer.loadInBackground=music;importer.SaveAndReimport();
        }
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>(Tuning);
        int[] retained={0};
        // Fixed baseline values make installation idempotent: no repeated 30% reduction.
        float[] baseline={.42f};
        var cues=Enum.GetValues(typeof(GameSound)).Cast<GameSound>().Select(sound=>tuning.Get(sound)??new GameSoundCue{sound=sound}).ToArray();
        foreach(var cue in cues)
        {
            int keep=Array.IndexOf(retained,(int)cue.sound);
            if(keep>=0){cue.gain=baseline[keep]*.7f;continue;}
            if(!new[]{6,7,8,10}.Contains((int)cue.sound))continue;
            cue.clip=null;cue.alternateClip=null;cue.gain=0;cue.loopWhileFiring=false;
        }
        void Set(GameSound sound,string file,float gain,int priority=80,bool loop=false,float release=.2f,float interval=.08f)
        {
            var cue=cues.Single(c=>c.sound==sound);cue.clip=Clip(file);
            if(!cue.clip)throw new InvalidOperationException("Missing processed clip "+file);
            cue.gain=gain;cue.pitch=1;cue.pitchVariation=0;cue.minimumInterval=interval;cue.priority=priority;cue.bus=GameSoundBus.Effects;
            cue.loopWhileFiring=loop;cue.releaseSeconds=release;
        }
        Set(GameSound.LaserRapid,"LaserRapid",.45f,112,true);
        Set(GameSound.DirectLaser,"DirectLaser",.43f,112,true);
        Set(GameSound.Gatling,"Gatling",.44f,112,true,.28f);
        Set(GameSound.RailLarge,"RailLarge",.60f,100,false,interval:.10f);
        Set(GameSound.RailSmall,"RailSmallA",.52f,104,false,interval:.08f);
        cues.Single(c=>c.sound==GameSound.RailSmall).alternateClip=Clip("RailSmallB");
        Set(GameSound.MissileShot,"MissileShot",.43f,110,false,interval:.11f);
        Set(GameSound.FlareDeploy,"FlareDeploy",.48f,75);
        Set(GameSound.LockAcquired,"LockAcquired",.5f,48,false,interval:.3f);
        Set(GameSound.TargetLost,"TargetLost",.45f,48,false,interval:.3f);
        Set(GameSound.EnemyDetected,"EnemyDetected",.4f,48,false,interval:.3f);
        Set(GameSound.EnemyLockWarning,"EnemyLockWarning",.5f,28,false,interval:.75f);
        Set(GameSound.MissileIncoming,"MissileIncoming",.55f,20,false,interval:.08f);
        Set(GameSound.HealthShieldWarning,"HealthShieldWarning",.5f,30,false,interval:3f);
        Set(GameSound.OverheatWarning,"OverheatWarning",.45f,35,false,interval:1f);
        tuning.cues=cues;tuning.workbenchMusic=tuning.flightMusic=Clip("Music");tuning.musicGain=.3f;
        EditorUtility.SetDirty(tuning);AssetDatabase.SaveAssets();
        var kept=cues.Where(c=>c.clip).Select(c=>AssetDatabase.GetAssetPath(c.clip)).ToHashSet();
        string[] old=Directory.GetFiles("Assets/_Starframe/Audio/Clips","*.wav").Select(p=>p.Replace('\\','/')).Where(p=>new[]{"Rotate_Servo.wav","Invalid_Placement.wav","Core_Power_On.wav","Laser_Impact_Metal.wav"}.Contains(Path.GetFileName(p))).ToArray();
        string backup="C:/Users/34808/.codex/visualizations/2026/11/04/01a1a7d9-fe76-7391-a1e5-cb26a9b2f175/Audio116-original-backup/Clips/";
        foreach(string path in old)
        {
            var saved=backup+Path.GetFileName(path);
            if(!File.Exists(saved)||!File.ReadAllBytes(saved).SequenceEqual(File.ReadAllBytes(path)))throw new InvalidOperationException("Backup mismatch: "+path);
        }
        foreach(string path in old)if(!AssetDatabase.DeleteAsset(path))throw new IOException("Cannot remove retired clip "+path);
        AssetDatabase.SaveAssets();
        return new{deleted=old,active=cues.Where(c=>c.clip&&c.gain>0).Select(c=>new{sound=c.sound.ToString(),clip=c.clip.name,c.gain,c.loopWhileFiring}),
            retained=cues.Where(c=>retained.Contains((int)c.sound)).Select(c=>new{sound=c.sound.ToString(),c.gain}),
            music=tuning.flightMusic.name,engineDisabled=tuning.thrusterLoop==null};
    }
}
