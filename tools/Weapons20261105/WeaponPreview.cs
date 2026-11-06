using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

// External, memory-scene-only fixture. CreateEdit before Play; Begin after Play.
public static class WeaponPreview
{
    const string RigName="WeaponPreviewRig";
    const string Output="tmp/Weapons20261105";
    static readonly BlockShape[] Shapes={BlockShape.Gun3,BlockShape.Laser3,BlockShape.Railgun3,BlockShape.Sword2};
    static readonly List<UnityEngine.Object> memoryAssets=new List<UnityEngine.Object>();
    static readonly List<string> errors=new List<string>();
    static readonly FlightShipRuntime[] owners=new FlightShipRuntime[4],targets=new FlightShipRuntime[4];
    static readonly ShipWeapons[] weapons=new ShipWeapons[4];
    static readonly GatlingThermalState[] thermal=new GatlingThermalState[4];
    static readonly int[] hits=new int[4],shotsAtSix=new int[4],shotsAtEighteen=new int[4];
    static ProjectileWorld world;
    static float elapsed,maximumHeat;
    static int lastFrame,firstFrame,processedFrames;
    static bool running,capturedFiring,capturedHot,capturedRecovered,sawOverheat,recordedSix,recordedEighteen;

    [Serializable]public sealed class GunReport
    {
        public string shape;public int shots,shotsAtSix,shotsAtEighteen,confirmedHits,blocked;
        public float heat,spin;public bool overheated;
    }
    [Serializable]public sealed class PreviewReport
    {
        public string state;public float simulationSeconds,maximumGatlingHeat;
        public int gameFrames,processedFrames;public bool sawGatlingOverheat;
        public GunReport[] guns;public string[] mechanismErrors;
        public string note="Memory-scene fixture, real production weapon definitions and ShipWeapons/ProjectileWorld; explicit full weapon power. No production assets or player workspace written.";
    }

