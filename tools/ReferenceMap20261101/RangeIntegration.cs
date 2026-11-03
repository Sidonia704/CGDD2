using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Flight;
using Starframe.Maps;

public static class ReferenceRangeIntegration
{
    const string ScenePath="Assets/_Starframe/Scenes/Testing/FlightRange.unity";
    const string LayoutPath="Assets/_Starframe/Maps/ReferenceMiningStation/Layouts/ReferenceMiningStation3500.asset";
    const string RootName="Temporary Map Sandbox";
    public static object Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before authoring the range.");
        var scene=SceneManager.GetSceneByPath(ScenePath);bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            var bootstrap=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FlightRangeBootstrap>(true)).Single();
            var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single(c=>c.CompareTag("MainCamera"));
            var root=scene.GetRootGameObjects().FirstOrDefault(r=>r.name==RootName);
            if(!root){root=new GameObject(RootName);SceneManager.MoveGameObjectToScene(root,scene);}
            root.transform.SetPositionAndRotation(new Vector3(0,-1100,0),Quaternion.identity);root.transform.localScale=Vector3.one;
            var map=root.GetComponent<SpaceMapRuntime>();if(!map)map=root.AddComponent<SpaceMapRuntime>();
            map.definition=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(LayoutPath);
            if(!map.definition)throw new InvalidOperationException("Missing reference layout.");
            map.stageBuildOnStart=true;map.instancesPerFrame=32;map.buildMillisecondsPerFrame=4;
            map.lazyFineVisuals=true;map.keepCoarseIntactVisuals=true;map.visualCamera=camera;map.visualLodDistance=70;map.maximumFineVisualCells=8000;map.visualRefreshFrames=6;
            map.useBudgetedPhysics=true;map.detailedCollisionDistance=120;map.activeColliderBudget=3500;map.physicsRefreshFrames=12;map.assemblyInitialMaxDepenetrationVelocity=1;
            var spawn=root.transform.Find("Range Spawn");
            if(!spawn){spawn=new GameObject("Range Spawn").transform;spawn.SetParent(root.transform,false);}
            spawn.SetPositionAndRotation(new Vector3(0,-600,-1100),Quaternion.Euler(25,0,0));
            var settings=new SerializedObject(bootstrap);settings.FindProperty("sandboxSpawn").objectReferenceValue=spawn;
            settings.FindProperty("sandboxArenaHalfExtent").floatValue=2000;settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Range scene save failed.");
            return new {scene=ScenePath,root=RootName,layout=LayoutPath,instances=map.definition.instances.Count,offsetY=root.transform.position.y,spawn=new[]{spawn.position.x,spawn.position.y,spawn.position.z},arenaSide=4000,restore="Disable Temporary Map Sandbox before entering Play; the original spawn and arena tuning are used automatically."};
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
    }
    public static object Probe()
    {
        var map=UnityEngine.Object.FindFirstObjectByType<SpaceMapRuntime>();
        var player=UnityEngine.Object.FindObjectsByType<ShipFlightController>(FindObjectsSortMode.None).FirstOrDefault(f=>f.name=="PlayerShip");
        return new {playing=Application.isPlaying,frame=Time.frameCount,time=Time.time,scene=SceneManager.GetActiveScene().path,
            map=map?map.name:null,loaded=map&&map.BuildIsComplete,built=map?map.BuiltInstanceCount:0,total=map?map.BuildTotalInstanceCount:0,
            activeColliders=map?map.ActiveMapColliderCount:0,player=player?new[]{player.transform.position.x,player.transform.position.y,player.transform.position.z}:null,
            arenaHalfExtent=player?player.Tuning.arenaHalfExtent:0,visibleStructures=map?map.Structures.Count(s=>s):0};
    }
    public static object CaptureLaunch()
    {
        var app=UnityEngine.Object.FindFirstObjectByType<Starframe.App.GameAppBootstrap>();
        if(!Application.isPlaying||!app||!app.IsReady)throw new InvalidOperationException("A ready Hangar Play session is required.");
        const string output="Documentation/RangeSandbox-2026-11-02/";
        System.IO.Directory.CreateDirectory(output);
        var timer=System.Diagnostics.Stopwatch.StartNew();bool capture=false,waitedForMap=false;
        EditorApplication.CallbackFunction observe=null;
        observe=()=>
        {
            if(!Application.isPlaying){EditorApplication.update-=observe;return;}
            if(RangeLoadingOverlay.IsLoading)
            {
                waitedForMap|=GameObject.Find("PlayerShip")==null;
                if(!capture&&RangeLoadingOverlay.Progress>=.22f&&RangeLoadingOverlay.Progress<.96f)
                {ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(output+"loading-progress.png"));capture=true;}
                return;
            }
            if(SceneManager.GetActiveScene().name!=FlightSession.RangeScene)return;
            var map=UnityEngine.Object.FindFirstObjectByType<SpaceMapRuntime>();
            if(!map||!map.BuildIsComplete||!GameObject.Find("PlayerShip"))return;
            string json=FormattableString.Invariant($"{{\"seconds\":{timer.Elapsed.TotalSeconds:F3},\"instances\":{map.Structures.Count},\"createdColliders\":{map.Structures.Sum(s=>s.GetComponents<Collider>().Length)},\"logicalDetailedBoxes\":{map.Structures.Sum(s=>s.ColliderCount)},\"progressCaptured\":{capture.ToString().ToLowerInvariant()},\"playerDelayedUntilReady\":{waitedForMap.ToString().ToLowerInvariant()},\"inputRestored\":{(!Starframe.App.GameInput.Suspended).ToString().ToLowerInvariant()},\"note\":\"Warm Editor reload, not a cold Player benchmark.\"}}");
            System.IO.File.WriteAllText(output+"loading-check.json",json);EditorApplication.update-=observe;
        };
        EditorApplication.update+=observe;app.EnterRange();
        if(!RangeLoadingOverlay.IsLoading){EditorApplication.update-=observe;throw new InvalidOperationException("Range validation prevented launch.");}
        return "Loading progress and completion are being captured.";
    }
}
