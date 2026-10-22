// @role Load target blueprints and configured target counts for the range.
// @calls BlueprintRepository

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Starframe.Construction;
namespace Starframe.Combat
{
    public sealed class RangeTargetRoster
    {
        public const string PreferenceKey="Starframe.RangeTargets.v1";
        [Serializable]public sealed class Selection{public string file;public int count;}
        [Serializable]sealed class Settings{public List<Selection> entries=new List<Selection>();}
        public sealed class SpawnPlan{public BlueprintData blueprint;public int count;}
        public sealed class TargetOption{public string file;public BlueprintData blueprint;public int count;public bool builtIn;}
        readonly string directory,key;readonly Settings settings;
        public RangeTargetRoster(string blueprintDirectory,string preferenceKey=null)
        {
            directory=blueprintDirectory;key=preferenceKey??PreferenceKey;
            try{settings=PlayerPrefs.HasKey(key)?JsonUtility.FromJson<Settings>(PlayerPrefs.GetString(key)):null;}catch(ArgumentException){}
            if(settings==null)settings=new Settings{entries=new List<Selection>{new Selection{file="test.json",count=1}}};
            if(settings.entries==null)settings.entries=new List<Selection>();
            settings.entries.RemoveAll(entry=>entry==null);
        }
        static void ValidateFile(string file){if(string.IsNullOrWhiteSpace(file)||Path.GetFileName(file)!=file||file.IndexOfAny(new[]{'/','\\'})>=0||!string.Equals(Path.GetExtension(file),".json",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("需要有效的蓝图文件名。");}
        public int GetCount(string file){foreach(var e in settings.entries)if(string.Equals(e.file,file,StringComparison.OrdinalIgnoreCase))return Mathf.Max(0,e.count);return 0;}
        public void SetCount(string file,int count)=>SetCount(file,count,null);
        public void SetCount(string file,int count,TextAsset defaultTest)
        {
            ValidateFile(file);if(count<0)throw new ArgumentOutOfRangeException(nameof(count));
            if(count>0)LoadTarget(file,defaultTest);
            settings.entries.RemoveAll(e=>string.Equals(e.file,file,StringComparison.OrdinalIgnoreCase));if(count>0)settings.entries.Add(new Selection{file=file,count=count});
            PlayerPrefs.SetString(key,JsonUtility.ToJson(settings));PlayerPrefs.Save();
        }
        public List<TargetOption> AvailableTargets(TextAsset defaultTest)
        {
            var result=new List<TargetOption>();
            bool useBuiltIn=defaultTest&&!File.Exists(Path.Combine(directory,"test.json"));
            if(useBuiltIn)AddOption(result,"test.json",defaultTest,true);
            foreach(var file in new BlueprintRepository(directory).List())AddOption(result,file,null,false);
            return result;
        }
        void AddOption(List<TargetOption> options,string file,TextAsset defaultTest,bool builtIn)
        {
            try{options.Add(new TargetOption{file=file,blueprint=LoadTarget(file,defaultTest),count=GetCount(file),builtIn=builtIn});}
            catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){}
        }
        BlueprintData LoadTarget(string file,TextAsset defaultTest)
        {
            ValidateFile(file);BlueprintData data;
            if(!File.Exists(Path.Combine(directory,file))&&string.Equals(file,"test.json",StringComparison.OrdinalIgnoreCase)&&defaultTest)
                data=BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(defaultTest.text));
            else data=new BlueprintRepository(directory).Load(file);
            if(!data.hasCore)throw new InvalidDataException("靶机蓝图需要一个核心。");
            return data;
        }
        public List<SpawnPlan> Resolve(TextAsset defaultTest)
        {
            var result=new List<SpawnPlan>();
            foreach(var entry in settings.entries){if(entry.count<=0)continue;
                try{
                    result.Add(new SpawnPlan{blueprint=LoadTarget(entry.file,defaultTest),count=entry.count});
                }catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){Debug.LogWarning("无法创建靶机 "+entry.file+"："+e.Message);}
            }return result;
        }
    }
}
