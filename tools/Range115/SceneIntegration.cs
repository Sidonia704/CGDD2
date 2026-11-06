using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Starframe.Maps;
using Starframe.Flight;

public static class Range115SceneIntegration
{
    public static string ApplyOld()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before installing map.");
        const string root="Assets/_Starframe/Maps/SaturnRingContinuous";
        var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(root+"/Layouts/SaturnRingContinuous.asset");
        if(!map||!map.Validate(out var error))throw new InvalidOperationException("Missing or invalid prepared map.");
        const string scenePath="Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity";
        var previous=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(scenePath);bool opened=!scene.isLoaded;
        if(!opened&&scene.isDirty)throw new InvalidOperationException("Mission has unsaved user changes.");
        Directory.CreateDirectory("tmp/Range115/before");if(!File.Exists("tmp/Range115/before/SaturnRing.unity"))File.Copy(scenePath,"tmp/Range115/before/SaturnRing.unity");
        if(opened)scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
        try
        {
            var roots=scene.GetRootGameObjects();var runtime=roots.SelectMany(r=>r.GetComponentsInChildren<SpaceMapRuntime>(true)).Single();
            runtime.definition=map;runtime.lazyFineVisuals=true;runtime.keepCoarseIntactVisuals=true;
            foreach(var backdrop in roots.SelectMany(r=>r.GetComponentsInChildren<RangeAsteroidBackdrop>(true)))
            {
                var so=new SerializedObject(backdrop);string[] fields={"detailedRockA","detailedRockB","distantRockA","distantRockB"};
                for(int i=0;i<fields.Length;i++){var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(root+"/Backdrop/ContinuousBackdrop"+i+".asset");if(!mesh)throw new InvalidOperationException("Missing backdrop "+i);so.FindProperty(fields[i]).objectReferenceValue=mesh;}
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "Installed continuous mission surfaces and four shared backdrop meshes.";
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }
    public static string Apply(string mapPath)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before installing map.");
        var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(mapPath);
        if(!map||!map.Validate(out var error))throw new InvalidOperationException("Missing or invalid prepared map.");
        const string scenePath="Assets/_Starframe/Scenes/Testing/FlightRange.unity";
        var previous=SceneManager.GetActiveScene();
        var scene=SceneManager.GetSceneByPath(scenePath);bool opened=!scene.isLoaded;
        if(!opened&&scene.isDirty)throw new InvalidOperationException("Range has unsaved user changes; merge explicitly before installing.");
        Directory.CreateDirectory("tmp/Range115/before");
        string backup="tmp/Range115/before/FlightRange.unity";
        if(!File.Exists(backup))File.Copy(scenePath,backup);
        if(opened)scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots=scene.GetRootGameObjects();
            var runtime=roots.SelectMany(r=>r.GetComponentsInChildren<SpaceMapRuntime>(true)).Single();
            runtime.definition=map;runtime.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);runtime.transform.localScale=Vector3.one;
            runtime.lazyFineVisuals=true;runtime.keepCoarseIntactVisuals=true;
            runtime.stageBuildOnStart=true;runtime.instancesPerFrame=16;runtime.buildMillisecondsPerFrame=4;
            foreach(var backdrop in roots.SelectMany(r=>r.GetComponentsInChildren<RangeAsteroidBackdrop>(true)))
            {backdrop.backdropEnabled=false;backdrop.enabled=false;}
            var bootstrap=roots.SelectMany(r=>r.GetComponentsInChildren<FlightRangeBootstrap>(true)).Single();
            var serialized=new SerializedObject(bootstrap);
            var spawn=(Transform)serialized.FindProperty("sandboxSpawn").objectReferenceValue;
            spawn.SetParent(runtime.transform,false);spawn.localPosition=map.playerSpawn;spawn.localRotation=Quaternion.Euler(map.playerEulerAngles);
            serialized.FindProperty("sandboxArenaHalfExtent").floatValue=Mathf.Max(2000,Mathf.Max(map.size.x,map.size.z)*.5f+200);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            const string skyPath="Assets/_Starframe/Art/SimulationTrial/Range115Space.mat";
            var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(!sky){sky=new Material(RenderSettings.skybox){name="Range115Space"};AssetDatabase.CreateAsset(sky,skyPath);}
            float radius=sky.GetFloat("_PlanetWorldRadius");
            sky.SetVector("_PlanetCenterWS",new Vector4(map.playerSpawn.x,map.playerSpawn.y-radius-320000,map.playerSpawn.z,0));
            EditorUtility.SetDirty(sky);RenderSettings.skybox=sky;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return "Installed independent range layout "+mapPath+"; planet below spawn; distant belts disabled.";
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }
}
