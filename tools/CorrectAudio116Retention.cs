using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
public static class CorrectAudio116Retention
{
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before updating audio assets.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
        var removed=new[]{GameSound.RotatePart,GameSound.InvalidPlacement,GameSound.CorePowerOn,GameSound.LaserImpact};
        string[] files={"UI_Select","Blueprint_Save","Warning_Confirm","Block_Place_Click","Block_Place_Click","Block_Remove_Click"};
        float[] gains={.294f,.5f,.42f,.62f,.62f,.54f};
        AudioClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Starframe/Audio/Clips/"+name+".wav");
        for(int i=0;i<files.Length;i++){var c=tuning.Get((GameSound)i);c.clip=Clip(files[i]);c.gain=gains[i];}
        void Restore(GameSound sound,string name,float gain){var c=tuning.Get(sound);c.clip=Clip(name);c.gain=gain;}
        Restore(GameSound.ManeuverJet,"Maneuver_Jet",0);
        Restore(GameSound.BoundaryWarning,"Boundary_Warning",.44f);
        Restore(GameSound.CoreDestroyed,"Core_Destroyed",.8f);
        Restore(GameSound.Respawn,"Respawn_Reboot",.45f);
        tuning.thrusterLoop=Clip("Engine_User_Loop");
        foreach(var sound in removed){var c=tuning.Get(sound);c.clip=null;c.alternateClip=null;c.gain=0;}
        EditorUtility.SetDirty(tuning);AssetDatabase.SaveAssets();
        foreach(string name in new[]{"Rotate_Servo","Invalid_Placement","Core_Power_On","Laser_Impact_Metal"})
        {
            string path="Assets/_Starframe/Audio/Clips/"+name+".wav";
            if(File.Exists(path)&&!AssetDatabase.DeleteAsset(path))throw new IOException(path);
        }
        foreach(string name in new[]{"07_旋转组件.wav","08_无法放置组件.wav","09_放置核心并启动.wav","11_武器命中_当前静音.wav"})
        {string path=Path.Combine("音效替换",name);if(File.Exists(path))File.Delete(path);}
        AssetDatabase.SaveAssets();
        return new{uiGain=tuning.Get(GameSound.Select).gain,deleted=removed.Select(s=>s.ToString()),engine=tuning.thrusterLoop.name,
            restored=tuning.cues.Where(c=>c.clip&&AssetDatabase.GetAssetPath(c.clip).Contains("/Clips/")).Select(c=>new{sound=c.sound.ToString(),c.gain}),
            newAudioCount=AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/_Starframe/Audio/116"}).Length};
    }
}
