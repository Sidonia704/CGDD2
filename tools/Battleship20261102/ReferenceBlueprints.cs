// Standalone Unity-side builder for the three review blueprints under Documentation/.
// Invoke Starframe.Tools.Battleship20261102.BattleshipBlueprints.Run(moduleDatabase) after this round's native tuning.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Starframe.Tools.Battleship20261102
{
    public static class BattleshipBlueprints
    {
        const float CellY = .5f;
        const float StandardGravity = 9.80665f;

        sealed class BuildSpec
        {
            public string name;
            public int minX=-8,maxX=8,minZ=-9,maxZ=9;
            public int lowerHalfWidth=6,lowerMinZ=-3,lowerMaxZ=3,armorCount=16,aftY;
            public Vector3Int powerCell=new Vector3Int(-5,0,0);
            public BlockShape power, shield;
            public Vector2Int[] topAndBottomThrusterCells;
            public BlockShape[] aftThrusters;
            public int[] aftThrusterX;
            public BlockShape[] weapons;
            public int[] weaponX;
            public int[] weaponGroupIds;
            public BlockShape[] extraArmor = Array.Empty<BlockShape>();
            public Vector3Int[] extraArmorCells = Array.Empty<Vector3Int>();
            public Vector3Int[] extraHullCells = Array.Empty<Vector3Int>();
        }

        sealed class BuildState
        {
            public readonly BlueprintData data;
            public readonly ModuleDatabase database;
            public readonly HashSet<Vector3Int> placementCells = new HashSet<Vector3Int>();
            public readonly Dictionary<string, BlockRecord> records = new Dictionary<string, BlockRecord>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> weaponGroups = new Dictionary<string, int>(StringComparer.Ordinal);
            public BuildState(string name, ModuleDatabase db) { data = new BlueprintData { name = name }; database = db; }
        }

        [Serializable]
        sealed class FileSummary
        {
            public string file, name;
            public int parts;
            public float cost, massKg, totalHullHp,shieldCapacity,generationWatts,storageJoules,weaponDemandWatts,shieldRecoveryWatts;
            public float weaponCost,propulsionCost,powerCost,shieldCost,armorCost,coreAndMiscCost,freeHullMass;
            public int weaponUnits,weaponGroups,freeHullParts;
            public float forwardG, reverseG, sideG, verticalG, turnDegreesPerSecond;
            public string axisOrder="X/Y/Z; angular X=pitch,Y=yaw,Z=roll; stopping distances from maxSpeed with opposite-axis thrust";
            public float maxSpeed;
            public float[] linearPositiveG,linearNegativeG,angularAccelerationPositive,angularAccelerationNegative,angularRatePositive,angularRateNegative,stoppingDistancePositive,stoppingDistanceNegative,turningRadiusPitchYaw,inertiaTensorKgM2,centerOfMass;
        }

        /// <summary>Builds, validates, writes and summarizes three reference blueprints.</summary>
        public static string Run(ModuleDatabase database)
        {
            if (!database) throw new ArgumentNullException(nameof(database));
            var previousDatabase = BlockModules.ConfiguredDatabase;
            BlockModules.Configure(database);
            var outputDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Documentation", "Battleship-2026-11-02", "Blueprints");
            Directory.CreateDirectory(outputDirectory);
            var reportPath = Path.Combine(outputDirectory, "report.txt");
            var summaries = new List<FileSummary>();
            var reportText = new StringBuilder();
            reportText.AppendLine("Reference ship blueprints (generated from the live ModuleDatabase)");
            reportText.AppendLine("3500-cost battleship references: 300-600 parts, 6-8 weapons, four real bound groups. Free hull mass trades against paid functions and armor. Validation: native connectivity, complete mounts, deployment, weapon/flare clearance, and all exhaust masks. Performance is a candidate, not a human-validated three-minute TTK.");
            try
            {
                var specs = new[]
                {
                    new BuildSpec {
                        name="Battleship-Heavy",minZ=-11,maxZ=11,lowerHalfWidth=7,lowerMinZ=-3,lowerMaxZ=3,armorCount=30,aftY=-1,
                        power=BlockShape.Power2,shield=BlockShape.Shield2,
                        topAndBottomThrusterCells=new[]{new Vector2Int(-6,-5),new Vector2Int(-3,-5),new Vector2Int(3,-5),new Vector2Int(6,-5),new Vector2Int(-6,-2),new Vector2Int(-3,-2),new Vector2Int(3,-2),new Vector2Int(6,-2)},
                        aftThrusters=new[]{BlockShape.Thruster3},aftThrusterX=new[]{0},
                        weapons=new[]{BlockShape.Laser2,BlockShape.Gun1,BlockShape.Gun1,BlockShape.Missile1,BlockShape.Missile1,BlockShape.Railgun1},
                        weaponX=new[]{-7,-4,-2,1,3,6},weaponGroupIds=new[]{0,1,1,2,2,3}
                    },
                    new BuildSpec {
                        name="Battleship-Maneuver",minX=-6,maxX=6,minZ=-7,maxZ=7,lowerHalfWidth=6,lowerMinZ=-4,lowerMaxZ=3,armorCount=16,powerCell=new Vector3Int(-1,0,-4),
                        power=BlockShape.Power2,shield=BlockShape.Shield,
                        topAndBottomThrusterCells=new[]{new Vector2Int(-5,-5),new Vector2Int(-2,-5),new Vector2Int(2,-5),new Vector2Int(5,-5),new Vector2Int(-5,-2),new Vector2Int(-2,-2),new Vector2Int(2,-2),new Vector2Int(5,-2),new Vector2Int(-5,2),new Vector2Int(-2,2),new Vector2Int(2,2),new Vector2Int(5,2)},
                        aftThrusters=new[]{BlockShape.Thruster2},aftThrusterX=new[]{0},
                        weapons=new[]{BlockShape.Laser,BlockShape.Laser,BlockShape.Gun1,BlockShape.Gun1,BlockShape.Gun1,BlockShape.Missile1,BlockShape.Missile1,BlockShape.Sword1},
                        weaponX=new[]{-5,-4,-2,-1,1,2,4,5},weaponGroupIds=new[]{0,0,1,1,1,2,2,3}
                    },
                    new BuildSpec {
                        name="Battleship-Assault",minZ=-10,maxZ=10,lowerHalfWidth=6,lowerMinZ=-3,lowerMaxZ=3,armorCount=14,aftY=-1,
                        power=BlockShape.Power2,shield=BlockShape.Shield,
                        topAndBottomThrusterCells=new[]{new Vector2Int(-6,-4),new Vector2Int(-3,-4),new Vector2Int(0,-4),new Vector2Int(3,-4),new Vector2Int(6,-4)},
                        aftThrusters=new[]{BlockShape.Thruster5},aftThrusterX=new[]{-1},
                        weapons=new[]{BlockShape.Laser,BlockShape.Laser,BlockShape.Laser,BlockShape.Gun1,BlockShape.Gun1,BlockShape.Missile1,BlockShape.Missile1,BlockShape.Railgun1},
                        weaponX=new[]{-7,-5,-3,-1,1,3,5,7},weaponGroupIds=new[]{0,0,0,1,1,2,2,3}
                    }
                };
                var flightTuning = LoadFlightTuning(database);
                foreach (var spec in specs)
                {
                    reportText.AppendLine("Building " + spec.name + "...");
                    var built = Build(spec, database);
                    var upgraded = BlueprintRepository.Upgrade(built);
                    ValidateCombatClearance(upgraded, database);
                    foreach(var part in upgraded.parts)if((BlockModules.IsWeapon(part.shape)||BlockModules.IsArmor(part.shape))&&!BlockConnectivity.HasCompleteMount(part,upgraded.parts))throw new InvalidDataException(spec.name+" incomplete mount: "+part.shape+" "+part.cell);
                    var deployment = DeploymentValidator.ValidateForRange(upgraded, database);
                    if (!deployment.IsValid)
                        throw new InvalidDataException(spec.name + " deployment validation failed: " +
                            string.Join("; ", deployment.Errors.Select(e => e.ToString())));

                    string fileName = spec.name + ".json";
                    string path = Path.Combine(outputDirectory, fileName);
                    var summary = Summarize(fileName, upgraded, database, flightTuning);
                    summaries.Add(summary);
                    reportText.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}: {1} parts, cost {2:0}, mass {3:0} kg, +Z {4:0.00}g, -Z {5:0.00}g, side {6:0.00}g, vertical {7:0.00}g, turn {8:0.0} deg/s",
                        summary.file, summary.parts, summary.cost, summary.massKg, summary.forwardG, summary.reverseG,
                        summary.sideG, summary.verticalG, summary.turnDegreesPerSecond));
                    reportText.AppendLine("  angular rates +XYZ="+string.Join("/",summary.angularRatePositive.Select(v=>v.ToString("0.00")))+"; -XYZ="+string.Join("/",summary.angularRateNegative.Select(v=>v.ToString("0.00")))+" deg/s; stop +XYZ="+string.Join("/",summary.stoppingDistancePositive.Select(v=>v.ToString("0.0")))+"; -XYZ="+string.Join("/",summary.stoppingDistanceNegative.Select(v=>v.ToString("0.0")))+"m; turn radius pitch/yaw="+string.Join("/",summary.turningRadiusPitchYaw.Select(v=>v.ToString("0.0")))+"m at "+summary.maxSpeed+"m/s.");
                    reportText.AppendLine("  role costs: weapons="+summary.weaponCost+", propulsion="+summary.propulsionCost+", power="+summary.powerCost+", shield="+summary.shieldCost+", armor="+summary.armorCost+", core/misc="+summary.coreAndMiscCost+"; weapon units/groups="+summary.weaponUnits+"/"+summary.weaponGroups+"; free hull parts="+summary.freeHullParts);
                    reportText.AppendLine("  demand/generation="+summary.weaponDemandWatts+"/"+summary.generationWatts+"W; shield recovery="+summary.shieldRecoveryWatts+"W; storage="+summary.storageJoules+"J; shield="+summary.shieldCapacity+"; summed part HP="+summary.totalHullHp+" (not core TTK).");
                    reportText.AppendLine("  " + path);
                    File.WriteAllText(reportPath, reportText.ToString(), new UTF8Encoding(false));
                    if (summary.parts < 300 || summary.parts > 600) throw new InvalidDataException(spec.name+" must contain 300-600 parts; got "+summary.parts);
                    if (upgraded.weaponGroups.Count != 4 || upgraded.weaponAssignments.Count < 6) throw new InvalidDataException(spec.name+" requires four groups and at least six weapons.");
                    if (summary.cost < 3300 || summary.cost > 3500)
                        throw new InvalidDataException(spec.name + " cost is outside the requested 3300-3500 range: " + summary.cost.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    File.WriteAllText(path, JsonUtility.ToJson(upgraded, true), new UTF8Encoding(false));
                }
                var jsonType=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
                string summaryJson=(string)jsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{summaries});
                File.WriteAllText(Path.Combine(outputDirectory,"summary.json"),summaryJson,new UTF8Encoding(false));
                return reportText.ToString();
            }
            catch (Exception error)
            {
                reportText.AppendLine("ERROR: " + error);
                throw;
            }
            finally
            {
                BlockModules.Configure(previousDatabase);
                File.WriteAllText(reportPath, reportText.ToString(), new UTF8Encoding(false));
            }
        }

        static Vector2Int[] ThrusterPairs(int count)
        {
            var cells=new[]{new Vector2Int(-7,-6),new Vector2Int(-4,-6),new Vector2Int(-1,-6),new Vector2Int(2,-6),new Vector2Int(5,-6),new Vector2Int(7,-6),
                new Vector2Int(-7,-3),new Vector2Int(7,-3),new Vector2Int(-3,-3),new Vector2Int(3,-3),new Vector2Int(-7,0),new Vector2Int(7,0)};
            return cells.Take(count).ToArray();
        }
        static BlueprintData Build(BuildSpec spec,ModuleDatabase database)
        {
            var state=new BuildState(spec.name,database);
            Add(state,BlockShape.Core,new Vector3Int(0,0,0),Quaternion.identity);
            Add(state,BlockShape.Power2,spec.powerCell,Quaternion.identity);
            Add(state,BlockShape.Power,new Vector3Int(-2,0,-1),Quaternion.identity);
            Add(state,BlockShape.Power,new Vector3Int(4,0,-1),Quaternion.identity);
            if(spec.shield==BlockShape.Shield2)Add(state,BlockShape.Shield2,new Vector3Int(1,0,-1),Quaternion.identity);
            else foreach(int x in new[]{-6,6})foreach(int z in new[]{-2,2})Add(state,BlockShape.Shield,new Vector3Int(x,0,z),Quaternion.identity);
            foreach(var cell in spec.topAndBottomThrusterCells){
                Add(state,BlockShape.Thruster6,new Vector3Int(cell.x,1,cell.y),Quaternion.identity);
                Add(state,BlockShape.Thruster6,new Vector3Int(cell.x,-1,cell.y),Quaternion.AngleAxis(180,Vector3.right));
            }
            var aftRotation=Quaternion.AngleAxis(-90,Vector3.right);
                        for(int i=0;i<spec.aftThrusters.Length;i++){
                Add(state,spec.aftThrusters[i],new Vector3Int(spec.aftThrusterX[i],spec.aftY,spec.minZ),aftRotation);
                // Low-mounted main engines keep their thrust near COM. A regular-cell pillar connects their real forward mount.
                for(int y=spec.aftY;y<0;y++)TryAdd(state,BlockShape.Cube,new Vector3Int(spec.aftThrusterX[i],y,spec.minZ+1),Quaternion.identity);
            }
            for(int i=0;i<spec.weapons.Length;i++){
                var gun=Add(state,spec.weapons[i],new Vector3Int(spec.weaponX[i],1,spec.maxZ-3),Quaternion.identity);
                state.weaponGroups[gun.id]=spec.weaponGroupIds[i];
            }
            // Armor is paid; it occupies real supported upper-deck slots behind the forward battery.
            int armor=0;
            foreach(int z in new[]{3,0,1,-1,-3,2,-2})foreach(int x in Enumerable.Range(spec.minX+1,spec.maxX-spec.minX-1)){
                if(armor>=spec.armorCount)break;
                if(x==0&&z==0)continue;
                if(TryAdd(state,BlockShape.Armor1,new Vector3Int(x,1,z),Quaternion.identity))armor++;
            }
            if(armor!=spec.armorCount)throw new InvalidDataException("Could not fit the paid armor allowance.");
            // Fill free structural hull around exact native footprints and every real exhaust reservation.
            for(int z=spec.minZ;z<=spec.maxZ;z++)for(int x=spec.minX;x<=spec.maxX;x++)TryAdd(state,BlockShape.Cube,new Vector3Int(x,0,z),Quaternion.identity);
            for(int z=spec.lowerMinZ;z<=spec.lowerMaxZ;z++)for(int x=-spec.lowerHalfWidth;x<=spec.lowerHalfWidth;x++)TryAdd(state,BlockShape.Cube,new Vector3Int(x,-1,z),Quaternion.identity);
            for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++)TryAdd(state,BlockShape.Cube,new Vector3Int(x,1,z),Quaternion.identity);
            // Small raised hull details remain supported by the deck; neither is in the forward firing lanes.
            foreach(int x in new[]{spec.minX,spec.maxX})foreach(int z in new[]{-4,-1,2})TryAdd(state,BlockShape.Wedge,new Vector3Int(x,1,z),Quaternion.identity);
            Add(state,BlockShape.FlareLauncher,new Vector3Int(0,2,0),Quaternion.identity);
            ConfigureWeaponGroups(state,database);
            return state.data;
        }
        static BlockRecord Add(BuildState state, BlockShape shape, Vector3Int cell, Quaternion rotation)
        {
            var record = new BlockRecord {
                id = Guid.NewGuid().ToString("N"), shape = shape, cell = cell, yawQuarterTurns = 0,
                position = new Vector3(cell.x, cell.y + CellY, cell.z), rotation = rotation.normalized
            };
            var cells = BlockModules.PlacementCells(record);
            foreach (var occupied in cells)
                if (state.placementCells.Contains(occupied))
                    throw new InvalidDataException(state.data.name + " overlap at " + occupied + " while adding " + shape + " " + cell);
            foreach (var occupied in BlockModules.AllCells(record)) state.placementCells.Add(occupied);
            state.data.parts.Add(record);
            state.records.Add(record.id, record);
            return record;
        }

        static bool TryAdd(BuildState state, BlockShape shape, Vector3Int cell, Quaternion rotation)
        {
            var probe = new BlockRecord { id = "probe", shape = shape, cell = cell,
                position = new Vector3(cell.x, cell.y + CellY, cell.z), rotation = rotation.normalized };
            foreach (var occupied in BlockModules.PlacementCells(probe))
                if (state.placementCells.Contains(occupied)) return false;
            Add(state, shape, cell, rotation);
            return true;
        }

        static void ConfigureWeaponGroups(BuildState state,ModuleDatabase database)
        {
            WeaponGroups.Normalize(state.data,database);
            WeaponGroups.ConfigureGroup(state.data,0,"Lasers",WeaponInputBinding.MouseLeft(),database);
            WeaponGroups.AddGroup(state.data,"Kinetic",new WeaponInputBinding{source=WeaponInputSource.MouseMiddle},database);
            WeaponGroups.AddGroup(state.data,"Missiles",new WeaponInputBinding{source=WeaponInputSource.MouseRight},database);
            WeaponGroups.AddGroup(state.data,"Special",WeaponInputBinding.Keyboard(Key.C),database);
            foreach(var assignment in state.weaponGroups)if(!WeaponGroups.Assign(state.data,assignment.Key,assignment.Value,database))throw new InvalidDataException("Could not assign weapon group.");
        }
        static void ValidateCombatClearance(BlueprintData data, ModuleDatabase database)
        {
            var clearance = new WeaponClearance();
            foreach (var part in data.parts)
            {
                var definition = database.Get(part.shape);
                if (!BlockModules.IsWeapon(part.shape) && part.shape != BlockShape.FlareLauncher) continue;
                foreach (var obstacle in data.parts)
                {
                    if (ReferenceEquals(part, obstacle)) continue;
                    var obstacleDefinition = database.Get(obstacle.shape);
                    if (clearance.IsBlockedBy(part, definition, obstacle, obstacleDefinition ? obstacleDefinition.mesh : null))
                        throw new InvalidDataException(data.name + " has a blocked firing/flare path: " + part.shape + " " + part.cell + " -> " + obstacle.shape + " " + obstacle.cell);
                }
            }
        }

        static FlightTuning LoadFlightTuning(ModuleDatabase database)
        {
#if UNITY_EDITOR
            var tuning = AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            if (!tuning) throw new InvalidDataException("Could not load the project's FlightTuning.asset.");
            if (tuning.database != database) throw new InvalidDataException("FlightTuning.asset does not reference the supplied live ModuleDatabase.");
            return tuning;
#else
            throw new InvalidOperationException("Reference blueprint performance summaries must run in the Unity Editor.");
#endif
        }

        static float[] Values(Vector3 value,float scale=1)=>new[]{value.x*scale,value.y*scale,value.z*scale};

        static FileSummary Summarize(string file, BlueprintData data, ModuleDatabase database, FlightTuning tuning)
        {
            var bodyOwners = new Dictionary<Vector3Int, BlockRecord>();
            float mass = 0, cost = 0;
            foreach (var part in data.parts)
            {
                var definition = database.Get(part.shape);
                if (!definition) throw new InvalidDataException("Missing ModuleDefinition for " + part.shape);
                mass += definition.massKg;
                cost += definition.cost;
                foreach (var cell in BlockModules.BodyCells(part)) bodyOwners[cell] = part;
            }

            var states = new List<ShipPartState>(data.parts.Count);
            foreach (var part in data.parts)
            {
                var definition = database.Get(part.shape);
                int mask = 0;
                var directions = BlockModules.LocalExhaustDirections(part.shape);
                for (int nozzle = 0; nozzle < directions.Length && nozzle < 31; nozzle++)
                {
                    bool blocked = false;
                    foreach (var cell in BlockModules.ExhaustCells(part, nozzle))
                        if (bodyOwners.TryGetValue(cell, out var owner) && !ReferenceEquals(owner, part)) { blocked = true; break; }
                    if (!blocked) mask |= 1 << nozzle;
                }
                if(BlockModules.IsThruster(part.shape)&&mask!=((1<<directions.Length)-1))throw new InvalidDataException(data.name+" has blocked thruster nozzles: "+part.shape+" "+part.cell+" mask="+mask);
                states.Add(new ShipPartState { record = part, present = true, hp = definition.maxHp,
                    maxHp = definition.maxHp, thrustAvailable = !BlockModules.IsThruster(part.shape) || mask != 0,
                    availableExhaustMask = BlockModules.IsThruster(part.shape) ? mask : -1 });
            }

            var performance = ShipPerformance.Calculate(states, Quaternion.identity, tuning);
            var result=new FileSummary { file = file, name = data.name, parts = data.parts.Count, cost = cost, massKg = mass,
                forwardG = performance.accelerationPositive.z / StandardGravity,
                reverseG = performance.accelerationNegative.z / StandardGravity,
                sideG = Mathf.Min(performance.accelerationPositive.x, performance.accelerationNegative.x) / StandardGravity,
                verticalG = Mathf.Min(performance.accelerationPositive.y, performance.accelerationNegative.y) / StandardGravity,
                turnDegreesPerSecond = performance.angularRate,weaponGroups=data.weaponGroups.Count,
                maxSpeed=performance.maxSpeed,linearPositiveG=Values(performance.accelerationPositive,1/StandardGravity),linearNegativeG=Values(performance.accelerationNegative,1/StandardGravity),
                angularAccelerationPositive=Values(performance.angularAccelerationPositive),angularAccelerationNegative=Values(performance.angularAccelerationNegative),
                angularRatePositive=Values(performance.angularRatePositive),angularRateNegative=Values(performance.angularRateNegative),
                stoppingDistancePositive=Values(performance.maxSpeedStoppingDistancePositive),stoppingDistanceNegative=Values(performance.maxSpeedStoppingDistanceNegative),
                turningRadiusPitchYaw=new[]{performance.turningRadiusAtMaxSpeed.x,performance.turningRadiusAtMaxSpeed.y},
                inertiaTensorKgM2=Values(performance.inertiaTensorKgM2),centerOfMass=Values(performance.centerOfMass) };
            foreach(var part in data.parts){
                var definition=database.Get(part.shape);result.totalHullHp+=definition.maxHp;result.shieldCapacity+=definition.shieldCapacity;
                result.generationWatts+=definition.generationWatts;result.storageJoules+=definition.storageJoules;
                if(definition.shieldCapacity>0)result.shieldRecoveryWatts+=definition.shieldPowerWatts;
                if(definition.weapon){result.weaponUnits++;result.weaponCost+=definition.cost;result.weaponDemandWatts+=definition.weapon.sustainedPowerWatts;}
                else if(BlockModules.IsThruster(part.shape))result.propulsionCost+=definition.cost;
                else if(BlockModules.IsArmor(part.shape))result.armorCost+=definition.cost;
                else if(definition.category==ModuleCategory.Power)result.powerCost+=definition.cost;
                else if(definition.shieldCapacity>0)result.shieldCost+=definition.cost;
                else if(definition.cost==0){result.freeHullParts++;result.freeHullMass+=definition.massKg;}
                else result.coreAndMiscCost+=definition.cost;
            }
            return result;
        }
    }
}




