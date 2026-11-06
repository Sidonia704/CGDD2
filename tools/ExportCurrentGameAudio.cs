using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Starframe.App;

// Exports editable copies only; does not alter Unity assets, references or playback settings.
public static class ExportCurrentGameAudio
{
    [Serializable] public sealed class Entry
    {
        public string file, purpose, binding, sourceAsset, sourceGuid, sourceSha256, status, notes;
        public float durationSeconds, gain, pitch, pitchVariation, minimumIntervalSeconds;
        public int sampleRate, channels;
        public bool loop;
        public long bytes;
    }
    [Serializable] public sealed class Manifest
    {
        public int version=1;
        public string exportedAt, tuningAsset, replacementMode;
        public List<Entry> entries=new List<Entry>();
    }
    static readonly string[] Purposes={
        "界面按钮点击", "保存蓝图", "警告确认与低血量警报", "放置船体及普通组件", "放置武器或推进器",
        "拆除组件", "旋转组件", "无法放置组件", "放置核心并启动", "武器开火_直射与导弹共用",
        "武器命中_当前静音", "锁定目标成功", "丢失目标锁定", "加力启动_当前静音", "地图边界警报",
        "己方核心摧毁", "重生与载入蓝图", "发现敌人", "被敌人锁定警报", "导弹接近警报", "干扰弹发射"
    };
    static string Hash(string path)
    {
        using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
    }
    static string F(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    public static object Run()
    {
        const string asset="Assets/_Starframe/Resources/Audio/GameAudioTuning.asset";
        var tuning=AssetDatabase.LoadAssetAtPath<GameAudioTuning>(asset);
        if(!tuning)throw new InvalidOperationException("Missing current game audio tuning.");
        string directory=Path.GetFullPath("音效替换");
        var manifest=new Manifest{exportedAt=DateTimeOffset.Now.ToString("o"),tuningAsset=asset,
            replacementMode="编辑副本：同名覆盖 WAV 后按 binding 接回游戏；本次未修改运行时引用。素材保留原始音频，播放音量及音高未烘焙。"};
        Entry Add(AudioClip clip,string purpose,string binding,float gain,float pitch=1,float variation=0,float interval=0,bool loop=false,string notes="")
        {
            if(!clip)throw new InvalidOperationException("Missing clip for "+binding);
            string source=AssetDatabase.GetAssetPath(clip);
            if(!File.Exists(source)||!string.Equals(Path.GetExtension(source),".wav",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Expected a native WAV source: "+source);
            using(var input=File.OpenRead(source))using(var reader=new BinaryReader(input))
                if(new string(reader.ReadChars(4))!="RIFF")throw new InvalidDataException("Invalid WAV: "+source);
            var entry=new Entry{file=(manifest.entries.Count+1).ToString("00")+"_"+purpose+".wav",purpose=purpose,binding=binding,
                sourceAsset=source,sourceGuid=AssetDatabase.AssetPathToGUID(source),sourceSha256=Hash(source),bytes=new FileInfo(source).Length,
                durationSeconds=clip.length,sampleRate=clip.frequency,channels=clip.channels,gain=gain,pitch=pitch,pitchVariation=variation,
                minimumIntervalSeconds=interval,loop=loop,status=gain>0?"当前启用":"已配置，当前静音",notes=notes};
            manifest.entries.Add(entry);return entry;
        }
        foreach(GameSound sound in Enum.GetValues(typeof(GameSound)))
        {
            if((int)sound>=Purposes.Length)throw new InvalidOperationException("Add a purpose for "+sound);
            var cue=tuning.Get(sound);if(cue==null)throw new InvalidOperationException("Missing configured event: "+sound);
            string notes="";
            switch(sound)
            {
                case GameSound.ConfirmWarning:notes="警告弹窗确认；己方船体低于25%时每10秒提醒，当前共用。";break;
                case GameSound.PlaceHull:notes="除核心、武器和推进器外的组件；与放置武器/推进器共用源素材，本次分别导出。";break;
                case GameSound.PlaceModule:notes="与放置船体共用源素材，本次分别导出，方便以后独立替换。";break;
                case GameSound.LaserShot:notes="目前直射武器和导弹均使用此素材；导弹额外乘0.9音高，敌方开火额外乘0.7音量并使用距离衰减。设置中的音效试听也使用此素材。";break;
                case GameSound.LaserImpact:notes="音量为0，且当前运行时代码没有触发此事件；列为已配置的备用音效。";break;
                case GameSound.TargetLost:notes="与锁定成功共用源素材，运行时以0.75倍音高播放；分别导出。";break;
                case GameSound.ManeuverJet:notes="开始加力时触发，但音量为0。持续推进声由22号循环素材提供。";break;
                case GameSound.CoreDestroyed:notes="当前仅己方核心摧毁时触发。";break;
                case GameSound.Respawn:notes="己方重生与装配界面载入蓝图共用。";break;
                case GameSound.MissileIncoming:notes="系统按距离和预计命中时间重复播放，间隔约0.12至0.9秒；使用专用告警通道。";break;
                case GameSound.FlareDeploy:notes="干扰弹发射提示；当前按0.75倍音高播放，素材本身未降调。";break;
            }
            Add(cue.clip,Purposes[(int)sound],"cues."+sound,cue.gain,cue.pitch,cue.pitchVariation,cue.minimumInterval,false,notes);
        }
        Add(tuning.thrusterLoop,"本舰引擎推进_循环","thrusterLoop",tuning.thrusterGain,1,0,0,true,
            "运行时随推力调节音量；待机/全推力/加力音高="+F(tuning.idlePitch)+"/"+F(tuning.fullThrustPitch)+"/"+F(tuning.boostPitch)+"；加力音量倍率="+F(tuning.boostVolumeMultiplier)+"。请保持无缝循环。");
        if(tuning.workbenchMusic)Add(tuning.workbenchMusic,"主菜单与装配背景氛围_循环","workbenchMusic",tuning.musicGain,1,0,0,true,
            "菜单、蓝图库、装配等非飞行场景的背景音；使用音乐音量设置。");
        if(tuning.flightMusic)Add(tuning.flightMusic,"飞行战斗背景音乐_循环","flightMusic",tuning.musicGain,1,0,0,true);
        // JsonUtility cannot reliably serialize nested types from transient run_script assemblies.
        var jsonType=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        string json=(string)jsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{manifest});
        foreach(var entry in manifest.entries)
        {
            string path=Path.Combine(directory,entry.file);
            if(File.Exists(path)&&Hash(path)!=entry.sourceSha256)throw new IOException("Refusing to overwrite edited audio: "+path);
        }
        Directory.CreateDirectory(directory);
        foreach(var entry in manifest.entries)
        {
            string path=Path.Combine(directory,entry.file);if(!File.Exists(path))File.Copy(entry.sourceAsset,path,false);
            if(Hash(path)!=entry.sourceSha256)throw new IOException("Export hash mismatch: "+entry.file);
        }
        var encoding=new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(directory,"音效映射.json"),json,encoding);
        var readme=new StringBuilder("# 当前游戏音效替换包\n\n");
        readme.AppendLine("导出日期："+manifest.exportedAt+"。共 "+manifest.entries.Count+" 个 WAV，用途不同但素材相同的事件已分别导出。包含当前静音的2项配置及背景氛围音。");
        readme.AppendLine("\n直接修改或用新 WAV 覆盖同名文件即可；建议保留文件名。请使用真正的 WAV 格式，不要只修改扩展名。循环素材请保持首尾衔接。原始采样率、声道和时长见映射文件，不要求替换素材保持原时长。");
        readme.AppendLine("\n这些是独立编辑副本。修改本文件夹后告诉我，我会根据音效映射.json接回对应用途；本次没有更改游戏的音效引用。不要改动原 Assets 音频或 .meta 文件。运行时音量、音高、随机音高和距离衰减未烘焙进素材；因此单独试听与游戏中听到的响度、音高可能不同。");
        readme.AppendLine("\n| 文件 | 状态 | 时长 | 播放音量 | 基础音高 | 当前用途说明 |\n|---|---|---:|---:|---:|---|");
        foreach(var e in manifest.entries)readme.AppendLine("| ["+e.file+"]("+e.file+") | "+e.status+" | "+F(e.durationSeconds)+"秒 | "+F(e.gain)+" | "+F(e.pitch)+" | "+e.notes+" |");
        readme.AppendLine("\n飞行/战斗背景音乐当前未指定文件，未加入历史素材。未使用的旧版本音效和素材包也未加入。");
        readme.AppendLine("\n音效映射.json保留每项用途的绑定、源路径、GUID、SHA256、时长、采样率、声道和播放参数，便于后续准确接入。共用素材的不同副本可分别替换。");
        File.WriteAllText(Path.Combine(directory,"音效清单与替换说明.md"),readme.ToString(),encoding);
        return new{directory,count=manifest.entries.Count,enabled=manifest.entries.Count(e=>e.gain>0),muted=manifest.entries.Count(e=>e.gain<=0),
            distinctSources=manifest.entries.Select(e=>e.sourceGuid).Distinct().Count(),totalBytes=manifest.entries.Sum(e=>e.bytes),
            files=manifest.entries.Select(e=>new{e.file,e.status,e.sourceAsset}).ToArray(),gameAssetChanges=false};
    }
}