    public static string CreateEdit()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("CreateEdit requires the already-cleared memory scene before Play.");
        Directory.CreateDirectory(Output);
        var old=GameObject.Find(RigName);if(old)UnityEngine.Object.DestroyImmediate(old);
        var rig=new GameObject(RigName);
        var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";cameraObject.transform.SetParent(rig.transform,false);
        var camera=cameraObject.AddComponent<Camera>();cameraObject.AddComponent<AudioListener>();camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.008f,.016f,.032f);camera.fieldOfView=55;camera.nearClipPlane=.1f;camera.farClipPlane=2000;
        camera.transform.position=new Vector3(110,72,-115);camera.transform.LookAt(new Vector3(0,0,175));camera.allowHDR=true;
        var lightObject=new GameObject("Preview key light");lightObject.transform.SetParent(rig.transform,false);
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2.2f;light.color=new Color(.91f,.95f,1);
        light.transform.rotation=Quaternion.Euler(42,-35,0);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.23f,.27f,.34f);
        return "Created unsaved preview camera/light. Enter Play, then call WeaponPreview.Begin().";
    }

    public static string Begin()
    {
        if(!EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play before Begin.");
        Stop();errors.Clear();memoryAssets.Clear();Array.Clear(hits,0,4);Array.Clear(shotsAtSix,0,4);Array.Clear(shotsAtEighteen,0,4);
        Directory.CreateDirectory(Output);
        var rig=GameObject.Find(RigName);if(!rig)throw new InvalidOperationException("CreateEdit must create the preview rig first.");
        var source=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var sourceTuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
        var rules=AssetDatabase.LoadAssetAtPath<BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");
        if(!source||!sourceTuning||!rules)throw new InvalidOperationException("Production database/tuning missing.");
        var tuning=Memory(UnityEngine.Object.Instantiate(sourceTuning));tuning.database=source;
        var dummyDb=Memory(UnityEngine.Object.Instantiate(source));dummyDb.modules=new List<ModuleDefinition>(source.modules);
        var dummyCore=Memory(UnityEngine.Object.Instantiate(source.Get(BlockShape.Core)));dummyCore.maxHp=1000000000f;
        for(int i=0;i<dummyDb.modules.Count;i++)if(dummyDb.modules[i].shape==BlockShape.Core)dummyDb.modules[i]=dummyCore;
        var dummyTuning=Memory(UnityEngine.Object.Instantiate(sourceTuning));dummyTuning.database=dummyDb;
        var effects=new GameObject("Preview production effects");effects.transform.SetParent(rig.transform,false);
        var visuals=effects.AddComponent<LaserVisualPool>();visuals.Initialize(rules.laserShader,rules.particleScale);
        world=new ProjectileWorld();world.Initialize(visuals);
        var flashes=effects.AddComponent<CombatVfxPool>();flashes.Initialize(rules.destructionShader,source.Get(BlockShape.Core).material,rules.particleScale);
        world.MuzzleFlash+=(p,d,w)=>flashes.EmitMuzzle(p,d,w);
        world.WeaponImpact+=(p,w)=>flashes.EmitWeaponHit(p,w);
        world.HitConfirmed+=owner=>{for(int i=0;i<4;i++)if(owners[i]==owner){hits[i]++;break;}};
        for(int i=0;i<Shapes.Length;i++)
        {
            var definition=source.Get(Shapes[i]);if(!definition||!definition.weapon)throw new InvalidOperationException("Missing production "+Shapes[i]);
            owners[i]=CreateOwner(rig.transform,tuning,rules,Shapes[i],new Vector3(-45+i*30,0,0));
            weapons[i]=new ShipWeapons();weapons[i].Initialize(owners[i],world);
            var slots=(IList)typeof(ShipWeapons).GetField("guns",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(weapons[i]);
            thermal[i]=(GatlingThermalState)slots[0].GetType().GetField("thermal").GetValue(slots[0]);
            var part=owners[i].Parts.Find(p=>p.record.shape==Shapes[i]);
            var matrix=CylindricalHitQuery.PhysicsMatrix(part.view.GetComponent<Collider>());
            Vector3 muzzle=matrix.MultiplyPoint3x4(definition.muzzleLocalPosition);
            targets[i]=CreateTarget(rig.transform,dummyTuning,rules,muzzle+Vector3.forward*500-Vector3.up*.5f,i);
        }
        BlockModules.Configure(source);Physics.SyncTransforms();
        elapsed=maximumHeat=0;processedFrames=0;lastFrame=-1;firstFrame=Time.frameCount;
        capturedFiring=capturedHot=capturedRecovered=sawOverheat=recordedSix=recordedEighteen=false;
        running=true;EditorApplication.update+=Update;WriteReport("running");
        return "Started 20s production Gun3/Laser3/Railgun3/Sword2 preview: fire0-6, cool6-18, fire18-20; capture3.05s.";
    }

    static FlightShipRuntime CreateOwner(Transform parent,FlightTuning tuning,BattleTuning rules,BlockShape shape,Vector3 position)
    {
        var root=new GameObject("Preview owner "+shape);root.transform.SetParent(parent,false);root.transform.position=position;
        var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
        var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);
        var cells=tuning.database.Get(shape).occupiedCells;int minX=0,maxX=0,minZ=0,maxZ=0,minY=0;
        foreach(var c in cells){minX=Mathf.Min(minX,c.x);maxX=Mathf.Max(maxX,c.x);minZ=Mathf.Min(minZ,c.z);maxZ=Mathf.Max(maxZ,c.z);minY=Mathf.Min(minY,c.y);}
        for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)
        {
            BlockShape support=x==minX&&z==minZ?BlockShape.Core:BlockShape.Cube;
            Place(assembly,support,new Vector3(x,minY-.5f,z));
        }
        Place(assembly,shape,new Vector3(0,.5f,0));
        var ship=root.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export("preview "+shape),tuning,rules);ship.Team=0;
        return ship;
    }
    static FlightShipRuntime CreateTarget(Transform parent,FlightTuning tuning,BattleTuning rules,Vector3 position,int index)
    {
        var root=new GameObject("Preview high HP target "+index);root.transform.SetParent(parent,false);root.transform.position=position;
        var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
        var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);Place(assembly,BlockShape.Core,new Vector3(0,.5f,0));
        var ship=root.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export("preview target"),tuning,rules);ship.Team=1;
        return ship;
    }
    static void Place(ShipAssembly assembly,BlockShape shape,Vector3 position)
    {
        if(!assembly.Place(shape,position,Quaternion.identity))throw new InvalidOperationException(shape+" placement: "+assembly.PlacementFailure);
    }
    static T Memory<T>(T item)where T:UnityEngine.Object{item.hideFlags=HideFlags.DontSave;memoryAssets.Add(item);return item;}

    static void Update()
    {
        if(!running)return;
        if(!EditorApplication.isPlaying){Stop();return;}
        if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;processedFrames++;
        try
        {
            float remaining=Mathf.Min(Time.deltaTime,20-elapsed);
            if(remaining<=0)return;
            while(remaining>.000001f&&elapsed<19.99999f)
            {
                float boundary=elapsed<6?6:elapsed<18?18:20;
                float dt=Mathf.Min(.02f,Mathf.Min(remaining,boundary-elapsed));
                bool firing=elapsed<6||elapsed>=18;
                for(int i=0;i<4;i++)
                {
                    owners[i].BeginStep(dt);targets[i].BeginStep(dt);
                    weapons[i].FireRequested=firing;weapons[i].Prepare();weapons[i].Execute(dt,1);
                    if(weapons[i].BlockedGuns>0)Error(Shapes[i]+" muzzle blocked");
                    if(!targets[i].IsAlive)Error(Shapes[i]+" target died");
                }
                world.Step(dt);elapsed+=dt;remaining-=dt;
                maximumHeat=Mathf.Max(maximumHeat,thermal[0].HeatFraction);sawOverheat|=thermal[0].IsOverheated;
                if(!recordedSix&&elapsed>=5.99999f){for(int i=0;i<4;i++)shotsAtSix[i]=weapons[i].ShotsFired;recordedSix=true;}
                if(!recordedEighteen&&elapsed>=17.99999f){for(int i=0;i<4;i++)shotsAtEighteen[i]=weapons[i].ShotsFired;recordedEighteen=true;}
            }
            if(elapsed>=3.05f&&!capturedFiring){Capture("Firing.png");capturedFiring=true;WriteReport("firing capture");}
            if(elapsed>=6.5f&&!capturedHot){Capture("Cooling.png");capturedHot=true;WriteReport("cooling capture");}
            if(elapsed>=18.5f&&!capturedRecovered){Capture("Recovered.png");capturedRecovered=true;WriteReport("recovered capture");}
            if(elapsed>=19.99999f)
            {
                for(int i=0;i<4;i++)
                {
                    if(shotsAtEighteen[i]!=shotsAtSix[i])Error(Shapes[i]+" fired during idle6-18s");
                    if(weapons[i].ShotsFired<=shotsAtEighteen[i])Error(Shapes[i]+" did not resume18-20s");
                    if(hits[i]<=0)Error(Shapes[i]+" had no real target hit");
                }
                if(!sawOverheat)Error("Gatling never overheated under continuous full-power fire0-6s");
                Stop();WriteReport(errors.Count==0?"complete":"complete with mechanism errors");
            }
        }
        catch(Exception e){Error(e.ToString());Stop();WriteReport("failed");Debug.LogException(e);}
    }
    static void Capture(string name)=>ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Output,name)));
    static void Error(string text){if(!errors.Contains(text))errors.Add(text);}
    static void WriteReport(string state)
    {
        var report=new PreviewReport{state=state,simulationSeconds=elapsed,maximumGatlingHeat=maximumHeat,gameFrames=Time.frameCount-firstFrame,
            processedFrames=processedFrames,sawGatlingOverheat=sawOverheat,mechanismErrors=errors.ToArray(),guns=new GunReport[4]};
        for(int i=0;i<4;i++)report.guns[i]=new GunReport{shape=Shapes[i].ToString(),shots=weapons[i]?.ShotsFired??0,
            shotsAtSix=shotsAtSix[i],shotsAtEighteen=shotsAtEighteen[i],confirmedHits=hits[i],blocked=weapons[i]?.BlockedGuns??0,
            heat=thermal[i]?.HeatFraction??0,spin=thermal[i]?.SpinFraction??0,overheated=thermal[i]?.IsOverheated??false};
        File.WriteAllText(Path.Combine(Output,"PreviewReport.json"),JsonUtility.ToJson(report,true));
    }
    public static string Status()=>File.Exists(Path.Combine(Output,"PreviewReport.json"))?File.ReadAllText(Path.Combine(Output,"PreviewReport.json")):"No preview report yet.";
    public static string Stop()
    {
        running=false;EditorApplication.update-=Update;
        for(int i=0;i<4;i++)if(weapons[i]!=null)weapons[i].FireRequested=false;
        return "Stopped preview callback; memory scene remains available.";
    }
}
