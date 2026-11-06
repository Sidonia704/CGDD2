using System;
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
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

// External EditMode-only inspection. Creates temporary fixtures and renders the real targeting graphic.
// No native assets, blueprints or scenes are saved; the root caller runs this explicitly.
public static class SharedLeadPreview
{
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const string Output="Documentation/SharedLead-2026-11-07";
    static Type FixtureType(string name)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(assembly=>assembly.GetType("Starframe.Construction.Tests."+name)).FirstOrDefault(value=>value!=null);
        if(type==null)throw new InvalidOperationException("Compile the Editor tests first: "+name);
        return type;
    }
    static object Invoke(object fixture,string name,params object[] arguments)
    {
        var method=fixture.GetType().GetMethod(name,Instance);
        if(method==null)throw new MissingMethodException(fixture.GetType().FullName,name);
        try{return method.Invoke(fixture,arguments);}
        catch(TargetInvocationException exception){throw exception.InnerException??exception;}
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void MoveOwnedRoots(object fixture,Scene scene)
    {
        var values=(IEnumerable<UnityEngine.Object>)fixture.GetType().GetField("owned",Instance).GetValue(fixture);
        foreach(var value in values)if(value is GameObject root&&root.transform.parent==null)SceneManager.MoveGameObjectToScene(root,scene);
    }

    public static object Run()
    {
        Require(!Application.isPlaying,"Stop Play Mode before the shared-lead preview.");
        var previousDatabase=BlockModules.ConfiguredDatabase;var previousRandom=UnityEngine.Random.state;
        var previousTexture=RenderTexture.active;var scene=EditorSceneManager.NewPreviewScene();
        object shared=null,sensor=null;bool sharedSetup=false,sensorSetup=false;
        GameObject ui=null;RenderTexture texture=null;Texture2D image=null;Camera camera=null;
        try
        {
            sensor=Activator.CreateInstance(FixtureType("TargetSensorWorldTests"));Invoke(sensor,"SetUp");sensorSetup=true;
            var lockArguments=new object[4];
            var targeting=(TargetLockSystem)Invoke(sensor,"LockFixture",lockArguments);
            var world=(RangeCombatWorld)lockArguments[0];camera=(Camera)lockArguments[1];
            var player=(RangeCombatWorld.Actor)lockArguments[2];var enemy=(RangeCombatWorld.Actor)lockArguments[3];

            shared=Activator.CreateInstance(FixtureType("SharedLeadPointTests"));Invoke(shared,"Setup");sharedSetup=true;
            var createArguments=new object[]{null,new[]{600f,600f,600f},new[]{2000f,2000f,2000f},false,WeaponDelivery.Projectile};
            var weapons=(ShipWeapons)Invoke(shared,"Create",createArguments);var owner=(FlightShipRuntime)createArguments[0];
            player.ship=owner;player.body=owner.GetComponent<Rigidbody>();player.weapons=weapons;
            MoveOwnedRoots(sensor,scene);MoveOwnedRoots(shared,scene);
            // Fixture meshes intentionally have no game materials; capture the native HUD alone.
            foreach(var root in scene.GetRootGameObjects())foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())renderer.enabled=false;
            var enemyPosition=owner.transform.position+Vector3.forward*500;
            enemy.ship.transform.position=enemyPosition;enemy.body.position=enemyPosition;
            enemy.flight.GetType().GetField("velocity",Instance).SetValue(enemy.flight,new Vector3(50,0,0));
            Physics.SyncTransforms();

            texture=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);texture.Create();
            camera.enabled=false;camera.scene=scene;camera.targetTexture=texture;
            camera.pixelRect=new Rect(0,0,1280,720);camera.aspect=1280f/720;camera.fieldOfView=45;
            camera.nearClipPlane=.1f;camera.farClipPlane=10000;camera.allowHDR=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color32(11,24,49,255);
            camera.transform.position=owner.CorePosition+new Vector3(0,3,-15);
            camera.transform.rotation=Quaternion.LookRotation(enemy.ship.CorePosition-camera.transform.position);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;

            ui=new GameObject("Shared lead real targeting UI",typeof(RectTransform));SceneManager.MoveGameObjectToScene(ui,scene);
            var canvas=ui.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=ui.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
            var graphicObject=new GameObject("TargetAimGraphic",typeof(RectTransform),typeof(CanvasRenderer));graphicObject.transform.SetParent(ui.transform,false);
            var rect=graphicObject.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var graphic=graphicObject.AddComponent<TargetAimGraphic>();graphic.Initialize(camera,targeting,weapons);
            image=new Texture2D(1280,720,TextureFormat.RGBA32,false);Directory.CreateDirectory(Output);

            world.Sensors.Update(world.Actors,0);
            targeting.ToggleLockAt(camera.WorldToScreenPoint(enemy.ship.CorePosition));
            Require(targeting.HasTarget,"The real sensor/lock fixture did not acquire the visible target.");
            var snapshots=new List<object>();
            object Snapshot(string label)
            {
                var profiles=new List<WeaponAimProfile>();weapons.GetLeadProfiles(profiles,true);
                return new{label,leadCount=targeting.LeadPoints.Count,profiles=profiles.Select(profile=>new{speed=profile.projectileSpeed,range=profile.range,count=profile.gunCount}).ToArray(),
                    points=targeting.LeadPoints.Select(point=>new{speed=point.projectileSpeed,time=point.flightTime,x=point.point.x,y=point.point.y,z=point.point.z}).ToArray()};
            }
            void Capture(string name)
            {
                graphic.SetVerticesDirty();Canvas.ForceUpdateCanvases();graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender);
                var mesh=graphic.canvasRenderer.GetMesh();int vertices=mesh?mesh.vertexCount:0;
                Require(vertices>0,"Real TargetAimGraphic produced no geometry.");
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});
                RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
            }

            targeting.RefreshLeadPoints();
            Require(targeting.LeadPoints.Count==1&&targeting.LeadPoints[0].projectileSpeed==600,"Three equal-speed sizes must produce one 600m/s lead.");
            var baseline=new List<WeaponAimProfile>();weapons.GetLeadProfiles(baseline);
            Require(baseline.Count==1&&baseline[0].gunCount==3,"All three distinct-size mounts must contribute to the shared lead.");
            Require(targeting.EstimatedCoreVelocity.x>49,"Target motion fallback must reach the real lead pipeline.");
            snapshots.Add(Snapshot("same-speed"));Capture("same-speed");

            var changed=owner.Tuning.GetDefinition(BlockShape.Gun3).weapon;
            Require(!AssetDatabase.Contains(changed),"Preview must only modify fixture-owned weapons.");changed.projectileSpeed=900;
            weapons.Prepare();targeting.RefreshLeadPoints();
            Require(targeting.LeadPoints.Count==2,"Changing one clone to 900m/s must produce two real lead points.");
            Require(targeting.LeadPoints[0].projectileSpeed==600&&targeting.LeadPoints[1].projectileSpeed==900,"Real lead points must preserve speed order.");
            snapshots.Add(Snapshot("mixed-speeds"));Capture("mixed-speeds");

            weapons.SetGroupEnabled(weapons.GetGroupId("large"),false);weapons.Prepare();targeting.RefreshLeadPoints();
            Require(targeting.LeadPoints.Count==1&&targeting.LeadPoints[0].projectileSpeed==600,"Disabling the 900m/s group must remove only that lead.");
            snapshots.Add(Snapshot("disabled-900"));Capture("disabled-speed");
            return new{sameSpeed=Path.GetFullPath(Output+"/same-speed.png"),mixedSpeeds=Path.GetFullPath(Output+"/mixed-speeds.png"),
                disabledSpeed=Path.GetFullPath(Output+"/disabled-speed.png"),pipeline=snapshots.ToArray(),playModeEntered=false,nativeAssetsModified=false};
        }
        finally
        {
            if(camera)camera.targetTexture=null;
            RenderTexture.active=previousTexture;
            if(ui)UnityEngine.Object.DestroyImmediate(ui);
            if(image)UnityEngine.Object.DestroyImmediate(image);
            if(texture){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
            try{if(sensorSetup)Invoke(sensor,"TearDown");}
            finally
            {
                try{if(sharedSetup)Invoke(shared,"Cleanup");}
                finally{EditorSceneManager.ClosePreviewScene(scene);BlockModules.Configure(previousDatabase);UnityEngine.Random.state=previousRandom;}
            }
        }
    }
}
