using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;

public static class BattleshipSnapshot
{
    public const string Folder="Documentation/Battleship-2026-11-02";
    const string DatabasePath="Assets/_Starframe/Settings/Modules/ModuleDatabase.asset";
    public sealed class Scalar { public string field,type,value; }
    public sealed class AssetRecord
    {
        public string path,guid,type,fileSha256,editorSerializedJson,rawAssetYaml;
        public List<Scalar> fields=new List<Scalar>();
    }
    public sealed class Snapshot
    {
        public string capturedUtc,note;
        public int moduleCount,weaponCount,assetCount,scalarCount;
        public List<AssetRecord> assets=new List<AssetRecord>();
    }
    static Type JsonType=>System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
    public static string Json(object value)=>(string)JsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
    public static T Parse<T>(string json)=>(T)JsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{json,typeof(T)});
    public static Snapshot ReadLive()
    {
        var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(DatabasePath);
        if(!database||database.modules.Count!=38)throw new InvalidOperationException("Review the module roster before this 38-module capture.");
        var paths=new SortedSet<string>(StringComparer.Ordinal){DatabasePath,
            "Assets/_Starframe/Settings/Flight/FlightTuning.asset","Assets/_Starframe/Settings/Flight/BattleTuning.asset"};
        var weapons=new HashSet<WeaponDefinition>();
        foreach(var module in database.modules){
            if(!module)throw new InvalidOperationException("Null native module slot.");
            paths.Add(AssetDatabase.GetAssetPath(module));
            if(module.weapon){weapons.Add(module.weapon);paths.Add(AssetDatabase.GetAssetPath(module.weapon));}
            if(module.countermeasure)paths.Add(AssetDatabase.GetAssetPath(module.countermeasure));
        }
        foreach(string guid in AssetDatabase.FindAssets("t:MissionDefinition"))paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        var snapshot=new Snapshot{capturedUtc=DateTime.UtcNow.ToString("o"),moduleCount=database.modules.Count,weaponCount=weapons.Count,
            note="Real native asset capture for the battleship round. Includes complete EditorJsonUtility asset payload, exact on-disk YAML and SHA-256, plus all supported scalar serialized properties. Editor payload records current in-memory state; raw YAML records current saved disk bytes. No historical values are reconstructed."};
        foreach(string path in paths){
            if(string.IsNullOrWhiteSpace(path))throw new InvalidOperationException("A native definition has no asset path.");
            var asset=AssetDatabase.LoadMainAssetAtPath(path);if(!asset)throw new InvalidOperationException("Missing native asset: "+path);
            byte[] bytes=File.ReadAllBytes(path);
            var record=new AssetRecord{path=path,guid=AssetDatabase.AssetPathToGUID(path),type=asset.GetType().FullName,
                rawAssetYaml=File.ReadAllText(path),editorSerializedJson=EditorJsonUtility.ToJson(asset,true),fileSha256=Hash(bytes)};
            var serialized=new SerializedObject(asset);var property=serialized.GetIterator();
            while(property.Next(true)){
                string value=ScalarValue(property);if(value==null)continue;
                record.fields.Add(new Scalar{field=property.propertyPath,type=property.propertyType.ToString(),value=value});
            }
            snapshot.scalarCount+=record.fields.Count;snapshot.assets.Add(record);
        }
        snapshot.assetCount=snapshot.assets.Count;Validate(snapshot);return snapshot;
    }
    static string ScalarValue(SerializedProperty p)
    {
        switch(p.propertyType){
            case SerializedPropertyType.Boolean:return p.boolValue?"true":"false";
            case SerializedPropertyType.Integer:return p.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Float:return p.doubleValue.ToString("R",CultureInfo.InvariantCulture);
            case SerializedPropertyType.Enum:return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.String:return p.stringValue;
            case SerializedPropertyType.ArraySize:return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.ObjectReference:
                return p.objectReferenceValue?AssetDatabase.GetAssetPath(p.objectReferenceValue)+"#"+p.objectReferenceValue.name:"null";
            default:return null;
        }
    }
    public static string CaptureBefore()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before native capture.");
        Directory.CreateDirectory(Folder);string path=Path.Combine(Folder,"before.json");
        if(File.Exists(path)){var existing=Parse<Snapshot>(File.ReadAllText(path));Validate(existing);return "Existing complete battleship baseline preserved: "+existing.assetCount+" assets / "+existing.scalarCount+" scalars.";}
        var snapshot=ReadLive();WriteVerified(path,snapshot);
        return "Captured real battleship baseline: "+snapshot.moduleCount+" modules / "+snapshot.weaponCount+" weapons / "+snapshot.assetCount+" assets / "+snapshot.scalarCount+" scalars. Native assets unchanged.";
    }
    public static string ExportAfter()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before export.");
        var before=Parse<Snapshot>(File.ReadAllText(Path.Combine(Folder,"before.json")));Validate(before);
        var after=ReadLive();WriteVerified(Path.Combine(Folder,"after.json"),after);
        var old=new Dictionary<string,Scalar>(StringComparer.Ordinal);
        foreach(var asset in before.assets)foreach(var field in asset.fields)old[asset.path+"|"+field.field]=field;
        var rows=new List<string>{"asset,property,type,before,after,changed"};int changed=0;
        foreach(var asset in after.assets)foreach(var field in asset.fields){
            old.TryGetValue(asset.path+"|"+field.field,out var previous);bool different=previous==null||previous.value!=field.value;
            if(different)changed++;
            rows.Add(Csv(asset.path)+","+Csv(field.field)+","+Csv(field.type)+","+Csv(previous?.value??"")+","+Csv(field.value)+","+(different?"true":"false"));
        }
        File.WriteAllLines(Path.Combine(Folder,"parameters.csv"),rows,new UTF8Encoding(false));
        return "Exported complete native after.json and "+after.scalarCount+" scalar rows; changed="+changed+". Before preserved.";
    }
    public static string Restore(string category="all")
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play before restoring native balance fields.");
        category=(category??"all").ToLowerInvariant();if(category=="weapons")category="weapon";if(category=="shields")category="shield";
        var allowed=new[]{"all","hull","weapon","power","shield","recovery","flight","countermeasure","mission"};
        if(!allowed.Contains(category))throw new ArgumentException("Unknown balance category: "+category);
        var before=Parse<Snapshot>(File.ReadAllText(Path.Combine(Folder,"before.json")));Validate(before);int count=0;
        foreach(var asset in before.assets){
            var target=AssetDatabase.LoadMainAssetAtPath(asset.path);if(!target)throw new InvalidOperationException("Missing restore target: "+asset.path);
            var serialized=new SerializedObject(target);bool changed=false;
            foreach(var field in asset.fields){
                if(!BalanceField(target,field.field,category))continue;
                var property=serialized.FindProperty(field.field);if(property==null)continue;
                switch(property.propertyType){
                    case SerializedPropertyType.Boolean:property.boolValue=bool.Parse(field.value);break;
                    case SerializedPropertyType.Float:property.floatValue=(float)double.Parse(field.value,CultureInfo.InvariantCulture);break;
                    case SerializedPropertyType.Integer:property.longValue=long.Parse(field.value,CultureInfo.InvariantCulture);break;
                    case SerializedPropertyType.Enum:property.intValue=int.Parse(field.value,CultureInfo.InvariantCulture);break;
                    default:continue;
                }
                count++;changed=true;
            }
            if(changed){serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target);}
        }
        AssetDatabase.SaveAssets();return "Restored "+count+" numeric/bool balance fields from this round's real before.json ("+category+"). Art, meshes, references, occupancy and arrays unchanged.";
    }
    static bool BalanceField(UnityEngine.Object target,string field,string category)
    {
        string group;string[] allowed;
        var module=target as ModuleDefinition;
        if(module){
            group=module.countermeasure?"countermeasure":module.weapon?"weapon":module.category==ModuleCategory.Propulsion?"flight":
                module.category==ModuleCategory.Power?"power":module.shieldCapacity>0?"shield":"hull";
            if(field=="recoverySeconds")return category=="all"||category=="recovery"||category==group;
            allowed=new[]{"cost","massKg","maxHp","thrustNewtons","generationWatts","storageJoules","thrusterPowerWatts",
                "shieldPowerWatts","shieldCapacity","shieldRadius","shieldRecoveryDelay","shieldRecoveryPerSecond"};
        }else if(target is WeaponDefinition){
            group="weapon";allowed=new[]{"shotsPerSecond","damage","projectileSpeed","projectileDiameter","penetrationDepth","secondLayerDamageMultiplier","range",
                "sustainedPowerWatts","missileLockSeconds","reloadSeconds","missileTurnDegrees","missileTurnAcceleration","missileGuidanceSeconds",
                "missileSeekerConeDegrees","missileSeekerLossSeconds","missileAcceleration","missileLaunchSpeed","explosionRadius","chargeSeconds","firstShotImmediate","activationMode","requireTargetLock"};
        }else if(target is FlareDefinition){
            group="countermeasure";allowed=new[]{"burstCount","cooldownSeconds","spreadHalfAngleDegrees","emissionSpeed","lifetime","decoyChance","decoyRadius","sparkSize","glowIntensity"};
        }else if(target is FlightTuning){
            group="flight";allowed=new[]{"maneuverThrustFraction","maxSpeed","maxLinearAcceleration","angularAccelerationGain","maxAngularAcceleration","angularRateGain","maxAngularRate",
                "rollRateGain","maxRollRate","angularAuthorityReferenceAcceleration","angularAuthorityExponent","boostPowerWatts","boostMultiplier"};
        }else if(target.GetType().Name=="BattleTuning"){
            if(field=="outOfCombatDelay"||field=="recoveryTick")return category=="all"||category=="recovery";
            group="power";allowed=new[]{"unlimitedRangePower"};
        }else if(target.GetType().Name=="MissionDefinition"){
            group="mission";allowed=new[]{"baseBudget","useProvisionalPricing","unpricedPartCost","maximumMassKg","relaxedBudgetMultiplier","standardBudgetMultiplier","expertBudgetMultiplier"};
        }else return false;
        return (category=="all"||category==group)&&allowed.Contains(field);
    }
    static void WriteVerified(string path,Snapshot value)
    {
        string json=Json(value);var verify=Parse<Snapshot>(json);Validate(verify);
        if(verify.scalarCount!=value.scalarCount||verify.assets.Count!=value.assets.Count)throw new InvalidDataException("Snapshot roundtrip lost nested data.");
        File.WriteAllText(path,json,new UTF8Encoding(false));
        Validate(Parse<Snapshot>(File.ReadAllText(path)));
    }
    public static void RequireBaseline()=>Validate(Parse<Snapshot>(File.ReadAllText(Path.Combine(Folder,"before.json"))));
    static void Validate(Snapshot value)
    {
        if(value==null||value.moduleCount!=38||value.weaponCount!=14||value.assets==null||value.assets.Count<55||value.scalarCount<700
            ||value.assets.Any(asset=>asset.fields==null||asset.fields.Count==0||string.IsNullOrEmpty(asset.editorSerializedJson)||string.IsNullOrEmpty(asset.rawAssetYaml)))
            throw new InvalidDataException("Incomplete native battleship baseline; do not apply assets.");
        if(value.scalarCount!=value.assets.Sum(asset=>asset.fields.Count))throw new InvalidDataException("Native snapshot scalar count mismatch.");
    }
    static string Csv(string value)=>"\""+(value??"").Replace("\"","\"\"")+"\"";
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
}

