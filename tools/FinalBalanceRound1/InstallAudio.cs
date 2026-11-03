using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Starframe.App;

/// <summary>Run through Unity CLI execute-code with this file; installs only the incoming missile cue.</summary>
public static class FinalBalanceRound1Audio
{
    const string ClipPath="Assets/_Starframe/Audio/Clips/Missile_Incoming.wav";
    const string TuningPath="Assets/_Starframe/Resources/Audio/GameAudioTuning.asset";

    public static string Main()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before installing warning audio.");
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        string prepared=Path.Combine(root,"tools/FinalBalanceRound1/AudioSource/Missile_Incoming.wav");
        if(!File.Exists(prepared))throw new FileNotFoundException("Run GenerateIncomingBeep.py first.",prepared);
        Preserve(root,ClipPath);Preserve(root,ClipPath+".meta");Preserve(root,TuningPath);
        // Keep the existing .meta/GUID so every existing reference retains the replacement clip.
        File.Copy(prepared,Path.Combine(root,ClipPath),true);
        AssetDatabase.ImportAsset(ClipPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        var importer=AssetImporter.GetAtPath(ClipPath) as AudioImporter;
        if(!importer)throw new InvalidOperationException("Missing incoming warning AudioImporter.");
        var sample=importer.defaultSampleSettings;
        sample.loadType=AudioClipLoadType.DecompressOnLoad;
        sample.compressionFormat=AudioCompressionFormat.PCM;
        sample.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
        sample.preloadAudioData=true;
        importer.defaultSampleSettings=sample;
        importer.forceToMono=true;
        importer.loadInBackground=false;
        var serialized=new SerializedObject(importer);
        var normalize=serialized.FindProperty("m_Normalize")??serialized.FindProperty("normalize");
        if(normalize!=null){normalize.boolValue=false;serialized.ApplyModifiedPropertiesWithoutUndo();}
        importer.SaveAndReimport();
        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if(!clip||clip.channels!=1||Mathf.Abs(clip.length-.06f)>.002f)throw new InvalidOperationException("Incoming cue must be a mono 60 ms pip.");
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>(TuningPath);
        if(!tuning)throw new InvalidOperationException("Missing GameAudioTuning.");
        var cue=tuning.Get(GameSound.MissileIncoming);
        if(cue==null)throw new InvalidOperationException("Missing existing incoming missile cue; do not replace the cue table.");
        Undo.RecordObject(tuning,"Install crisp incoming missile pip");
        cue.clip=clip;cue.bus=GameSoundBus.Effects;cue.gain=.48f;cue.pitch=1;cue.pitchVariation=0;cue.minimumInterval=.08f;cue.priority=20;
        tuning.missileWarningFarDistance=600;tuning.missileWarningNearDistance=40;
        tuning.missileWarningFarTimeToImpact=4;tuning.missileWarningNearTimeToImpact=.35f;
        tuning.missileWarningFarInterval=.9f;tuning.missileWarningNearInterval=.12f;
        EditorUtility.SetDirty(tuning);AssetDatabase.SaveAssets();
        return "Installed incoming warning: mono PCM 48 kHz, 2 kHz / 60 ms, 2.5 ms cosine edges, gain .48, no pitch variation; one reserved voice, .90-.12 s cadence by distance or impact time. Enemy-lock cue retained. Importer normalization property found: "+(normalize!=null);
    }

    static void Preserve(string root,string relative)
    {
        string source=Path.Combine(root,relative),destination=Path.Combine(root,"Documentation/FinalBalanceRound1-2026-11-03/before",relative);
        if(!File.Exists(source)||File.Exists(destination))return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(source,destination);
    }
}
