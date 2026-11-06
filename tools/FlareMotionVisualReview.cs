using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Starframe.Combat;
using Starframe.Flight;

public static class FlareMotionVisualReview
{
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required.");
        var scene=EditorSceneManager.NewPreviewScene();var random=UnityEngine.Random.state;
        var previousTarget=RenderTexture.active;
        var target=new RenderTexture(960,540,24,RenderTextureFormat.ARGBHalf);
        var image=new Texture2D(960,540,TextureFormat.RGB24,false);
        var definition=ScriptableObject.CreateInstance<FlareDefinition>();
        GameObject Root(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
        try
        {
            UnityEngine.Random.InitState(152);
            var world=Root("Flare review").AddComponent<FlareWorld>();world.Initialize();
            var owner=Root("Visual-only owner").AddComponent<FlightShipRuntime>();
            var camera=Root("Review camera").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.09f,.28f);
            camera.fieldOfView=50;camera.nearClipPlane=.1f;camera.farClipPlane=300;camera.targetTexture=target;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var volume=Root("Native range post FX").AddComponent<Volume>();volume.isGlobal=true;
            volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Starframe/Settings/Rendering/SpaceTestRangePostFX.asset");
            var inherited=new Vector3(350,70,-90);
            world.EmitBurst(owner,definition,Vector3.zero,Vector3.up,inherited);
            var present=typeof(FlareWorld).GetMethod("Present",BindingFlags.Instance|BindingFlags.NonPublic);
            string output=Path.GetFullPath("Documentation/FlareMotion-2026-11-06");Directory.CreateDirectory(output);
            int ticks=0;var files=new System.Collections.Generic.List<string>();
            foreach(double time in new[]{.8,.816666667,.833333333,1.20,1.216666667,1.233333333})
            {
                while((ticks+1)*.02<=time+1e-9){world.Step(.02f);ticks++;}
                present.Invoke(world,new object[]{(float)((time-ticks*.02)/.02)});
                camera.transform.position=inherited*((float)time-.02f)+new Vector3(0,22,-42);
                camera.transform.rotation=Quaternion.identity;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
                string path=Path.Combine(output,"moving-"+files.Count+".png");File.WriteAllBytes(path,image.EncodeToPNG());files.Add(path);
            }
            return new{files,activeSparks=world.ActiveSparkCount,trailCount=world.GetComponentsInChildren<TrailRenderer>().Length,mode="Native offscreen render, 350 m/s translation, interpolated 60 Hz samples, native range post FX"};
        }
        finally
        {
            RenderTexture.active=previousTarget;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(definition);EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Random.state=random;
        }
    }
}
