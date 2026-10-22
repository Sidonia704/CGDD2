using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Starframe.EditorTools
{
    /// <summary>Keep runtime-created materials available after shader stripping.</summary>
    public sealed class FrameworkBuildChecks:IPreprocessBuildWithReport
    {
        public int callbackOrder=>-100;
        public static int IncludeRuntimeShaders()
        {
            var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").First();
            var serialized=new SerializedObject(settings);var shaders=serialized.FindProperty("m_AlwaysIncludedShaders");
            if(shaders==null)throw new BuildFailedException("Graphics settings do not expose the shader inclusion list.");
            int added=0;
            foreach(string guid in AssetDatabase.FindAssets("t:Shader",new[]{"Assets/_Starframe/Art/Shaders"}))
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));if(!shader)continue;
                bool exists=false;for(int i=0;i<shaders.arraySize;i++)if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader){exists=true;break;}
                if(exists)continue;
                shaders.InsertArrayElementAtIndex(shaders.arraySize);shaders.GetArrayElementAtIndex(shaders.arraySize-1).objectReferenceValue=shader;added++;
            }
            if(added>0){serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();}
            return added;
        }
        public void OnPreprocessBuild(BuildReport report)
        {
            IncludeRuntimeShaders();
            var scenes=EditorBuildSettings.scenes.Where(value=>value.enabled).ToArray();
            if(scenes.Any(value=>value.path==GameShellInstaller.HangarScene))
            {
                string localization=Starframe.Presentation.Editor.LocalizationInstaller.Validate();
                if(!localization.StartsWith("COMPLETE:",StringComparison.Ordinal))throw new BuildFailedException(localization);
            }
        }
    }
}
