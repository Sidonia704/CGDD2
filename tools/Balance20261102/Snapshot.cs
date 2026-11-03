using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;

public static class BalanceSnapshotTools
{
    // The Editor contains a second Newtonsoft namespace embedded by Localization;
    // select the real assembly explicitly rather than letting Roslyn bind ambiguously.
    static class JsonConvert
    {
        static readonly Type Serializer=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        public static string SerializeObject(object value)=>(string)Serializer.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
        public static T DeserializeObject<T>(string text)=>(T)Serializer.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{text,typeof(T)});
    }
    public const string Folder="Documentation/Balance-2026-11-02";
    [Serializable] public sealed class Snapshot {public string date;public List<Value> values=new List<Value>();}
    [Serializable] public sealed class Value {public string path,field,category;public float number;public bool boolean,isBool;}
    static readonly string[] ModuleFields={"cost","massKg","maxHp","recoverySeconds","thrustNewtons","generationWatts","storageJoules","thrusterPowerWatts","shieldPowerWatts","shieldCapacity","shieldRadius","shieldRecoveryDelay","shieldRecoveryPerSecond"};
    static readonly string[] WeaponFields={"shotsPerSecond","damage","projectileSpeed","projectileDiameter","penetrationDepth","secondLayerDamageMultiplier","range","sustainedPowerWatts","missileLockSeconds","reloadSeconds","missileTurnDegrees","missileTurnAcceleration","missileGuidanceSeconds","missileSeekerConeDegrees","missileSeekerLossSeconds","missileAcceleration","missileLaunchSpeed","explosionRadius","chargeSeconds","firstShotImmediate"};
    static readonly string[] FlightFields={"maneuverThrustFraction","maxSpeed","maxLinearAcceleration","angularAccelerationGain","maxAngularAcceleration","angularRateGain","maxAngularRate","rollRateGain","maxRollRate","angularAuthorityReferenceAcceleration","angularAuthorityExponent","boostPowerWatts","boostMultiplier"};
    public static Snapshot ReadLive()
    {
        var s=new Snapshot{date=DateTime.UtcNow.ToString("o")};
        foreach(string guid in AssetDatabase.FindAssets("t:ModuleDefinition")){
            string path=AssetDatabase.GUIDToAssetPath(guid);var m=AssetDatabase.LoadAssetAtPath<ModuleDefinition>(path);
            string category=m.countermeasure?"countermeasure":Starframe.Construction.BlockModules.IsArmor(m.shape)?"hull":m.category.ToString().ToLowerInvariant();
            foreach(string field in ModuleFields)Read(s,m,path,field,field=="recoverySeconds"?"recovery":category);
        }
        foreach(string guid in AssetDatabase.FindAssets("t:WeaponDefinition")){
            string path=AssetDatabase.GUIDToAssetPath(guid);var obj=AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            foreach(string field in WeaponFields)Read(s,obj,path,field,"weapon");
        }
        foreach(string guid in AssetDatabase.FindAssets("t:FlareDefinition")){
            string path=AssetDatabase.GUIDToAssetPath(guid);var obj=AssetDatabase.LoadMainAssetAtPath(path);
            foreach(var field in obj.GetType().GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
                if(field.FieldType==typeof(float)||field.FieldType==typeof(int)||field.FieldType==typeof(bool))Read(s,obj,path,field.Name,"countermeasure");
        }
        var flight=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
        foreach(string field in FlightFields)Read(s,flight,AssetDatabase.GetAssetPath(flight),field,"flight");
        var battle=AssetDatabase.LoadMainAssetAtPath("Assets/_Starframe/Settings/Flight/BattleTuning.asset");
        Read(s,battle,AssetDatabase.GetAssetPath(battle),"outOfCombatDelay","recovery");
        Read(s,battle,AssetDatabase.GetAssetPath(battle),"unlimitedRangePower","power");
        foreach(string guid in AssetDatabase.FindAssets("t:MissionDefinition")){
            string path=AssetDatabase.GUIDToAssetPath(guid);var obj=AssetDatabase.LoadMainAssetAtPath(path);
            Read(s,obj,path,"baseBudget","mission");Read(s,obj,path,"useProvisionalPricing","mission");
        }
        return s;
    }
    static void Read(Snapshot s,UnityEngine.Object obj,string path,string field,string category)
    {
        var p=new SerializedObject(obj).FindProperty(field);if(p==null)return;
        if(p.propertyType!=SerializedPropertyType.Float&&p.propertyType!=SerializedPropertyType.Integer&&p.propertyType!=SerializedPropertyType.Boolean)return;
        bool b=p.propertyType==SerializedPropertyType.Boolean;
        s.values.Add(new Value{path=path,field=field,category=category,isBool=b,boolean=b&&p.boolValue,number=b?0:p.propertyType==SerializedPropertyType.Integer?p.intValue:p.floatValue});
    }
    public static string Capture()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before balancing.");
        Directory.CreateDirectory(Folder);string file=Folder+"/before.json";
        if(File.Exists(file)){var previous=JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(file));if(previous?.values==null||previous.values.Count<700)throw new InvalidDataException("Existing snapshot is incomplete; preserve it and recover explicitly.");return "Original snapshot already exists; preserved without overwrite.";}
        var data=ReadLive();File.WriteAllText(file,JsonConvert.SerializeObject(data));return "Saved "+data.values.Count+" baseline fields.";
    }
    public static string CaptureCurrent(string file){var data=ReadLive();Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/"+file+".json",JsonConvert.SerializeObject(data));return "Saved and verified "+JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(Folder+"/"+file+".json")).values.Count+" fields.";}
    public static string Export()
    {
        var data=ReadLive();Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/after.json",JsonConvert.SerializeObject(data));
        var before=JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(Folder+"/before.json")).values.ToDictionary(v=>v.path+"|"+v.field);
        var rows=new List<string>{"category,asset,parameter,before,after"};
        foreach(var v in data.values){before.TryGetValue(v.path+"|"+v.field,out var old);rows.Add(v.category+","+Path.GetFileNameWithoutExtension(v.path)+","+v.field+","+Format(old)+","+Format(v));}
        File.WriteAllLines(Folder+"/parameters.csv",rows,System.Text.Encoding.UTF8);return "Exported "+data.values.Count+" fields.";
    }
    static string Format(Value v)=>v==null?"":v.isBool?v.boolean.ToString():v.number.ToString(CultureInfo.InvariantCulture);
    public static string Restore(string category="all")
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before rollback.");
        var data=JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(Folder+"/before.json"));if(data?.values==null||data.values.Count<700)throw new InvalidDataException("Incomplete rollback baseline");int count=0;
        foreach(var v in data.values){if(category!="all"&&v.category!=category)continue;var obj=AssetDatabase.LoadMainAssetAtPath(v.path);if(!obj)throw new InvalidOperationException(v.path);
            var so=new SerializedObject(obj);var p=so.FindProperty(v.field);if(p==null)continue;
            if(v.isBool)p.boolValue=v.boolean;else if(p.propertyType==SerializedPropertyType.Integer)p.intValue=Mathf.RoundToInt(v.number);else p.floatValue=v.number;
            so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(obj);count++;
        }
        AssetDatabase.SaveAssets();return "Restored "+count+" fields in category "+category;
    }
}
