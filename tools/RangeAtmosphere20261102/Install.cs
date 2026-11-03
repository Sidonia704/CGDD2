using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Starframe.Flight;
public static class RangeAtmosphereInstall
{
    const string Root="Assets/_Starframe/";
    const string ScenePath=Root+"Scenes/Testing/FlightRange.unity";
    const string ProfilePath=Root+"Settings/Rendering/RangeAtmospherePostFX.asset";
    const string TuningPath=Root+"Settings/Rendering/RangeAtmosphereTuning.asset";
    const string Materials=Root+"Art/Materials/Environment/";
    public static string Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        var pipeline=UniversalRenderPipeline.asset;if(!pipeline||!pipeline.supportsHDR)throw new InvalidOperationException("URP HDR is required.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var roots=scene.GetRootGameObjects();var volume=roots.SelectMany(r=>r.GetComponentsInChildren<Volume>(true)).First(v=>v.isGlobal);
            var original=volume.sharedProfile;if(!original)throw new InvalidOperationException("Missing volume profile.");
            var flight=AssetDatabase.LoadAssetAtPath<FlightTuning>(Root+"Settings/Flight/FlightTuning.asset");
            var key=roots.SelectMany(r=>r.GetComponentsInChildren<Light>(true)).First(l=>l.name=="Directional Star Light");
            var fill=roots.SelectMany(r=>r.GetComponentsInChildren<Light>(true)).First(l=>l.name=="Space Fill");
            Directory.CreateDirectory("Documentation/RangeAtmosphere-2026-11-02");
            if(!File.Exists("Documentation/RangeAtmosphere-2026-11-02/before.json"))
            {
                string report=FormattableString.Invariant($"{{\"profile\":\"{AssetDatabase.GetAssetPath(original)}\",\"gridWallSpacing\":{flight.gridWallSpacing},\"gridCellSize\":{flight.gridCellSize},\"gridOpacity\":1,\"fillIntensity\":{fill.intensity},\"keyIntensity\":{key.intensity}}}");
                File.WriteAllText("Documentation/RangeAtmosphere-2026-11-02/before.json",report);
            }
            if(!AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath)&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original),ProfilePath))throw new InvalidOperationException("Could not copy native profile.");
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            var bloom=Get<Bloom>(profile);bloom.active=true;bloom.threshold.Override(1.05f);bloom.intensity.Override(.8f);bloom.scatter.Override(.64f);bloom.tint.Override(new Color(.94f,.98f,1));
            bloom.highQualityFiltering.Override(false);bloom.downscale.Override(BloomDownscaleMode.Quarter);bloom.maxIterations.Override(5);
            var dof=Get<DepthOfField>(profile);dof.active=true;dof.mode.Override(DepthOfFieldMode.Off);
            var color=Get<ColorAdjustments>(profile);color.contrast.Override(10);color.saturation.Override(-6);
            var vignette=Get<Vignette>(profile);vignette.intensity.Override(.15f);
            foreach(var component in profile.components)EditorUtility.SetDirty(component);EditorUtility.SetDirty(profile);
            volume.sharedProfile=profile;volume.enabled=true;volume.isGlobal=true;volume.weight=1;
            if(!volume.GetComponent<Starframe.App.SettingsPostProcessing>())volume.gameObject.AddComponent<Starframe.App.SettingsPostProcessing>();
            var camera=roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
            camera.allowHDR=true;var cameraData=camera.GetUniversalAdditionalCameraData();cameraData.renderPostProcessing=true;cameraData.requiresDepthTexture=true;cameraData.volumeLayerMask|=1<<volume.gameObject.layer;
            if(cameraData.scriptableRenderer==null)throw new InvalidOperationException("Missing renderer.");
            flight.gridWallSpacing=600;flight.gridCellSize=12;flight.gridOpacity=.42f;EditorUtility.SetDirty(flight);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"Art/Shaders/RangeAtmosphereParticle.shader");
            if(!shader||!shader.isSupported||ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))throw new InvalidOperationException("Atmosphere shader is not ready.");
            var dustTexture=Density("RangeDustDensity",false);var hazeTexture=Density("RangeHazeDensity",true);
            var tuning=AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>(TuningPath);
            if(!tuning){tuning=ScriptableObject.CreateInstance<RangeAtmosphereTuning>();AssetDatabase.CreateAsset(tuning,TuningPath);}
            tuning.dustMaterial=Material("RangeDust",shader,dustTexture,1.5f,1.15f);
            tuning.hazeMaterial=Material("RangeHaze",shader,hazeTexture,18,1);
            EditorUtility.SetDirty(tuning);
            var environment=roots.SelectMany(r=>r.GetComponentsInChildren<RangeEnvironment>(true)).First();
            var atmosphere=environment.GetComponent<RangeAtmosphere>();if(!atmosphere)atmosphere=environment.gameObject.AddComponent<RangeAtmosphere>();
            var so=new SerializedObject(atmosphere);so.FindProperty("tuning").objectReferenceValue=tuning;so.FindProperty("volume").objectReferenceValue=volume;so.FindProperty("starLight").objectReferenceValue=key;
            var map=roots.SelectMany(r=>r.GetComponentsInChildren<Starframe.Maps.SpaceMapRuntime>(true)).FirstOrDefault();so.FindProperty("beltAnchor").objectReferenceValue=map?map.transform:null;so.ApplyModifiedPropertiesWithoutUndo();
            // Reuse the existing two lights, keeping the same lighting-pass count and shadow budget.
            key.color=new Color(1,.94f,.86f);fill.intensity=.38f;
            // Author fog in the scene too, so Player shader stripping retains the required fog variant.
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=tuning.fogDensity;RenderSettings.fogColor=tuning.fogColor;
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save range.");
            AssetDatabase.SaveAssets();
            return "Installed range-only atmosphere: grid 600m/12m, opacity .42; quarter bloom; depth of field off; dust<=160, haze<=12, no particle collisions/lights.";
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
    static T Get<T>(VolumeProfile p)where T:VolumeComponent,new()
    {
        if(p.TryGet<T>(out var component))return component;
        component=p.Add<T>(true);AssetDatabase.AddObjectToAsset(component,p);return component;
    }
    static Material Material(string name,Shader shader,Texture texture,float softness,float gain)
    {
        string path=Materials+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(shader){name=name};AssetDatabase.CreateAsset(material,path);}
        material.shader=shader;material.SetTexture("_MainTex",texture);material.SetFloat("_Softness",softness);material.SetFloat("_Gain",gain);EditorUtility.SetDirty(material);return material;
    }
    static Texture2D Density(string name,bool noise)
    {
        string path=Materials+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing)return existing;
        const int size=128;var texture=new Texture2D(size,size,TextureFormat.R8,true,true){name=name,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        var pixels=new Color[size*size];for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float u=x/(float)(size-1)*2-1,v=y/(float)(size-1)*2-1;
            float density=Mathf.Pow(Mathf.Clamp01(1-u*u-v*v),noise?2:1.8f);
            if(noise)density*=.24f+.76f*(Mathf.PerlinNoise(x*.045f+3,y*.045f+7)*.7f+Mathf.PerlinNoise(x*.095f+11,y*.095f+1)*.3f);
            pixels[y*size+x]=new Color(density,density,density,1);
        }
        texture.SetPixels(pixels);texture.Apply(true,true);AssetDatabase.CreateAsset(texture,path);return texture;
    }
}
