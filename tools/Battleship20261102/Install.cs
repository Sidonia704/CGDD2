using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;
using Starframe.Construction;

public static class BattleshipInstall
{
    // Preserve the preceding 4.2 durability candidate. Free structure now trades mass for durability;
    // this round does not claim a human three-minute TTK before the new large-ship validation.
    const float DurabilityScale=4.2f;
    static ModuleDatabase db;
    static void Set(UnityEngine.Object obj,string field,float value)
    {
        var so=new SerializedObject(obj);var p=so.FindProperty(field);
        if(p==null)throw new InvalidOperationException(obj.name+"."+field);
        if(p.propertyType==SerializedPropertyType.Integer)p.intValue=Mathf.RoundToInt(value);
        else p.floatValue=value;
        so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(obj);
    }
    static void Bool(UnityEngine.Object obj,string field,bool value){var so=new SerializedObject(obj);so.FindProperty(field).boolValue=value;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(obj);}
    static ModuleDefinition Part(BlockShape shape,float cost,float mass,float hp,float rebuild)
    {
        var d=db.Get(shape);if(!d)throw new InvalidOperationException("Missing "+shape);
        Set(d,"cost",cost);Set(d,"massKg",mass);Set(d,"maxHp",hp*DurabilityScale);Set(d,"recoverySeconds",rebuild);return d;
    }
    static void Power(BlockShape shape,float cost,float mass,float hp,float generation,float storage,float rebuild)
    {var d=Part(shape,cost,mass,hp,rebuild);Set(d,"generationWatts",generation);Set(d,"storageJoules",storage);}
    static void Shield(BlockShape shape,float cost,float mass,float hp,float capacity,float rate,float watts,float delay,float radius,float rebuild)
    {var d=Part(shape,cost,mass,hp,rebuild);Set(d,"shieldCapacity",capacity*DurabilityScale);Set(d,"shieldRecoveryPerSecond",rate*2);Set(d,"shieldPowerWatts",watts*2);Set(d,"shieldRecoveryDelay",delay);Set(d,"shieldRadius",radius);}
    static WeaponDefinition Weapon(BlockShape shape,float cost,float mass,float hp,float rebuild,float damage,float rate,float speed,float range,float diameter,float watts,float depth=1,float nextLayer=.35f)
    {
        var d=Part(shape,cost,mass,hp,rebuild);var w=d.weapon;if(!w)throw new InvalidOperationException("Missing weapon "+shape);
        Set(w,"damage",damage);Set(w,"shotsPerSecond",rate);Set(w,"projectileSpeed",speed);Set(w,"range",range);Set(w,"projectileDiameter",diameter);Set(w,"sustainedPowerWatts",watts);Set(w,"penetrationDepth",depth);Set(w,"secondLayerDamageMultiplier",nextLayer);return w;
    }
    static void Missile(BlockShape shape,float cost,float mass,float hp,float damage,float reload,float speed,float range,float watts,float radius,float turn,float lockSeconds)
    {
        var w=Weapon(shape,cost,mass,hp,22,damage,1/reload,speed,range,.55f,watts);
        Set(w,"reloadSeconds",reload);Set(w,"explosionRadius",radius);Set(w,"missileLockSeconds",lockSeconds);
        Set(w,"missileTurnDegrees",turn);Set(w,"missileTurnAcceleration",turn*2);Set(w,"missileGuidanceSeconds",10);Set(w,"missileSeekerConeDegrees",50);Set(w,"missileSeekerLossSeconds",.3f);
        Set(w,"missileAcceleration",90);Set(w,"missileLaunchSpeed",30);
    }
    sealed class BaselineGuard { public int moduleCount,weaponCount,scalarCount;public object[] assets; }
    static void RequireBaseline()
    {
        string path="Documentation/Battleship-2026-11-02/before.json";
        if(!System.IO.File.Exists(path))throw new InvalidOperationException("Run BattleshipSnapshot.CaptureBefore first.");
        var jsonType=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        var baseline=(BaselineGuard)jsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{System.IO.File.ReadAllText(path),typeof(BaselineGuard)});
        if(baseline==null||baseline.moduleCount!=38||baseline.weaponCount!=14||baseline.scalarCount<700||baseline.assets==null||baseline.assets.Length<55)
            throw new InvalidOperationException("Native battleship before.json is incomplete; no asset changes applied.");
    }
    public static string Apply()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before balancing.");
        RequireBaseline();
        db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        if(db.modules.Count!=38)throw new InvalidOperationException("Review new modules before applying this 38-module baseline.");
        Part(BlockShape.Cube,0,150,110,12);Part(BlockShape.Wedge,0,90,65,10);Part(BlockShape.Slab,0,80,65,10);Part(BlockShape.Pyramid,0,60,45,10);
        Part(BlockShape.Block5,0,120,90,11);Part(BlockShape.Block6,0,100,75,10);Part(BlockShape.Block7,0,300,220,12);Part(BlockShape.Block8,0,1200,880,16);
        Part(BlockShape.Armor1,18,260,330,18);Part(BlockShape.Armor2,34,500,620,20);Part(BlockShape.Armor3,48,730,880,22);Part(BlockShape.Armor4,92,1420,1680,26);
        Part(BlockShape.Core,160,1200,1000,25);
        // Propulsion module values and flight controls are installed by the flight owner; do not overwrite them here.
        Power(BlockShape.Power,55,450,240,100,1400,16);Power(BlockShape.Power2,340,3100,1650,650,10000,24);
        Shield(BlockShape.Shield,100,550,260,650,45,90,4.5f,8,20);Shield(BlockShape.Shield2,400,3000,1400,3000,180,360,5,14,26);
        Weapon(BlockShape.Laser,160,450,260,16,14,9,1300,2800,.55f,55);
        Weapon(BlockShape.Laser2,310,1200,560,19,23,10,1450,3200,.65f,100);
        Weapon(BlockShape.Laser3,520,2200,1000,23,38,10,1550,3600,.75f,165);
        Weapon(BlockShape.Gun1,130,500,280,15,6,16,1600,1500,.35f,18);
        Weapon(BlockShape.Gun2,250,1250,600,18,8.5f,20,1800,1800,.4f,32);
        Weapon(BlockShape.Gun3,430,2300,1080,22,12,24,2000,2200,.45f,55);
        var r1=Weapon(BlockShape.Railgun1,230,850,360,20,90,1.2f,6000,3800,.22f,100,2,.35f);
        var r2=Weapon(BlockShape.Railgun2,430,1700,680,23,150,1,7500,4500,.24f,180,3,.35f);
        var r3=Weapon(BlockShape.Railgun3,690,3300,1200,27,260,.8f,9000,5200,.28f,280,4,.4f);
        Bool(r1,"firstShotImmediate",true);Bool(r2,"firstShotImmediate",true);Bool(r3,"firstShotImmediate",true);
        Missile(BlockShape.Missile1,180,650,300,280,8,230,2200,70,2.5f,60,1.4f);
        Missile(BlockShape.Missile2,330,1300,600,480,10,260,2600,120,3.5f,50,1.8f);
        Missile(BlockShape.Missile3,520,2400,1000,800,13,290,3000,180,4.5f,42,2.2f);
        Weapon(BlockShape.Sword1,180,600,320,17,25,12,1400,45,.55f,120);
        Weapon(BlockShape.Sword2,350,1350,680,21,45,12,1400,60,.65f,220);
        var flare=Part(BlockShape.FlareLauncher,45,150,180,18);Set(flare.countermeasure,"decoyChance",.5f);
        // FlightTuning is left to the flight owner, including the shared nozzle model and boost settings.
        var b=AssetDatabase.LoadMainAssetAtPath("Assets/_Starframe/Settings/Flight/BattleTuning.asset");Set(b,"outOfCombatDelay",10);Bool(b,"unlimitedRangePower",false);
        foreach(string guid in AssetDatabase.FindAssets("t:MissionDefinition")){var mission=AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));Set(mission,"baseBudget",3500);Bool(mission,"useProvisionalPricing",false);}
        AssetDatabase.SaveAssets();return "Applied 3500-cost battleship combat/economy candidate: exactly eight structural hull types are free; armor and functional parts retain native prices. Preserved the prior 4.2 durability candidate and weapon DPS; propulsion/FlightTuning belong to the flight owner. Art, occupancy and player blueprints unchanged.";
    }
}


