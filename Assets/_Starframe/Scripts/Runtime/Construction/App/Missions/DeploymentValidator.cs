// @role Validate ship structure, module configuration, budget and flight readiness.
// @calls BlockPlacement, MissionDefinition

using System;
using System.Collections.Generic;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.App
{
    public sealed class DeploymentIssue
    {
        public string key;
        public object[] args;
        public DeploymentIssue(string key, params object[] args) { this.key = key; this.args = args ?? Array.Empty<object>(); }
    }

    public sealed class DeploymentReport
    {
        public bool IsValid => Errors.Count == 0;
        public float Cost { get; internal set; }
        public float Budget { get; internal set; }
        public float Mass { get; internal set; }
        public int PartCount { get; internal set; }
        public int ProvisionalParts { get; internal set; }
        public readonly List<DeploymentIssue> Errors = new List<DeploymentIssue>();
        public readonly List<DeploymentIssue> Warnings = new List<DeploymentIssue>();
    }

    public static class DeploymentValidator
    {
        static readonly Vector3Int[] Directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, Vector3Int.forward, Vector3Int.back };

        public static DeploymentReport Validate(BlueprintData blueprint, ModuleDatabase database,
            MissionDefinition mission, MissionDifficulty difficulty)
            => Validate(blueprint, database, mission, difficulty, true);

        /// <summary>Validate flight readiness without applying a mission's economy or mass limits.</summary>
        public static DeploymentReport ValidateForRange(BlueprintData blueprint, ModuleDatabase database)
            => Validate(blueprint, database, null, MissionDifficulty.Standard, false);

        static DeploymentReport Validate(BlueprintData blueprint, ModuleDatabase database,
            MissionDefinition mission, MissionDifficulty difficulty, bool applyMissionConstraints)
        {
            var report = new DeploymentReport();
            bool validMission = applyMissionConstraints && mission && MissionIsValid(mission, difficulty);
            if (applyMissionConstraints)
            {
                if (!mission) Error(report, "deploy.error.noMission");
                else if (!validMission) Error(report, "deploy.error.missionConfig");
                else report.Budget = mission.BudgetFor(difficulty);
            }
            if (!database || database.modules == null) Error(report, "deploy.error.noDatabase");
            if (blueprint == null) { Error(report, "deploy.error.noBlueprint"); return report; }
            report.PartCount = blueprint.parts?.Count ?? 0;
            if (blueprint.parts == null || (blueprint.version != 1 && blueprint.version != 2)
                || string.IsNullOrWhiteSpace(blueprint.name))
            { Error(report, "deploy.error.invalidBlueprint"); return report; }

            // Perform the repository's v1 -> v2 pose migration on copies. Validation does not depend
            // on BlockModules' ambient editor database, meshes, colliders, or physics queries.
            var snapshot = new BlueprintData { name = blueprint.name };
            foreach (var original in blueprint.parts)
            {
                if (original == null) { snapshot.parts.Add(null); continue; }
                var copy = BlockPlacement.Copy(original);
                if (blueprint.version == 1)
                {
                    copy.position = new Vector3(copy.cell.x, copy.cell.y + .5f, copy.cell.z);
                    copy.rotation = Quaternion.Euler(0, copy.yawQuarterTurns * 90, 0);
                }
                snapshot.parts.Add(copy);
            }
            report.PartCount = snapshot.parts.Count;
            var definitions = new Dictionary<BlockShape, ModuleDefinition>();
            var duplicateDefinitions = new HashSet<BlockShape>();
            if (database && database.modules != null)
                foreach (var definition in database.modules)
                {
                    if (!definition) continue;
                    if (definitions.ContainsKey(definition.shape)) duplicateDefinitions.Add(definition.shape);
                    else definitions.Add(definition.shape, definition);
                }
            var checkedDefinitions = new Dictionary<BlockShape, bool>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var owners = new Dictionary<Vector3Int, int>();
            var bodyCells = new List<Vector3Int>[snapshot.parts.Count];
            int cores = 0, coreIndex = -1, weapons = 0;
            double cost = 0, mass = 0, generation = 0, storage = 0;
            bool invalidGeometry = false;
            for (int i = 0; i < snapshot.parts.Count; i++)
            {
                var part = snapshot.parts[i];
                if (part != null && part.shape == BlockShape.Core) { cores++; coreIndex = i; }
                if (part == null || string.IsNullOrWhiteSpace(part.id) || !ids.Add(part.id))
                { Error(report, "deploy.error.partId", part?.id ?? "—"); invalidGeometry = true; continue; }
                if (!Enum.IsDefined(typeof(BlockShape), part.shape) || !definitions.TryGetValue(part.shape, out var definition))
                { Error(report, "deploy.error.moduleMissing", part.id, part.shape); invalidGeometry = true; continue; }
                if (!checkedDefinitions.TryGetValue(part.shape, out bool validDefinition))
                {
                    validDefinition = !duplicateDefinitions.Contains(part.shape) && ModuleIsValid(definition, applyMissionConstraints);
                    checkedDefinitions.Add(part.shape, validDefinition);
                    if (!validDefinition) Error(report, "deploy.error.moduleConfig", part.shape);
                }
                if (!validDefinition) { invalidGeometry = true; continue; }
                if (applyMissionConstraints)
                {
                    cost += validMission ? mission.CostFor(definition) : definition.cost;
                    if (validMission && mission.useProvisionalPricing && definition.cost == 0) report.ProvisionalParts++;
                }
                mass += definition.massKg;
                generation += definition.generationWatts; storage += definition.storageJoules;
                if (definition.weapon) weapons++;
                if (part.yawQuarterTurns < 0 || part.yawQuarterTurns > 3 || !PoseIsValid(part))
                { Error(report, "deploy.error.gridPose", part.id); invalidGeometry = true; continue; }
                part.rotation = part.rotation.normalized;
                var cells = new List<Vector3Int>(definition.occupiedCells.Length); bodyCells[i] = cells;
                var anchor = BlockPlacement.GridHint(part.position);
                bool overlaps = false;
                foreach (var offset in definition.occupiedCells)
                {
                    var cell = anchor + Vector3Int.RoundToInt(part.rotation * (Vector3)offset);
                    cells.Add(cell);
                    if (owners.ContainsKey(cell)) overlaps = true;
                    else owners.Add(cell, i);
                }
                if (overlaps) { Error(report, "deploy.error.overlap", part.id); invalidGeometry = true; }
            }
            if (cores != 1) Error(report, "deploy.error.coreCount", cores);
            if (cores == 1 && !invalidGeometry && !AllConnected(coreIndex, bodyCells, owners))
                Error(report, "deploy.error.disconnected");
            if (applyMissionConstraints && (cost > float.MaxValue || mass > float.MaxValue)) Error(report, "deploy.error.totals");
            report.Cost = (float)Math.Min(float.MaxValue, cost);
            report.Mass = (float)Math.Min(float.MaxValue, mass);
            if (validMission)
            {
                if (report.Cost > report.Budget) Error(report, "deploy.error.budget", report.Cost, report.Budget);
                if (mission.maximumMassKg > 0 && report.Mass > mission.maximumMassKg)
                    Error(report, "deploy.error.mass", report.Mass, mission.maximumMassKg);
            }
            if (report.ProvisionalParts > 0)
                Warning(report, "deploy.warning.provisionalCosts", report.ProvisionalParts, mission.unpricedPartCost);
            if (generation <= 0 && storage <= 0) Warning(report, "deploy.warning.noPower");
            if (weapons == 0) Warning(report, "deploy.warning.noWeapons");
            return report;
        }

        static bool MissionIsValid(MissionDefinition mission, MissionDifficulty difficulty)
        {
            if (!Enum.IsDefined(typeof(MissionDifficulty), difficulty) || !Nonnegative(mission.baseBudget)
                || !Nonnegative(mission.maximumMassKg) || !Nonnegative(mission.relaxedBudgetMultiplier)
                || !Nonnegative(mission.standardBudgetMultiplier) || !Nonnegative(mission.expertBudgetMultiplier)
                || (mission.useProvisionalPricing && !Positive(mission.unpricedPartCost))) return false;
            double multiplier = difficulty == MissionDifficulty.Relaxed ? mission.relaxedBudgetMultiplier
                : difficulty == MissionDifficulty.Expert ? mission.expertBudgetMultiplier : mission.standardBudgetMultiplier;
            return mission.baseBudget * multiplier <= float.MaxValue;
        }

        static bool ModuleIsValid(ModuleDefinition definition, bool validateCost)
        {
            if ((validateCost && !Nonnegative(definition.cost)) || !Nonnegative(definition.massKg) || !Positive(definition.maxHp)
                || !Positive(definition.recoverySeconds) || !Nonnegative(definition.thrustNewtons)
                || !Nonnegative(definition.generationWatts) || !Nonnegative(definition.storageJoules)
                || definition.occupiedCells == null || definition.occupiedCells.Length == 0) return false;
            if ((definition.category == ModuleCategory.Weapon || definition.shape == BlockShape.Laser) && !definition.weapon) return false;
            if ((definition.category == ModuleCategory.Shield || definition.shape == BlockShape.Shield || definition.shieldCapacity > 0)
                && (!Positive(definition.shieldCapacity) || !Positive(definition.shieldRadius)
                    || !Nonnegative(definition.shieldRecoveryDelay) || !Nonnegative(definition.shieldRecoveryPerSecond)
                    || !Nonnegative(definition.shieldPowerWatts))) return false;
            if (definition.weapon)
            {
                var weapon = definition.weapon;
                if (!Positive(weapon.shotsPerSecond) || !Positive(weapon.projectileSpeed) || !Positive(weapon.range)
                    || !Positive(weapon.projectileDiameter) || !Nonnegative(weapon.damage)
                    || !Positive(weapon.penetrationDepth) || !Nonnegative(weapon.sustainedPowerWatts)
                    || !Enum.IsDefined(typeof(WeaponActivationMode), weapon.activationMode)
                    || (weapon.activationMode == WeaponActivationMode.ChargeRelease && !Positive(weapon.chargeSeconds))) return false;
            }
            var cells = new HashSet<Vector3Int>();
            foreach (var cell in definition.occupiedCells) if (!cells.Add(cell)) return false;
            var visited = new HashSet<Vector3Int>(); var queue = new Queue<Vector3Int>();
            var first = definition.occupiedCells[0]; visited.Add(first); queue.Enqueue(first);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var direction in Directions)
                    if (cells.Contains(cell + direction) && visited.Add(cell + direction)) queue.Enqueue(cell + direction);
            }
            return visited.Count == cells.Count;
        }

        static bool PoseIsValid(BlockRecord part)
        {
            var p = part.position; var r = part.rotation;
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || !Finite(r.x) || !Finite(r.y) || !Finite(r.z) || !Finite(r.w)) return false;
            float magnitude = r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w;
            return Mathf.Abs(magnitude - 1) <= .01f && BlockPlacement.IsCellAligned(p, r.normalized);
        }

        static bool AllConnected(int core, List<Vector3Int>[] bodyCells, Dictionary<Vector3Int, int> owners)
        {
            var seen = new bool[bodyCells.Length]; var queue = new int[bodyCells.Length];
            int head = 0, tail = 0; queue[tail++] = core; seen[core] = true;
            while (head < tail)
                foreach (var cell in bodyCells[queue[head++]])
                    foreach (var direction in Directions)
                        if (owners.TryGetValue(cell + direction, out int next) && !seen[next])
                        { seen[next] = true; queue[tail++] = next; }
            return tail == bodyCells.Length;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Nonnegative(float value) => Finite(value) && value >= 0;
        static bool Positive(float value) => Finite(value) && value > 0;
        static void Error(DeploymentReport report, string key, params object[] args) => report.Errors.Add(new DeploymentIssue(key, args));
        static void Warning(DeploymentReport report, string key, params object[] args) => report.Warnings.Add(new DeploymentIssue(key, args));
    }
}
