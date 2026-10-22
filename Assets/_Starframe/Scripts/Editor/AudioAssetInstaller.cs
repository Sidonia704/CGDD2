using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Starframe.App;

namespace Starframe.Editor
{
    public static class AudioAssetInstaller
    {
        public const string TuningPath="Assets/_Starframe/Resources/Audio/GameAudioTuning.asset";
        const string Clips="Assets/_Starframe/Audio/Clips/";
        [MenuItem("Starframe/Audio/Import prepared audio and create tuning")]
        public static void Install()
        {
            foreach(string path in AssetDatabase.FindAssets("t:AudioClip",new[]{Clips.TrimEnd('/')}))
            {
                string file=AssetDatabase.GUIDToAssetPath(path);var importer=AssetImporter.GetAtPath(file) as AudioImporter;
                if(!importer)continue;
                bool music=file.EndsWith("Workbench_Ambience.wav");
                var sample=importer.defaultSampleSettings;
                sample.loadType=music?AudioClipLoadType.Streaming:AudioClipLoadType.DecompressOnLoad;
                sample.compressionFormat=music?AudioCompressionFormat.Vorbis:AudioCompressionFormat.PCM;
                sample.quality=.8f;sample.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
                sample.preloadAudioData=!music;
                importer.defaultSampleSettings=sample;importer.forceToMono=true;
                importer.loadInBackground=music;importer.SaveAndReimport();
            }
            if(AssetDatabase.LoadAssetAtPath<GameAudioTuning>(TuningPath)){AssetDatabase.SaveAssets();return;}
            if(!AssetDatabase.IsValidFolder("Assets/_Starframe/Resources/Audio"))AssetDatabase.CreateFolder("Assets/_Starframe/Resources","Audio");
            var tuning=ScriptableObject.CreateInstance<GameAudioTuning>();tuning.name="GameAudioTuning";
            tuning.workbenchMusic=Clip("Workbench_Ambience");tuning.thrusterLoop=Clip("Engine_User_Loop")?Clip("Engine_User_Loop"):Clip("Engine_Low_Rumble");
            var cues=new List<GameSoundCue>();
            Add(cues,GameSound.Select,"UI_Select",.42f,.045f,64,true,.025f);
            Add(cues,GameSound.SaveBlueprint,"Blueprint_Save",.5f,.25f,48,true,0);
            Add(cues,GameSound.ConfirmWarning,"Warning_Confirm",.42f,.6f,48,true,0);
            Add(cues,GameSound.PlaceHull,"Block_Place_Click",.62f,.055f,128,false,.025f);
            Add(cues,GameSound.PlaceModule,"Block_Place_Click",.62f,.055f,128,false,.025f);
            Add(cues,GameSound.RemovePart,"Block_Remove_Click",.54f,.07f,128,false,.025f);
            Add(cues,GameSound.RotatePart,"Rotate_Servo",.3f,.055f,144,false,.025f);
            Add(cues,GameSound.InvalidPlacement,"Invalid_Placement",.4f,.22f,80,false,0);
            Add(cues,GameSound.CorePowerOn,"Core_Power_On",.48f,.5f,80,false,0);
            Add(cues,GameSound.LaserShot,Clip("Laser_Shot_New")?"Laser_Shot_New":"Laser_Shot",.48f,.07f,112,false,.025f);
            Add(cues,GameSound.LaserImpact,"Laser_Impact_Metal",0,.09f,136,false,.05f);
            Add(cues,GameSound.LockAcquired,"Lock_Acquired",.5f,.3f,48,false,0);
            Add(cues,GameSound.TargetLost,"Target_Lost",.4f,.3f,48,false,0);
            Add(cues,GameSound.ManeuverJet,"Maneuver_Jet",0,.7f,144,false,.02f);
            Add(cues,GameSound.BoundaryWarning,"Boundary_Warning",.44f,2,40,false,0);
            Add(cues,GameSound.CoreDestroyed,"Core_Destroyed",.8f,.3f,32,false,.025f);
            Add(cues,GameSound.Respawn,"Respawn_Reboot",.45f,.5f,64,false,0);
            tuning.cues=cues.ToArray();AssetDatabase.CreateAsset(tuning,TuningPath);AssetDatabase.SaveAssets();
        }
        static AudioClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AudioClip>(Clips+name+".wav");
        static void Add(List<GameSoundCue> cues,GameSound sound,string clip,float gain,float interval,int priority,bool ui,float variation)
        {
            cues.Add(new GameSoundCue{sound=sound,clip=Clip(clip),gain=gain,minimumInterval=interval,priority=priority,
                bus=ui?GameSoundBus.Interface:GameSoundBus.Effects,pitchVariation=variation});
        }
    }
}
