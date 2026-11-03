using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Starframe.Maps;

public static class ReferenceMapScene
{
    public static object Create()
    {
        const string root="Assets/_Starframe/Maps/ReferenceMiningStation";
        string path=root+"/Scenes/ReferenceMiningStation3500.unity";
        if(!AssetDatabase.IsValidFolder(root+"/Scenes"))AssetDatabase.CreateFolder(root,"Scenes");
        var previous=SceneManager.GetActiveScene();bool restoreEmpty=string.IsNullOrEmpty(previous.path)&&previous.rootCount==0&&!previous.isDirty;
        string preservedEditorContext=null;
        if(string.IsNullOrEmpty(previous.path)&&!restoreEmpty)
        {
            if(previous.isDirty)throw new InvalidOperationException("Save the modified untitled scene before creating the sandbox; it has been left untouched.");
            preservedEditorContext=AssetDatabase.GenerateUniqueAssetPath(root+"/Scenes/EditorContextPreserved.unity");
            if(!EditorSceneManager.SaveScene(previous,preservedEditorContext))throw new InvalidOperationException("Could not preserve the clean untitled editor context.");
        }
        if(restoreEmpty){EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity",OpenSceneMode.Single);previous=SceneManager.GetActiveScene();}
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var mapRoot=new GameObject("Reference mining station — source-derived 3500m");var map=mapRoot.AddComponent<SpaceMapRuntime>();
            map.definition=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(root+"/Layouts/ReferenceMiningStation3500.asset");
            map.stageBuildOnStart=true;map.instancesPerFrame=32;map.buildMillisecondsPerFrame=5;map.lazyFineVisuals=true;map.visualLodDistance=70;map.visualCullDistance=0;map.visualRefreshFrames=6;map.assemblyInitialMaxDepenetrationVelocity=1;
            map.useBudgetedPhysics=true;map.detailedCollisionDistance=300;map.activeColliderBudget=8000;map.physicsRefreshFrames=12;
            map.maximumFineVisualCells=8000;
            var go=new GameObject("Sandbox Camera",typeof(Camera),typeof(AudioListener));go.tag="MainCamera";
            var camera=go.GetComponent<Camera>();camera.nearClipPlane=.2f;camera.farClipPlane=9000;camera.fieldOfView=48;camera.allowHDR=true;
            camera.transform.SetPositionAndRotation(new Vector3(1900,1500,-2700),Quaternion.LookRotation(new Vector3(0,-220,0)-new Vector3(1900,1500,-2700)));
            camera.clearFlags=CameraClearFlags.Skybox;var data=go.AddComponent<UniversalAdditionalCameraData>();data.renderPostProcessing=true;data.volumeLayerMask=1;
            go.AddComponent<MapSandboxCamera>().flySpeed=140;map.visualCamera=camera;
            var sun=new GameObject("Warm distant sun",typeof(Light));var light=sun.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.color=new Color(1,.96f,.9f);light.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(38,-40,0);
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/Materials/Environment/SpaceNebula.mat");
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.21f,.23f,.27f);
            var reflection=AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/_Starframe/Art/Textures/Space/SpaceReflection.exr");
            if(reflection){RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=reflection;}
            var volume=new GameObject("Shared game post processing",typeof(Volume)).GetComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Starframe/Settings/Rendering/ConstructionPostFX.asset");
            var probe=mapRoot.AddComponent<ReferenceMapProbe>();probe.map=map;probe.view=camera;
            EditorSceneManager.SaveScene(scene,path);
            return new {path,instances=map.definition.instances.Count,lazy=true,staged=true,preservedEditorContext};
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(restoreEmpty)EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);else if(previous.IsValid())SceneManager.SetActiveScene(previous);}
    }
}
