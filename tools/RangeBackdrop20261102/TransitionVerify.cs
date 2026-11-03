using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Flight;
using Starframe.Combat;
using Starframe.Maps;

public static class RangeTransitionVerify
{
    const string DirectoryPath="Documentation/RangeTransition-2026-11-02";
    public static string Open()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before opening.");
        EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Testing/FlightRange.unity",OpenSceneMode.Single);
        return "Opened range for transition check.";
    }
    public static string View(int mode)
    {
        var world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
        var hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();
        if(!Application.isPlaying||!world||world.Player==null||!hud||RangeLoadingOverlay.IsLoading)return "Still loading";
        world.SetBotMode(RangeBotMode.Stationary,false);
        world.Player.flight.SetExternalInput(Vector3.zero,Vector2.zero);
        var camera=hud.WorldCamera;
        var follow=camera.GetComponent<FlightChaseCamera>();if(follow)follow.enabled=mode==0;
        if(mode==1){camera.transform.position=new Vector3(0,3500,-4500);camera.transform.LookAt(new Vector3(0,-1330,1400));}
        if(mode==2){camera.transform.position=new Vector3(900,-600,850);camera.transform.LookAt(new Vector3(0,-1330,2800));}
        return "Set view "+mode;
    }
    public static string Capture(string name)
    {
        Directory.CreateDirectory(DirectoryPath);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(DirectoryPath+"/"+name+".png"));
        var b=UnityEngine.Object.FindFirstObjectByType<RangeAsteroidBackdrop>();
        var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
        var structures=UnityEngine.Object.FindObjectsByType<VoxelStructureRuntime>(FindObjectsSortMode.None);
        var masses=structures.Where(s=>s.Body&&s.AliveCount>0).Select(s=>s.Body.mass).OrderByDescending(m=>m).ToArray();
        return "rocks="+b.totalRockCount+", visible="+b.visibleRockCount+", batches="+b.visibleBatchCount+", tris="+b.estimatedTriangles+
            "; shipMass="+w.Player.body.mass+"; maxSceneryMass="+(masses.Length>0?masses[0]:0)+
            "; scene="+SceneManager.GetActiveScene().path;
    }
}
