using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
public static class RangeBackdropIntegrate
{
    public static string SaveMaterial()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before persisting material.");
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/Materials/Environment/RangeAsteroidBackdrop.mat");
        material.SetColor("_BaseColor",new Color(.93f,.93f,.93f,1));EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();return "Saved matching neutral plastic backdrop palette";
    }
    public static string Apply()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before authoring.");
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Starframe/Settings/Rendering/RangeAtmospherePostFX.asset");
        if(!profile)throw new InvalidOperationException("Range profile missing.");
        if(!profile.TryGet<DepthOfField>(out var dof)){dof=profile.Add<DepthOfField>(true);AssetDatabase.AddObjectToAsset(dof,profile);}
        dof.active=true;dof.mode.Override(DepthOfFieldMode.Off);EditorUtility.SetDirty(dof);
        if(profile.TryGet<MotionBlur>(out var motion)){motion.intensity.Override(0);EditorUtility.SetDirty(motion);}
        EditorUtility.SetDirty(profile);
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Testing/FlightRange.unity",OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
            camera.farClipPlane=28000;EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Scene save failed.");
            AssetDatabase.SaveAssets();return "Range DOF off on all quality tiers; world far clip 28000m; near clip preserved at "+camera.nearClipPlane;
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
}
