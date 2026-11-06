using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Starframe.Flight;

// Native material/value installation and isolated still renders only. No gameplay is simulated.
public static class CoreEnergyVisualReview
{
    const string Root="Assets/_Starframe/";
    const string Output="Documentation/CoreEnergy-2026-11-07";
    static ModuleDefinition Module(string name)=>AssetDatabase.LoadAssetAtPath<ModuleDefinition>(Root+"Settings/Modules/"+name+".asset");
    static void Backup(UnityEngine.Object asset)
    {
        string path=AssetDatabase.GetAssetPath(asset),destination=Output+"/before/"+path;
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        if(!File.Exists(destination))File.Copy(path,destination);
    }
    public static object Before()=>Capture("before");
    public static object After()=>Capture("after");
    public static object Install()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required.");
        var core=Module("Core");var smallPower=Module("Power");var material=core.material;
        if(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material))!="e4d142f3f18dbc34383de94cb246b9d2")throw new Exception("Unexpected actual core material.");
        var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>(Root+"Settings/Flight/FlightTuning.asset");
        Backup(core);Backup(material);Backup(tuning);
        Undo.RecordObject(tuning,"Boost cost multiplier four");tuning.boostPowerCostMultiplier=4;EditorUtility.SetDirty(tuning);AssetDatabase.SaveAssetIfDirty(tuning);
        Undo.RecordObjects(new UnityEngine.Object[]{core,material},"Core green emission and half small power");
        core.generationWatts=smallPower.generationWatts*.5f;core.storageJoules=smallPower.storageJoules*.5f;
        material.SetColor("_BaseColor",new Color(.14f,.56f,.025f,1));
        material.SetColor("_Color",new Color(.14f,.56f,.025f,1));
        material.SetColor("_EmissionColor",new Color(.12f,2.4f,.04f,1));
        material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(core);EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(core);AssetDatabase.SaveAssetIfDirty(material);
        return new{core.generationWatts,core.storageJoules,tuning.boostPowerCostMultiplier,material=AssetDatabase.GetAssetPath(material),emission=material.GetColor("_EmissionColor"),enabled=material.IsKeywordEnabled("_EMISSION")};
    }
    public static object Capture(string stage)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required.");
        if(UniversalRenderPipeline.asset==null||!UniversalRenderPipeline.asset.supportsHDR)throw new Exception("HDR URP is required.");
        var scene=EditorSceneManager.NewPreviewScene();var oldTarget=RenderTexture.active;
        var target=new RenderTexture(960,640,24,RenderTextureFormat.ARGBHalf);target.Create();
        var image=new Texture2D(960,640,TextureFormat.RGB24,false);
        VolumeProfile profile=null;
        GameObject RootObject(string name){var go=new GameObject(name){layer=30};SceneManager.MoveGameObjectToScene(go,scene);return go;}
        var paths=new System.Collections.Generic.List<string>();
        var diagnostics=new System.Collections.Generic.List<object>();
        try
        {
            var core=Module("Core");var model=RootObject("Actual core mesh and material");
            model.AddComponent<MeshFilter>().sharedMesh=core.mesh;model.AddComponent<MeshRenderer>().sharedMaterial=core.material;
            var camera=RootObject("HDR native visual camera").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.065f);
            camera.allowHDR=true;camera.orthographic=true;camera.orthographicSize=1.65f;camera.nearClipPlane=.01f;camera.farClipPlane=100;
            var direction=new Vector3(1.4f,1.05f,-1.6f).normalized;
            camera.transform.SetPositionAndRotation(core.mesh.bounds.center+direction*10,Quaternion.LookRotation(-direction));
            camera.cullingMask=1<<30;camera.targetTexture=target;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.volumeLayerMask=1<<30;data.volumeTrigger=camera.transform;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var light=RootObject("Directional reference light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.color=new Color(1,.94f,.85f);light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(40,-35,0);
            var volume=RootObject("Actual native post FX").AddComponent<Volume>();volume.isGlobal=true;volume.priority=1000;
            Directory.CreateDirectory(Output);
            foreach(string mode in new[]{"assembly","flight"})
            {
                var source=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"Settings/Rendering/"+(mode=="assembly"?"ConstructionPostFX":"SpaceTestRangePostFX")+".asset");
                profile=ScriptableObject.CreateInstance<VolumeProfile>();
                foreach(var component in source.components){var copy=UnityEngine.Object.Instantiate(component);profile.components.Add(copy);if(mode=="assembly"&&(copy is Tonemapping||copy is ColorAdjustments||copy is Vignette))copy.active=false;}
                // Mirror SettingsPostProcessing's default bloom and assembly grading behavior.
                if(profile.TryGet<Bloom>(out var bloom))bloom.intensity.Override(.65f);
                volume.enabled=false;volume.sharedProfile=profile;volume.enabled=true;
                VolumeManager.instance.Update(camera.transform,1<<30);
                // Warm the new camera/renderer and volume stack before GPU readback.
                // The first isolated URP submission can initialize resources without a complete image.
                for(int warmup=0;warmup<3;warmup++)
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                var stack=VolumeManager.instance.stack;
                diagnostics.Add(new{mode,bloom=stack.GetComponent<Bloom>().intensity.value,threshold=stack.GetComponent<Bloom>().threshold.value,tone=stack.GetComponent<Tonemapping>().mode.value.ToString(),post=data.renderPostProcessing,hdr=camera.allowHDR,background=camera.backgroundColor.ToString()});
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();
                string path=Path.GetFullPath(Output+"/core-"+stage+"-"+mode+".png");File.WriteAllBytes(path,image.EncodeToPNG());paths.Add(path);
                volume.sharedProfile=null;foreach(var component in profile.components)UnityEngine.Object.DestroyImmediate(component);UnityEngine.Object.DestroyImmediate(profile);profile=null;
            }
            var report=new{paths,diagnostics,mode="Actual core mesh/material, native post FX, default settings .65 bloom; no gameplay simulation"};
            var json=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
            File.WriteAllText(Output+"/core-"+stage+"-capture.json",(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report}));
            return report;
        }
        finally
        {
            RenderTexture.active=oldTarget;
            if(profile){foreach(var component in profile.components)UnityEngine.Object.DestroyImmediate(component);UnityEngine.Object.DestroyImmediate(profile);}
            UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
