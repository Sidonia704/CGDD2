using Starframe.Combat;
using UnityEditor;
using UnityEngine;

namespace Starframe.Editor
{
    public static class CombatDamageTools
    {
        public const string Path="Assets/_Starframe/Settings/Combat/CombatDamageTuning.asset";
        [MenuItem("Starframe/平衡参数/伤害倍率")]
        public static void Open()
        {
            var tuning=AssetDatabase.LoadAssetAtPath<CombatDamageTuning>(Path);
            if(!tuning){Debug.LogError("Combat damage tuning is not installed: "+Path);return;}
            Selection.activeObject=tuning;EditorGUIUtility.PingObject(tuning);
        }
    }
}
