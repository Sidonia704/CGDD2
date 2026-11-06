using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TMPro;
using Starframe.Construction;
using Starframe.Flight;

// Isolated native UI preview; never invokes Start, touches workspace storage or saves a scene.
public static class MirrorGroupUiPreview
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private).SetValue(owner,value);
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required.");
        var previous=BlockModules.ConfiguredDatabase;var random=UnityEngine.Random.state;var oldTarget=RenderTexture.active;
        var scene=EditorSceneManager.NewPreviewScene();RenderTexture output=null;ShipEditorUI ui=null;
        GameObject Root(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
        try
        {
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            var host=Root("Native editor UI preview");var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);
            Set(assembly,"bodyMaterial",tuning.database.Get(BlockShape.Cube).material);Set(assembly,"coreMaterial",tuning.database.Get(BlockShape.Core).material);
            Set(assembly,"edgeMaterial",tuning.database.Get(BlockShape.Cube).material);Set(assembly,"forwardRayMaterial",tuning.database.Get(BlockShape.Core).material);
            if(!assembly.Place(BlockShape.Core,Vector3.up*.5f,Quaternion.identity)||!assembly.Place(BlockShape.Gun1,new Vector3(0,1.5f,0),Quaternion.identity))throw new InvalidOperationException(assembly.PlacementFailure);
            var world=Root("Preview world camera").AddComponent<Camera>();world.enabled=false;world.scene=scene;world.cullingMask=1<<30;world.clearFlags=CameraClearFlags.SolidColor;world.backgroundColor=new Color32(14,50,142,255);
            world.transform.position=new Vector3(0,5,-15);world.transform.LookAt(Vector3.zero);world.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var display=Root("Preview UI camera").AddComponent<Camera>();display.scene=scene;display.cullingMask=1<<5;display.clearFlags=CameraClearFlags.SolidColor;display.backgroundColor=new Color32(14,50,142,255);display.nearClipPlane=.01f;display.farClipPlane=20;display.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            output=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);output.Create();display.targetTexture=output;
            var orbit=host.AddComponent<WorkbenchOrbit>();orbit.enabled=false;Set(orbit,"viewCamera",world);
            var editor=host.AddComponent<ShipEditorController>();editor.enabled=false;Set(editor,"assembly",assembly);Set(editor,"orbit",orbit);Set(editor,"mirrorMode",true);
            ui=host.AddComponent<ShipEditorUI>();ui.enabled=false;Set(ui,"flightTuning",tuning);Set(ui,"presentationCamera",display);
            Set(ui,"backdropBlurShader",Shader.Find("Starframe/UI/BackdropBlur"));Set(ui,"moduleIcons",Array.Empty<Sprite>());Set(ui,"moduleSelectedIcons",Array.Empty<Sprite>());Set(editor,"ui",ui);
            ui.Initialize(editor,world);ui.BlueprintName="MIRROR PREVIEW";ui.SetMirror(true);
            void Capture(string file)
            {
                Canvas.ForceUpdateCanvases();foreach(var text in ui.GetComponentsInChildren<TextMeshProUGUI>(true))text.ForceMeshUpdate();
                if(world.targetTexture)RenderPipeline.SubmitRenderRequest(world,new UniversalRenderPipeline.SingleCameraRequest{destination=world.targetTexture});
                RenderPipeline.SubmitRenderRequest(display,new UniversalRenderPipeline.SingleCameraRequest{destination=output});
                RenderTexture.active=output;var pixels=new Texture2D(output.width,output.height,TextureFormat.RGBA32,false);
                try{pixels.ReadPixels(new Rect(0,0,output.width,output.height),0,0);pixels.Apply();File.WriteAllBytes(file,pixels.EncodeToPNG());}
                finally{UnityEngine.Object.DestroyImmediate(pixels);}
            }
            Directory.CreateDirectory("Documentation/FleetRevision6");Capture("Documentation/FleetRevision6/mirror-editor-ui.png");
            ui.ToggleWeaponGroups();Capture("Documentation/FleetRevision6/weapon-groups-ui.png");
            var minus=ui.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Minus");
            var mirror=ui.CanvasRoot.GetComponentsInChildren<Transform>(true).First(t=>t.name=="MirrorTool");
            return new{mirrorButton=mirror.name,mirrorActive=editor.MirrorMode,minusPresent=minus!=null,minusVisible=minus&&minus.gameObject.activeInHierarchy,
                files=new[]{"Documentation/FleetRevision6/mirror-editor-ui.png","Documentation/FleetRevision6/weapon-groups-ui.png"}};
        }
        finally
        {
            RenderTexture.active=oldTarget;
            // UI owns a temporary world RT; release explicitly because edit-mode teardown uses Destroy.
            if(ui){var rt=(RenderTexture)typeof(ShipEditorUI).GetField("sceneTexture",Private).GetValue(ui);Set(ui,"sceneTexture",null);if(rt){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
                var materials=(System.Collections.Generic.List<Material>)typeof(ShipEditorUI).GetField("runtimeMaterials",Private).GetValue(ui);
                foreach(var material in materials)if(material)UnityEngine.Object.DestroyImmediate(material);materials.Clear();}
            EditorSceneManager.ClosePreviewScene(scene);
            if(output){output.Release();UnityEngine.Object.DestroyImmediate(output);}
            BlockModules.Configure(previous);UnityEngine.Random.state=random;
        }
    }
}
