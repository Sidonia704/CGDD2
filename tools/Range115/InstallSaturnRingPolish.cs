using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Flight;

public static class InstallSaturnRingPolish
{
    const string Root = "Assets/_Starframe/Art/SimulationTrial/SaturnRing";
    static Material MaterialAsset(string name, string shaderName, string source)
    {
        var shader = Shader.Find(shaderName);
        if (!shader) throw new Exception("Missing shader: " + shaderName);
        string path = Root + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(source));
            material.name = name;
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        return material;
    }
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("Install in Edit mode.");
        var scene=EditorSceneManager.OpenScene("Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity");
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/_Starframe/Art/SimulationTrial", "SaturnRing");
        var sky = MaterialAsset("SaturnRingSky", "Starframe/Environment/Saturn Stylized Sky", "Assets/_Starframe/Art/SimulationTrial/SimulationSpace.mat");
        sky.SetColor("_Background", new Color(.128f,.272f,.52f));
        sky.SetColor("_GridColor", Color.black);
        sky.SetColor("_PlanetColor", new Color(.9f,.36f,.04875f));
        sky.SetColor("_NightColor", new Color(.135f,.054f,.0073125f));
        sky.SetFloat("_BandStrength", .075f);
        sky.SetColor("_AtmosphereColor", new Color(.22f,.29f,.36f));
        sky.SetFloat("_PlanetAmbient", .14f);
        sky.SetFloat("_ShowPlanet", 1);
        var rock = MaterialAsset("SaturnRingRock", "Starframe/Environment/Saturn Ring Rock", "Assets/_Starframe/Art/Materials/Environment/RangeAsteroidBackdrop.mat");
        rock.enableInstancing = true;
        rock.SetColor("_BaseColor", new Color(.62f,.61f,.59f));
        rock.SetFloat("_Smoothness", .18f);
        rock.SetFloat("_MacroScale", .015f);
        rock.SetFloat("_MacroVariation", .05f);
        rock.SetFloat("_ContrastReduction", .35f);
        rock.SetFloat("_HazeStart",4000);
        rock.SetFloat("_HazeEnd",24000);
        rock.SetFloat("_HazeStrength",.35f);
        string tuningPath = Root + "/SaturnRingAtmosphere.asset";
        var tuning = AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>(tuningPath);
        if (!tuning)
        {
            tuning = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>("Assets/_Starframe/Art/SimulationTrial/SimulationAtmosphere.asset"));
            tuning.name="SaturnRingAtmosphere";
            AssetDatabase.CreateAsset(tuning,tuningPath);
        }
        tuning.fogDensity=.00002f;
        tuning.fogColor=new Color(.026f,.040f,.065f);
        tuning.dustEnabled=false; tuning.hazeEnabled=false;
        RenderSettings.skybox=sky;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.20f,.27f,.40f);
        RenderSettings.ambientEquatorColor=new Color(.10f,.15f,.24f);
        RenderSettings.ambientGroundColor=new Color(.05f,.075f,.12f);
        RenderSettings.fogColor=tuning.fogColor;
        RenderSettings.fogDensity=tuning.fogDensity;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach(var backdrop in root.GetComponentsInChildren<RangeAsteroidBackdrop>(true))
            {
                var so=new SerializedObject(backdrop);
                so.FindProperty("rockMaterial").objectReferenceValue=rock;
                so.FindProperty("densityMultiplier").intValue=1;
                so.FindProperty("farFadeStart").floatValue=28000;
                so.FindProperty("farFadeEnd").floatValue=35000;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach(var atmosphere in root.GetComponentsInChildren<RangeAtmosphere>(true))
            {
                var so=new SerializedObject(atmosphere);
                so.FindProperty("tuning").objectReferenceValue=tuning;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach(var camera in root.GetComponentsInChildren<Camera>(true)) camera.farClipPlane=Mathf.Max(camera.farClipPlane,35000);
            foreach(var light in root.GetComponentsInChildren<Light>(true))
            {
                if(light.name=="Directional Star Light")light.intensity=1.8f;
                if(light.name=="Space Fill")light.intensity=.18f;
            }
        }
        EditorUtility.SetDirty(sky);EditorUtility.SetDirty(rock);EditorUtility.SetDirty(tuning);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene))throw new Exception("Could not save Saturn mission.");
        return "Mission-specific sky, opaque ring material, atmosphere and lighting installed.";
    }
    public static string LivePalette()
    {
        var sky=AssetDatabase.LoadAssetAtPath<Material>(Root+"/SaturnRingSky.mat");
        sky.SetColor("_Background",new Color(.128f,.272f,.52f));
        sky.SetColor("_PlanetColor",new Color(.9f,.36f,.04875f));
        sky.SetColor("_NightColor",new Color(.135f,.054f,.0073125f));
        sky.SetFloat("_BandStrength",.075f);
        var rock=AssetDatabase.LoadAssetAtPath<Material>(Root+"/SaturnRingRock.mat");
        rock.SetFloat("_Smoothness",.18f);rock.SetFloat("_ContrastReduction",.35f);
        var backdrop=UnityEngine.Object.FindFirstObjectByType<RangeAsteroidBackdrop>();
        var activeMaterial=(Material)typeof(RangeAsteroidBackdrop).GetField("instanceMaterial",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(backdrop);
        activeMaterial.SetFloat("_Smoothness",.18f);activeMaterial.SetFloat("_ContrastReduction",.35f);
        RenderSettings.ambientSkyColor=new Color(.20f,.27f,.40f);
        RenderSettings.ambientEquatorColor=new Color(.10f,.15f,.24f);
        RenderSettings.ambientGroundColor=new Color(.05f,.075f,.12f);
        EditorUtility.SetDirty(sky);EditorUtility.SetDirty(rock);AssetDatabase.SaveAssets();
        return "Approved palette applied; scene lighting persists through Run after Stop.";
    }
}
