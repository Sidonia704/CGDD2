using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

// External, isolated preview only: does not save a scene, profile, blueprint or native asset.
public static class VlsHudPreview
{
    public static object Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required.");
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        var previousDatabase=BlockModules.ConfiguredDatabase;var random=UnityEngine.Random.state;
        var previousTarget=RenderTexture.active;var owned=new List<UnityEngine.Object>();
        var scene=EditorSceneManager.NewPreviewScene();RenderTexture target=null;Texture2D pixels=null;ProjectileWorld projectiles=null;
        GameObject Root(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
        T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
        try
        {
            var native=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            if(!native)throw new InvalidOperationException("Native module database missing.");
            var database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            database.modules=new List<ModuleDefinition>(native.modules);
            foreach(var shape in new[]{BlockShape.VLS1,BlockShape.VLS2})
            {
                int index=database.modules.FindIndex(m=>m&&m.shape==shape);
                if(index<0)throw new InvalidOperationException("Install native "+shape+" first.");
                var module=Own(UnityEngine.Object.Instantiate(database.modules[index]));
                module.weapon=Own(UnityEngine.Object.Instantiate(module.weapon));module.weapon.sustainedPowerWatts=0;
                database.modules[index]=module;
            }
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            var host=Root("VLS HUD native fixture");host.transform.position=new Vector3(0,20000,0);
            var body=host.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(database);
            void Place(BlockShape shape,Vector3 position)
            {if(!assembly.Place(shape,position,Quaternion.identity))throw new InvalidOperationException(shape+": "+assembly.PlacementFailure);}
            // Connected native core/support floor; all VLS bottom mount faces touch the floor.
            for(int x=0;x<11;x++)for(int z=0;z<2;z++)Place(x==0&&z==0?BlockShape.Core:BlockShape.Cube,new Vector3(x,.5f,z));
            var shapes=new[]{BlockShape.VLS2,BlockShape.VLS2,BlockShape.VLS1,BlockShape.VLS1};
            for(int i=0;i<shapes.Length;i++)Place(shapes[i],new Vector3(i*3,1.5f,0));
            var ship=host.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export("Unstored VLS HUD fixture"),tuning);
            projectiles=new ProjectileWorld();projectiles.Initialize(null);
            var weapons=new ShipWeapons();weapons.Initialize(ship,projectiles);
            // Inject only the two cooldown instants. Everything drawn comes through real GetHudWeaponStates/Refresh.
            var slots=(IEnumerable)typeof(ShipWeapons).GetField("guns",Private).GetValue(weapons);var counts=new Dictionary<int,int>();
            foreach(var slot in slots)
            {
                var type=slot.GetType();var definition=(WeaponDefinition)type.GetField("definition").GetValue(slot);
                if(!definition.IsVls)continue;
                counts.TryGetValue(definition.magazineSize,out int index);counts[definition.magazineSize]=index+1;
                if(index==1){type.GetField("reloadRemaining").SetValue(slot,20f);type.GetField("ammoRemaining").SetValue(slot,0);}
            }
            var camera=Root("VLS HUD offscreen camera").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.orthographic=true;camera.orthographicSize=65;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=200;
            camera.transform.position=new Vector3(-84,50,-100);camera.transform.rotation=Quaternion.identity;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color32(18,57,148,255);camera.cullingMask=1<<30;camera.allowHDR=false;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;
            var ui=new GameObject("VLS HUD native graphic",typeof(RectTransform));SceneManager.MoveGameObjectToScene(ui,scene);ui.layer=30;
            var canvas=ui.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
            var canvasRect=ui.GetComponent<RectTransform>();canvasRect.sizeDelta=new Vector2(376,324);
            var child=new GameObject("CombatWeaponStatusGraphic",typeof(RectTransform),typeof(CanvasRenderer));child.transform.SetParent(ui.transform,false);child.layer=30;
            var rect=child.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var graphic=child.AddComponent<CombatWeaponStatusGraphic>();graphic.Bind(ship,weapons,null);graphic.Refresh();
            foreach(var transform in ui.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=30;
            Canvas.ForceUpdateCanvases();graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender);
            foreach(var text in ui.GetComponentsInChildren<TextMeshProUGUI>(true))text.ForceMeshUpdate();
            target=new RenderTexture(650,650,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;pixels=new Texture2D(650,650,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,650,650),0,0);pixels.Apply();
            string path=Path.GetFullPath("Documentation/WeaponVls5/vls-hud.png");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pixels.EncodeToPNG());
            var states=new List<WeaponHudState>();weapons.GetHudWeaponStates(states);var bounds=new List<Rect>();CombatWeaponStatusGraphic.PackMissiles(states,bounds);
            return new{path,mounts=states.Select(s=>new{s.Definition.name,s.AmmoRemaining,s.AmmoCapacity,s.ReloadRemaining}).ToArray(),
                rects=bounds.Select(b=>new{b.x,b.y,b.width,b.height}).ToArray(),timers=ui.GetComponentsInChildren<TextMeshProUGUI>().Select(t=>t.text).ToArray()};
        }
        finally
        {
            projectiles?.Clear();
            RenderTexture.active=previousTarget;
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels);if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            EditorSceneManager.ClosePreviewScene(scene);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);
            BlockModules.Configure(previousDatabase);UnityEngine.Random.state=random;
        }
    }
}
