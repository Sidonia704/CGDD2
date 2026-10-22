using Starframe.Presentation;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    public static class UiDesignTools
    {
        public const string ThemePath="Assets/_Starframe/Resources/UI/WorkbenchTheme.asset";
        public static UiTheme EnsureTheme()
        {
            if(!AssetDatabase.IsValidFolder("Assets/_Starframe/Resources"))AssetDatabase.CreateFolder("Assets/_Starframe","Resources");
            if(!AssetDatabase.IsValidFolder("Assets/_Starframe/Resources/UI"))AssetDatabase.CreateFolder("Assets/_Starframe/Resources","UI");
            var theme=AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            if(!theme){theme=ScriptableObject.CreateInstance<UiTheme>();AssetDatabase.CreateAsset(theme,ThemePath);AssetDatabase.SaveAssets();}
            UiTheme.Reload();return theme;
        }
        [MenuItem("Starframe/UI 设计/打开设计配置")]
        public static void SelectTheme(){Selection.activeObject=EnsureTheme();EditorGUIUtility.PingObject(Selection.activeObject);}
        static void SetAll(bool value){var theme=EnsureTheme();Undo.RecordObject(theme,"切换 UI 设计");theme.SetAll(value);EditorUtility.SetDirty(theme);AssetDatabase.SaveAssets();}
        [MenuItem("Starframe/UI 设计/启用本轮全部设计")]
        public static void EnableAll()=>SetAll(true);
        [MenuItem("Starframe/UI 设计/恢复本轮之前的全部设计")]
        public static void DisableAll()=>SetAll(false);
    }
}
