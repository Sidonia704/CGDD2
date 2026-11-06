using System;
using Starframe.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShrinkRingRocks
{
    public static object Begin() => UniformBackdropBuilder.Begin();
    public static object Status() => UniformBackdropBuilder.Status();
    public static string Finish()
    {
        var status=UniformBackdropBuilder.Status();
        var type=status.GetType();
        if((bool)type.GetProperty("running").GetValue(status))throw new InvalidOperationException("Resource generation is still running.");
        if((int)type.GetProperty("completed").GetValue(status)!=16||(int)type.GetProperty("errors").GetValue(status)!=0)
            throw new InvalidOperationException("Resource generation did not finish: "+type.GetProperty("latestError").GetValue(status));
        UniformBackdropBuilder.InstallToScene("Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity");
        var sky=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/SimulationTrial/SaturnRing/SaturnRingSky.mat");
        sky.SetColor("_Background",new Color(.128f,.272f,.52f));
        sky.SetColor("_PlanetColor",new Color(.9f,.36f,.04875f));
        sky.SetColor("_NightColor",new Color(.135f,.054f,.0073125f));
        EditorUtility.SetDirty(sky);AssetDatabase.SaveAssets();
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/_Starframe/Scenes/Frontend/Hangar.unity")
            EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Frontend/Hangar.unity");
        return "Smaller native ring assets installed, density increased 15%, vivid palette restored; stopped in Hangar. No tests run.";
    }
}
