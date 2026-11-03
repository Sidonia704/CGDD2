using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
public static class RangeAtmosphereInspect
{
    public static string Read()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();var sb=new StringBuilder();
        try{
            var scene=EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Testing/FlightRange.unity",OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var roots=scene.GetRootGameObjects();
            foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true))){var d=c.GetUniversalAdditionalCameraData();sb.AppendLine($"Camera {c.name}: HDR={c.allowHDR}; post={d.renderPostProcessing}; depth={d.requiresDepthTexture}; volumeMask={d.volumeLayerMask.value}; renderer={d.scriptableRenderer.GetType().Name}");}
            foreach(var v in roots.SelectMany(r=>r.GetComponentsInChildren<Volume>(true)))sb.AppendLine($"Volume {v.name}: layer={v.gameObject.layer}; global={v.isGlobal}; weight={v.weight}; enabled={v.enabled}; profile={AssetDatabase.GetAssetPath(v.sharedProfile)}");
            foreach(var l in roots.SelectMany(r=>r.GetComponentsInChildren<Light>(true)))sb.AppendLine($"Light {l.name}: {l.type}; intensity={l.intensity}; color={l.color}; rotation={l.transform.eulerAngles}; shadows={l.shadows}");
            sb.AppendLine($"Fog={RenderSettings.fog} {RenderSettings.fogMode} density={RenderSettings.fogDensity}; ambient={RenderSettings.ambientIntensity}");
            sb.AppendLine("Quality="+QualitySettings.GetQualityLevel()+" names="+string.Join(",",QualitySettings.names));
            sb.AppendLine("User bloom="+Starframe.App.GameSettings.Current.bloomEnabled+" intensity="+Starframe.App.GameSettings.Current.bloomIntensity);
            var rp=UniversalRenderPipeline.asset;var so=new SerializedObject(rp);var renderers=so.FindProperty("m_RendererDataList");
            for(int i=0;i<renderers.arraySize;i++){var data=renderers.GetArrayElementAtIndex(i).objectReferenceValue;var rso=new SerializedObject(data);var post=rso.FindProperty("postProcessData");sb.AppendLine("RendererData="+AssetDatabase.GetAssetPath(data)+" postData="+(post?.objectReferenceValue?AssetDatabase.GetAssetPath(post.objectReferenceValue):"MISSING"));}
            return sb.ToString();
        }finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
}
