using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.App;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    /// <summary>Native entry points for this round. Restores only explicit scalar balance fields.</summary>
    public static class BattleshipFrameworkTools
    {
        const string Folder="Documentation/Battleship-2026-11-02";
        [Serializable] public sealed class Snapshot { public int moduleCount,weaponCount,scalarCount;public List<AssetRecord> assets; }
        [Serializable] public sealed class AssetRecord { public string path,guid;public List<Scalar> fields; }
        [Serializable] public sealed class Scalar { public string field,type,value; }
        sealed class Operation { public UnityEngine.Object asset;public Scalar scalar; }

        [MenuItem("Starframe/战舰框架/打开组件")]
        public static void OpenModules()=>Selection.activeObject=AssetDatabase.LoadMainAssetAtPath("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        [MenuItem("Starframe/战舰框架/打开飞控")]
        public static void OpenFlight()=>Selection.activeObject=AssetDatabase.LoadMainAssetAtPath("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
        [MenuItem("Starframe/战舰框架/导入参考船（保留同名存档）")]
        public static void ImportReferences()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before importing reference blueprints.");
            string directory=Path.Combine(Folder,"Blueprints");
            if(!Directory.Exists(directory))throw new DirectoryNotFoundException("Generate this round's reference blueprints first: "+directory);
            var files=Directory.GetFiles(directory,"Battleship-*.json").OrderBy(path=>path,StringComparer.Ordinal).ToArray();
            if(files.Length==0)throw new FileNotFoundException("No Battleship-*.json reference blueprints exist.");
            var ships=files.Select(path=>BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(File.ReadAllText(path)))).ToArray();
            var repository=new BlueprintRepository(ShipWorkspace.BlueprintDirectory);int imported=0,preserved=0;
            foreach(var ship in ships){
                if(repository.NameExists(ship.name)){preserved++;continue;}
                repository.Save(ship,false);imported++;
            }
            Debug.Log("Battleship reference blueprints imported="+imported+", same names preserved="+preserved+". Current workspace unchanged.");
        }

        [MenuItem("Starframe/战舰框架/恢复本轮调整前参数/船体与装甲")]
        static void Hull()=>ConfirmRestore("hull");
        [MenuItem("Starframe/战舰框架/恢复本轮调整前参数/飞控与推进器")]
        static void Flight()=>ConfirmRestore("flight");
        [MenuItem("Starframe/战舰框架/恢复本轮调整前参数/武器")]
        static void Weapon()=>ConfirmRestore("weapon");
        [MenuItem("Starframe/战舰框架/恢复本轮调整前参数/电源")]
        static void Power()=>ConfirmRestore("power");
        [MenuItem("Starframe/战舰框架/恢复本轮调整前参数/护盾")]
        static void Shield()=>ConfirmRestore("shield");
        [MenuItem("Starframe/战舰框架/恢复本轮调整前参数/任务预算")]
        static void Mission()=>ConfirmRestore("mission");
        static void ConfirmRestore(string category)
        {
            if(EditorApplication.isPlaying){EditorUtility.DisplayDialog("战舰框架","请先停止播放。","确定");return;}
            if(EditorUtility.DisplayDialog("恢复本轮调整前参数",category+"：读取本轮实际 before.json，仅恢复白名单数值/布尔字段，保留美术、引用、占格、数组、玩家蓝图和工作区。可撤销。","恢复","取消"))
                Debug.Log("Battleship balance fields restored: "+Restore(category));
        }

        public static int Restore(string category)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before restoring balance fields.");
            if(!new[]{"hull","flight","weapon","power","shield","mission"}.Contains(category))throw new ArgumentException("Unknown battleship balance category: "+category);
            var snapshot=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Path.Combine(Folder,"before.json")));
            if(snapshot==null||snapshot.moduleCount!=38||snapshot.weaponCount!=14||snapshot.scalarCount<700||snapshot.assets==null||snapshot.assets.Count<55
                ||snapshot.assets.Any(asset=>asset.fields==null)||snapshot.assets.Sum(asset=>asset.fields.Count)!=snapshot.scalarCount)
                throw new InvalidDataException("Incomplete native battleship baseline; no changes applied.");
            var operations=new List<Operation>();
            foreach(var record in snapshot.assets){
                if(string.IsNullOrEmpty(record.path)||!record.path.StartsWith("Assets/_Starframe/Settings/",StringComparison.Ordinal)||record.path.Contains(".."))throw new InvalidDataException("Unexpected restore asset path.");
                if(!string.IsNullOrEmpty(record.guid)&&AssetDatabase.AssetPathToGUID(record.path)!=record.guid)throw new InvalidDataException("Restore asset identity changed: "+record.path);
                var asset=AssetDatabase.LoadMainAssetAtPath(record.path);if(!asset)throw new FileNotFoundException("Restore target missing: "+record.path);
                var serialized=new SerializedObject(asset);
                foreach(var scalar in record.fields){
                    if(!Allowed(asset,scalar.field,category))continue;
                    var property=serialized.FindProperty(scalar.field);
                    if(property==null)continue; // New fields absent from an older captured asset are not invented.
                    if(property.propertyType.ToString()!=scalar.type)throw new InvalidDataException("Restore field type changed: "+record.path+" / "+scalar.field);
                    ValidateScalar(property,scalar.value);operations.Add(new Operation{asset=asset,scalar=scalar});
                }
            }
            Undo.IncrementCurrentGroup();int undoGroup=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Restore battleship "+category);
            foreach(var operation in operations.GroupBy(value=>value.asset)){
                var asset=operation.Key;Undo.RecordObject(asset,"Restore battleship "+category);var serialized=new SerializedObject(asset);
                foreach(var value in operation){
                    var property=serialized.FindProperty(value.scalar.field);string text=value.scalar.value;
                    switch(property.propertyType){
                        case SerializedPropertyType.Boolean:property.boolValue=bool.Parse(text);break;
                        case SerializedPropertyType.Float:property.floatValue=(float)double.Parse(text,CultureInfo.InvariantCulture);break;
                        case SerializedPropertyType.Integer:property.longValue=long.Parse(text,CultureInfo.InvariantCulture);break;
                        case SerializedPropertyType.Enum:property.intValue=int.Parse(text,CultureInfo.InvariantCulture);break;
                    }
                }
                serialized.ApplyModifiedProperties();EditorUtility.SetDirty(asset);
            }
            Undo.CollapseUndoOperations(undoGroup);AssetDatabase.SaveAssets();return operations.Count;
        }
        static void ValidateScalar(SerializedProperty property,string value)
        {
            switch(property.propertyType){
                case SerializedPropertyType.Boolean:bool.Parse(value);return;
                case SerializedPropertyType.Integer:long.Parse(value,CultureInfo.InvariantCulture);return;
                case SerializedPropertyType.Enum:int.Parse(value,CultureInfo.InvariantCulture);return;
                case SerializedPropertyType.Float:
                    double number=double.Parse(value,CultureInfo.InvariantCulture);
                    if(double.IsNaN(number)||double.IsInfinity(number)||number>float.MaxValue||number<-float.MaxValue)throw new InvalidDataException("Nonfinite restore number.");return;
                default:throw new InvalidDataException("Only explicitly allowed scalar balance properties can be restored.");
            }
        }
        static bool Allowed(UnityEngine.Object asset,string field,string category)
        {
            string group;string[] fields;
            var module=asset as ModuleDefinition;
            if(module){
                group=module.countermeasure?"countermeasure":module.weapon?"weapon":module.category==ModuleCategory.Propulsion?"flight":module.category==ModuleCategory.Power?"power":module.shieldCapacity>0?"shield":"hull";
                fields=new[]{"cost","massKg","maxHp","recoverySeconds","thrustNewtons","generationWatts","storageJoules","thrusterPowerWatts","shieldPowerWatts","shieldCapacity","shieldRadius","shieldRecoveryDelay","shieldRecoveryPerSecond"};
            }else if(asset is WeaponDefinition){
                group="weapon";fields=new[]{"shotsPerSecond","damage","projectileSpeed","projectileDiameter","penetrationDepth","secondLayerDamageMultiplier","range","sustainedPowerWatts","missileLockSeconds","reloadSeconds","missileTurnDegrees","missileTurnAcceleration","missileGuidanceSeconds","missileSeekerConeDegrees","missileSeekerLossSeconds","missileAcceleration","missileLaunchSpeed","explosionRadius","chargeSeconds","firstShotImmediate","activationMode","requireTargetLock"};
            }else if(asset is FlightTuning){
                group="flight";fields=new[]{"maneuverThrustFraction","maxSpeed","maxLinearAcceleration","angularAccelerationGain","maxAngularAcceleration","angularRateGain","maxAngularRate","rollRateGain","maxRollRate","angularAuthorityReferenceAcceleration","angularAuthorityExponent","boostPowerWatts","boostMultiplier"};
            }else if(asset is Starframe.Combat.BattleTuning){group="power";fields=new[]{"unlimitedRangePower"};
            }else if(asset is MissionDefinition){group="mission";fields=new[]{"baseBudget","useProvisionalPricing","unpricedPartCost","maximumMassKg","relaxedBudgetMultiplier","standardBudgetMultiplier","expertBudgetMultiplier"};
            }else return false;
            return group==category&&fields.Contains(field);
        }
    }
}
