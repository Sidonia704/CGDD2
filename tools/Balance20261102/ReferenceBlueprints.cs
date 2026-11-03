// Standalone Unity-side builder for the three review blueprints under Documentation/.
// Invoke ReferenceBlueprints.Run(moduleDatabase) from an Editor/native helper after tuning.

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

namespace Starframe.Tools.Balance20261102
{
    public static class ReferenceBlueprints
    {
        const float CellY = .5f;
        const float StandardGravity = 9.80665f;

        sealed class BuildSpec
        {
            public string name;
            public int minX = -5, maxX = 5, minZ = -4, maxZ = 4;
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
            public float cost, massKg, forwardG, reverseG, sideG, verticalG, turnDegreesPerSecond;
        }

        /// <summary>Builds, validates, writes and summarizes three reference blueprints.</summary>
        public static string Run(ModuleDatabase database)
        {
            if (!database) throw new ArgumentNullException(nameof(database));
            var previousDatabase = BlockModules.ConfiguredDatabase;
            BlockModules.Configure(database);
            var outputDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Documentation", "Balance-2026-11-02", "Blueprints");
            Directory.CreateDirectory(outputDirectory);
            var reportPath = Path.Combine(outputDirectory, "report.txt");
            var summaries = new List<FileSummary>();
            var reportText = new StringBuilder();
            reportText.AppendLine("Reference ship blueprints (generated from the live ModuleDatabase)");
            reportText.AppendLine("Validation: BlueprintRepository.Upgrade, DeploymentValidator, weapon/flare clearance, and exhaust masks.");
            try
            {
                var specs = new[]
                {
                    new BuildSpec {
                        name = "Balance-Standard", power = BlockShape.Power2, shield = BlockShape.Shield,
                        topAndBottomThrusterCells = new[] { new Vector2Int(-4, -3), new Vector2Int(-1, -3), new Vector2Int(2, -3), new Vector2Int(4, -3), new Vector2Int(-2, -1), new Vector2Int(1, -1), new Vector2Int(4, -1) },
                        aftThrusters = new[] { BlockShape.Thruster2, BlockShape.Thruster2, BlockShape.Thruster2 },
                        aftThrusterX = new[] { -3, 0, 3 },
                        weapons = new[] { BlockShape.Railgun1, BlockShape.Laser2, BlockShape.Gun2, BlockShape.Missile1 },
                        weaponX = new[] { -4, -2, 0, 2 }, weaponGroupIds = new[] { 2, 0, 0, 1 }
                    },
                    new BuildSpec {
                        name = "Balance-Maneuver", power = BlockShape.Power2, shield = BlockShape.Shield,
                        topAndBottomThrusterCells = new[] { new Vector2Int(-4, -3), new Vector2Int(-1, -3), new Vector2Int(2, -3), new Vector2Int(4, -3), new Vector2Int(-2, -1), new Vector2Int(1, -1), new Vector2Int(4, -1), new Vector2Int(2, 1) },
                        aftThrusters = new[] { BlockShape.Thruster4 },
                        aftThrusterX = new[] { -1 },
                        weapons = new[] { BlockShape.Laser2, BlockShape.Gun2, BlockShape.Missile1 },
                        weaponX = new[] { -3, 0, 3 }, weaponGroupIds = new[] { 0, 0, 1 }
                    },
                    new BuildSpec {
                        name = "Balance-Heavy", power = BlockShape.Power2, shield = BlockShape.Shield,
                        topAndBottomThrusterCells = new[] { new Vector2Int(-4, -3), new Vector2Int(-2, -3), new Vector2Int(0, -3), new Vector2Int(2, -3), new Vector2Int(4, -3), new Vector2Int(-5, -1), new Vector2Int(-1, -1), new Vector2Int(3, -1) },
                        aftThrusters = new[] { BlockShape.Thruster3, BlockShape.Thruster1 },
                        aftThrusterX = new[] { -1, 3 },
                        weapons = new[] { BlockShape.Laser, BlockShape.Gun3, BlockShape.Missile1 },
                        weaponX = new[] { -3, -1, 2 }, weaponGroupIds = new[] { 0, 0, 1 },
                        extraArmor = new[] { BlockShape.Armor1, BlockShape.Armor1, BlockShape.Armor1, BlockShape.Armor1 },
                        extraArmorCells = new[] { new Vector3Int(-2, 1, 0), new Vector3Int(2, 1, 0), new Vector3Int(-3, 1, 1), new Vector3Int(3, 1, 1) },
                        extraHullCells = new[] {
                            new Vector3Int(-1,2,-1), new Vector3Int(0,2,-1), new Vector3Int(1,2,-1),
                            new Vector3Int(-1,2,0), new Vector3Int(1,2,0),
                            new Vector3Int(-1,2,1), new Vector3Int(0,2,1), new Vector3Int(1,2,1),
                            new Vector3Int(-2,2,0), new Vector3Int(2,2,0), new Vector3Int(0,2,-2), new Vector3Int(0,2,2)
                        }
                    }
                };

                var flightTuning = LoadFlightTuning(database);
                foreach (var spec in specs)
                {
                    reportText.AppendLine("Building " + spec.name + "...");
                    var built = Build(spec, database);
                    var upgraded = BlueprintRepository.Upgrade(built);
                    ValidateCombatClearance(upgraded, database);
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
                    reportText.AppendLine("  " + path);
                    File.WriteAllText(reportPath, reportText.ToString(), new UTF8Encoding(false));
                    if (summary.cost < 2700 || summary.cost > 3000)
                        throw new InvalidDataException(spec.name + " cost is outside the requested 2700-3000 range: " + summary.cost.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    File.WriteAllText(path, JsonUtility.ToJson(upgraded, true), new UTF8Encoding(false));
                }
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

        static BlueprintData Build(BuildSpec spec, ModuleDatabase database)
        {
            var state = new BuildState(spec.name, database);
            Add(state, BlockShape.Core, new Vector3Int(0, 0, 0), Quaternion.identity);

            // These modules replace deck voxels; the hull is filled around their exact asset footprints.
            var powerCell = spec.power == BlockShape.Power2 ? new Vector3Int(-4, 0, -1) : new Vector3Int(-4, 0, -1);
            var shieldCell = spec.shield == BlockShape.Shield2 ? new Vector3Int(2, 0, -1) : new Vector3Int(4, 0, -1);
            Add(state, spec.power, powerCell, Quaternion.identity);
            Add(state, spec.shield, shieldCell, Quaternion.identity);

            // A top-and-bottom T6 pair preserves all four horizontal nozzle axes and both vertical directions.
            foreach (var cell in spec.topAndBottomThrusterCells)
            {
                Add(state, BlockShape.Thruster6, new Vector3Int(cell.x, 1, cell.y), Quaternion.identity);
                Add(state, BlockShape.Thruster6, new Vector3Int(cell.x, -1, cell.y), Quaternion.AngleAxis(180, Vector3.right));
            }

            // Main engines exhaust aft (-Z) and therefore thrust forward (+Z).
            var aftRotation = Quaternion.AngleAxis(-90, Vector3.right);
            for (int i = 0; i < spec.aftThrusters.Length; i++)
                Add(state, spec.aftThrusters[i], new Vector3Int(spec.aftThrusterX[i], 0, -4), aftRotation);

            // Guns sit above the forward deck edge. Their local +Z bores point into open space.
            for (int i = 0; i < spec.weapons.Length; i++)
            {
                var gun = Add(state, spec.weapons[i], new Vector3Int(spec.weaponX[i], 1, 2), Quaternion.identity);
                state.weaponGroups[gun.id] = spec.weaponGroupIds[i];
            }

            // The upward flare column is kept clear; a small voxel cage shields the central core from above/below.
            Add(state, BlockShape.FlareLauncher, new Vector3Int(0, 1, 0), Quaternion.identity);
            foreach (int x in new[] { -1, 0, 1 })
            foreach (int z in new[] { -1, 0, 1 })
            {
                if (x == 0 && z == 0) continue;
                TryAdd(state, BlockShape.Cube, new Vector3Int(x, 1, z), Quaternion.identity);
                TryAdd(state, BlockShape.Cube, new Vector3Int(x, -1, z), Quaternion.identity);
            }

            for (int i = 0; i < spec.extraArmor.Length; i++)
                if (!TryAdd(state, spec.extraArmor[i], spec.extraArmorCells[i], Quaternion.identity))
                    throw new InvalidDataException(spec.name + " armor placement overlaps another module: " + spec.extraArmor[i] + " " + spec.extraArmorCells[i]);
            foreach (var cell in spec.extraHullCells)
                TryAdd(state, BlockShape.Cube, cell, Quaternion.identity);

            // Fill a compact planar hull from real module occupancy. A hole is left only where a component
            // body or an engine's actual clearance cells reserve the grid cell.
            for (int z = spec.minZ; z <= spec.maxZ; z++)
            for (int x = spec.minX; x <= spec.maxX; x++)
            {
                var cell = new Vector3Int(x, 0, z);
                if (state.placementCells.Contains(cell)) continue;
                Add(state, BlockShape.Cube, cell, Quaternion.identity);
            }

            ConfigureWeaponGroups(state, database);
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
            foreach (var occupied in cells) state.placementCells.Add(occupied);
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

        static void ConfigureWeaponGroups(BuildState state, ModuleDatabase database)
        {
            WeaponGroups.Normalize(state.data, database);
            WeaponGroups.ConfigureGroup(state.data, 0, "Primary", WeaponInputBinding.MouseLeft(), database);
            if (state.weaponGroups.Values.Contains(1))
                WeaponGroups.AddGroup(state.data, "Missiles", new WeaponInputBinding { source = WeaponInputSource.MouseRight }, database);
            if (state.weaponGroups.Values.Contains(2))
                WeaponGroups.AddGroup(state.data, "Railgun", new WeaponInputBinding { source = WeaponInputSource.Keyboard, key = Key.C }, database);
            foreach (var assignment in state.weaponGroups)
                if (!WeaponGroups.Assign(state.data, assignment.Key, assignment.Value, database))
                    throw new InvalidDataException("Could not assign weapon group for " + assignment.Key);
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
                states.Add(new ShipPartState { record = part, present = true, hp = definition.maxHp,
                    maxHp = definition.maxHp, thrustAvailable = !BlockModules.IsThruster(part.shape) || mask != 0,
                    availableExhaustMask = BlockModules.IsThruster(part.shape) ? mask : -1 });
            }

            var performance = ShipPerformance.Calculate(states, Quaternion.identity, tuning);
            return new FileSummary { file = file, name = data.name, parts = data.parts.Count, cost = cost, massKg = mass,
                forwardG = performance.accelerationPositive.z / StandardGravity,
                reverseG = performance.accelerationNegative.z / StandardGravity,
                sideG = Mathf.Min(performance.accelerationPositive.x, performance.accelerationNegative.x) / StandardGravity,
                verticalG = Mathf.Min(performance.accelerationPositive.y, performance.accelerationNegative.y) / StandardGravity,
                turnDegreesPerSecond = performance.angularRate };
        }
    }
}
