using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    public static class BalanceParameterTools
    {
        const string Baseline="Documentation/Balance-2026-11-02/before.json";
        [Serializable] public sealed class Snapshot{public string provenance;public List<Value> values;}
        [Serializable] public sealed class Value{public string path,field,category;public float number;public bool boolean,isBool;}
        [MenuItem("Starframe/平衡参数/打开组件数据库")]
        public static void OpenModules(){Selection.activeObject=AssetDatabase.LoadMainAssetAtPath("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");}
        [MenuItem("Starframe/平衡参数/打开飞行参数")]
        public static void OpenFlight(){Selection.activeObject=AssetDatabase.LoadMainAssetAtPath("Assets/_Starframe/Settings/Flight/FlightTuning.asset");}
        [MenuItem("Starframe/平衡参数/打开恢复与战斗参数")]
        public static void OpenBattle(){Selection.activeObject=AssetDatabase.LoadMainAssetAtPath("Assets/_Starframe/Settings/Flight/BattleTuning.asset");}
        [MenuItem("Starframe/平衡参数/导入三艘参考船（保留同名存档）")]
        public static void ImportReferences()
        {
            var repository=new Starframe.Construction.BlueprintRepository(Starframe.App.ShipWorkspace.BlueprintDirectory);int imported=0,preserved=0;
            foreach(var file in Directory.GetFiles("Documentation/Balance-2026-11-02/Blueprints","Balance-*.json")){
                var data=JsonUtility.FromJson<Starframe.Construction.BlueprintData>(File.ReadAllText(file));
                try{repository.Save(data,false);imported++;}catch(IOException){preserved++;}
            }
            Debug.Log("Balance reference blueprints imported="+imported+", existing names preserved="+preserved);
        }
        [MenuItem("Starframe/平衡参数/恢复旧参数/武器")]
        static void Weapons()=>Confirm("weapon");
        [MenuItem("Starframe/平衡参数/恢复旧参数/船体与装甲")]
        static void Hull()=>Confirm("hull");
        [MenuItem("Starframe/平衡参数/恢复旧参数/推进器")]
        static void Engines()=>Confirm("propulsion");
        [MenuItem("Starframe/平衡参数/恢复旧参数/电源")]
        static void Power()=>Confirm("power");
        [MenuItem("Starframe/平衡参数/恢复旧参数/护盾")]
        static void Shields()=>Confirm("shield");
        [MenuItem("Starframe/平衡参数/恢复旧参数/核心")]
        static void Core()=>Confirm("core");
        [MenuItem("Starframe/平衡参数/恢复旧参数/反制")]
        static void Countermeasures()=>Confirm("countermeasure");
        [MenuItem("Starframe/平衡参数/恢复旧参数/飞行")]
        static void Flight()=>Confirm("flight");
        [MenuItem("Starframe/平衡参数/恢复旧参数/修复")]
        static void Recovery()=>Confirm("recovery");
        [MenuItem("Starframe/平衡参数/恢复旧参数/出击预算")]
        static void Missions()=>Confirm("mission");
        static void Confirm(string category)
        {
            if(EditorApplication.isPlaying){EditorUtility.DisplayDialog("平衡参数","请先停止播放。","确定");return;}
            if(EditorUtility.DisplayDialog("恢复旧参数",category+"：仅恢复数值，保留美术与玩家蓝图。基线由历史档案与调整前读数重建，可在 parameters.csv 对照。","恢复","取消"))
                Debug.Log("Restored balance fields: "+Restore(category));
        }
        public static int Restore(string category)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
            var data=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Baseline));
            if(data?.values==null||data.values.Count<700)throw new InvalidDataException("Incomplete balance baseline.");
            int count=0;Undo.SetCurrentGroupName("Restore balance "+category);
            foreach(var v in data.values){if(v.category!=category)continue;
                if(!v.path.StartsWith("Assets/_Starframe/",StringComparison.Ordinal))throw new InvalidDataException("Unexpected asset path");
                var asset=AssetDatabase.LoadMainAssetAtPath(v.path);if(!asset)throw new InvalidDataException(v.path);var so=new SerializedObject(asset);var property=so.FindProperty(v.field);
                if(property==null)throw new InvalidDataException(v.field);Undo.RecordObject(asset,"Restore balance "+category);
                if(v.isBool)property.boolValue=v.boolean;else if(property.propertyType==SerializedPropertyType.Integer)property.intValue=Mathf.RoundToInt(v.number);else property.floatValue=v.number;
                so.ApplyModifiedProperties();EditorUtility.SetDirty(asset);count++;
            }
            AssetDatabase.SaveAssets();return count;
        }
    }
}
