using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using TMPro;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

// External only. Root creates an empty unsaved memory scene, then CreateEdit/Play/Begin.
public static class HeatHudPreview
{
    const string Rig="HeatHudPreviewRig";
    static RangeCombatWorld combat;static RangeHUD hud;static FlightShipRuntime enemy;
    public static string CreateEdit()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("CreateEdit before Play in an empty memory scene.");
        var root=new GameObject(Rig);
        var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";cameraObject.transform.SetParent(root.transform,false);
        var camera=cameraObject.AddComponent<Camera>();cameraObject.AddComponent<AudioListener>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.012f,.025f,.05f);
        camera.nearClipPlane=.1f;camera.farClipPlane=2500;camera.fieldOfView=55;
        camera.transform.position=new Vector3(18,12,-40);camera.transform.LookAt(new Vector3(7,0,150));
        var lightObject=new GameObject("Preview light");lightObject.transform.SetParent(root.transform,false);
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2.3f;
        light.transform.rotation=Quaternion.Euler(35,-30,0);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.2f,.25f,.32f);
        return "Created memory-scene preview camera/light. Enter Play, then HeatHudPreview.Begin().";
    }
    public static string Begin()
    {
        if(!EditorApplication.isPlaying)throw new InvalidOperationException("Begin requires Play.");
        var root=GameObject.Find(Rig);if(!root)throw new InvalidOperationException("CreateEdit first.");
        var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
        var battle=AssetDatabase.LoadAssetAtPath<BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");
        var camera=Camera.main;
        var owner=Ship(root.transform,database,tuning,battle,"Player heat preview",Vector3.zero,Quaternion.identity,1);
        var ownerFlight=Flight(owner,tuning,camera);
        var hudObject=new GameObject("Preview HUD");hudObject.transform.SetParent(root.transform,false);hud=hudObject.AddComponent<RangeHUD>();
        hud.Initialize(camera,ownerFlight,tuning);
        var worldObject=new GameObject("Preview combat");worldObject.transform.SetParent(root.transform,false);combat=worldObject.AddComponent<RangeCombatWorld>();
        combat.Initialize(owner,ownerFlight,owner.GetComponent<ShipAssembly>(),tuning,battle,camera,hud,true);combat.CombatEnabled=false;
        // Four real Gun3 mounts yield a visibly nontrivial original configuration cost.
        enemy=Ship(root.transform,database,tuning,battle,"Preview Enemy",new Vector3(8,0,220),Quaternion.Euler(0,180,0),4);
        enemy.InvulnerableUntil=100000;
        var enemyFlight=Flight(enemy,tuning,camera);
        var register=typeof(RangeCombatWorld).GetMethod("Register",BindingFlags.Instance|BindingFlags.NonPublic);
        var actor=(RangeCombatWorld.Actor)register.Invoke(combat,new object[]{enemy,enemyFlight,1});actor.blueprintName="Preview Enemy";
        ((IList)typeof(RangeCombatWorld).GetField("bots",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(combat)).Add(actor);
        Physics.SyncTransforms();
        // Real per-mount integration; no synthetic telemetry, UI latch or production-asset changes.
        var weapons=combat.Player.weapons;weapons.FireRequested=true;
        for(int i=0;i<300;i++){owner.BeginStep(.02f);weapons.Prepare();weapons.Execute(.02f,1);combat.Projectiles.Step(.02f);}
        weapons.FireRequested=false;combat.Projectiles.RemoveOwned(owner);
        combat.Sensors.Update(combat.Actors,0);
        combat.Targeting.ToggleLockAt(camera.WorldToScreenPoint(enemy.CorePhysicsPosition));
        // The regular HUD/TargetLock updates now render while CombatEnabled=false freezes simulation.
        return Read();
    }
    static FlightShipRuntime Ship(Transform parent,ModuleDatabase database,FlightTuning tuning,BattleTuning battle,string name,Vector3 position,Quaternion rotation,int gunCount)
    {
        var root=new GameObject(name);root.transform.SetParent(parent,false);root.transform.SetPositionAndRotation(position,rotation);
        var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
        var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(database);
        for(int z=0;z<4;z++)for(int x=0;x<gunCount*2;x++)
            Place(assembly,x==0&&z==0?BlockShape.Core:BlockShape.Cube,new Vector3(x,-.5f,z));
        for(int i=0;i<gunCount;i++)Place(assembly,BlockShape.Gun3,new Vector3(i*2,.5f,0));
        var runtime=root.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,assembly.Export(name),tuning,battle);return runtime;
    }
    static ShipFlightController Flight(FlightShipRuntime ship,FlightTuning tuning,Camera camera)
    {
        var flight=ship.gameObject.AddComponent<ShipFlightController>();flight.Initialize(ship.GetComponent<Rigidbody>(),ship,tuning,camera,ship.HullRadius);
        flight.ExternalControl=true;flight.enabled=false;return flight;
    }
    static void Place(ShipAssembly assembly,BlockShape shape,Vector3 position)
    {if(!assembly.Place(shape,position,Quaternion.identity))throw new InvalidOperationException(shape+": "+assembly.PlacementFailure);}
    public static string Read()
    {
        if(!combat)combat=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
        if(!hud)hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();
        if(!enemy&&combat&&combat.Bot!=null)enemy=combat.Bot.ship;
        if(!combat||!hud)return "No HUD preview yet.";
        var heat=combat.Player.weapons.GatlingHeat;
        var names=new[]{"GatlingHeatState","GatlingHeatValue","TargetBlueprintCost","TargetLockStatus"};
        var labels=hud.GetComponentsInChildren<TextMeshProUGUI>(true);
        string text="hasTarget="+combat.Targeting.HasTarget+" cost="+(enemy?enemy.BlueprintCost:0)+" heat="+heat.DisplayHeatFraction+" overheated="+heat.AnyOverheated;
        foreach(string name in names){var label=labels.FirstOrDefault(t=>t.name==name);text+="\n"+name+"="+(label?label.text:"MISSING")+" active="+(label&&label.gameObject.activeInHierarchy);}
        Directory.CreateDirectory("tmp/Weapons20261105");File.WriteAllText("tmp/Weapons20261105/HeatHudPreview.txt",text);return text;
    }
}
