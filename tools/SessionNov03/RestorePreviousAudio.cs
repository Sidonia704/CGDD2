using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Starframe.App;
public static class RestorePreviousAudioNov03 {
 public static object Main(){
 if(EditorApplication.isPlaying)throw new InvalidOperationException("Not in edit mode");
 const string root="Assets/_Starframe/Audio/Clips/";
 const string previous="Documentation/SessionNov03/audio-environment/before/";
 const string backup="Documentation/AudioRollback-2026-11-03/before/";
 var a=AssetDatabase.LoadAssetAtPath<GameAudioTuning>("Assets/_Starframe/Resources/Audio/GameAudioTuning.asset");
 var currentLost=a.Get(GameSound.TargetLost);
 var flareBytes=File.ReadAllBytes(AssetDatabase.GetAssetPath(currentLost.clip));
 float flarePitch=currentLost.pitch,flareGain=currentLost.gain,flareVariation=currentLost.pitchVariation;
 string[] names={"Flare_Deploy","Enemy_Detected","Enemy_Lock_Warning","UI_Select","Blueprint_Save","Warning_Confirm","Rotate_Servo","Core_Power_On","Lock_Acquired","Boundary_Warning","Respawn_Reboot"};
 foreach(var n in names){string path=root+n+".wav";if(!File.Exists(backup+n+".wav"))File.Copy(path,backup+n+".wav");File.Copy(previous+n+".wav",path,true);}
 File.WriteAllBytes(root+"Flare_Deploy.wav",flareBytes);
 foreach(var n in names)AssetDatabase.ImportAsset(root+n+".wav",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
 string baseline=File.ReadAllText(previous+"GameAudioTuning.asset.txt");
 foreach(Match m in Regex.Matches(baseline,@"(?ms)^  - sound: (\d+)\r?\n(.*?)(?=^  - sound:|\z)")){
 var cue=a.Get((GameSound)int.Parse(m.Groups[1].Value));if(cue==null)continue;string data=m.Groups[2].Value;
 var guid=Regex.Match(data,@"guid: ([a-f0-9]+)");cue.clip=guid.Success?AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid.Groups[1].Value)):null;
 Func<string,float> value=name=>float.Parse(Regex.Match(data,@"(?m)^    "+name+@": ([^\r\n]+)").Groups[1].Value,CultureInfo.InvariantCulture);
 cue.gain=value("gain");cue.pitch=value("pitch");cue.pitchVariation=value("pitchVariation");cue.minimumInterval=value("minimumInterval");cue.priority=(int)value("priority");cue.bus=(GameSoundBus)(int)value("bus");
 }
 var flare=a.Get(GameSound.FlareDeploy);flare.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(root+"Flare_Deploy.wav");flare.pitch=flarePitch;flare.gain=flareGain;flare.pitchVariation=flareVariation;
 EditorUtility.SetDirty(a);AssetDatabase.SaveAssets();
 return new{restoredClips=names.Length,flareClip=flare.clip.name,flarePitch,flareGain,missilePitchScale=.9f};
 }
}
