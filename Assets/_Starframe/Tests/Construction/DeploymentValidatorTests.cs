using System;
using System.Collections.Generic;
using NUnit.Framework;
using Starframe.App;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class DeploymentValidatorTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        ModuleDatabase database;
        MissionDefinition mission;

        [SetUp] public void SetUp()
        {
            database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            foreach (var shape in new[] { BlockShape.Core, BlockShape.Cube, BlockShape.Laser, BlockShape.Power })
            {
                var definition = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape = shape; definition.cost = shape == BlockShape.Core ? 2 : 3;
                definition.massKg = 10; definition.occupiedCells = new[] { Vector3Int.zero };
                definition.category = shape == BlockShape.Core ? ModuleCategory.Core : ModuleCategory.Hull;
                if (shape == BlockShape.Laser)
                {
                    definition.category = ModuleCategory.Weapon;
                    definition.weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
                    definition.occupiedCells = new[] { Vector3Int.zero, Vector3Int.forward, Vector3Int.forward * 2 };
                }
                if (shape == BlockShape.Power) { definition.category = ModuleCategory.Power; definition.generationWatts = 1; definition.storageJoules = 50; }
                database.modules.Add(definition);
            }
            mission = Own(ScriptableObject.CreateInstance<MissionDefinition>());
            mission.id = "test-mission"; mission.baseBudget = 10000;
        }

        [TearDown] public void TearDown()
        {
            BlockModules.Configure(null);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test] public void DifficultyMultipliersAreConfigurableAndEmptySceneRemainsAValidContentHook()
        {
            mission.baseBudget = 6000;
            Assert.That(mission.BudgetFor(MissionDifficulty.Relaxed), Is.EqualTo(7500));
            Assert.That(mission.BudgetFor(MissionDifficulty.Standard), Is.EqualTo(6000));
            Assert.That(mission.BudgetFor(MissionDifficulty.Expert), Is.EqualTo(4500));
            mission.expertBudgetMultiplier = .6f;
            Assert.That(mission.BudgetFor(MissionDifficulty.Expert), Is.EqualTo(3600).Within(.001f));
            mission.sceneName = "";
            Assert.That(Validate(Ship()).IsValid, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => mission.BudgetFor((MissionDifficulty)99));
        }

        [Test] public void CatalogFindIsStableAndToleratesUnfilledAssetSlots()
        {
            var catalog = Own(ScriptableObject.CreateInstance<MissionCatalog>());
            catalog.missions.Add(null); catalog.missions.Add(mission);
            Assert.That(catalog.Find("test-mission"), Is.SameAs(mission));
            Assert.That(catalog.Find("TEST-MISSION"), Is.Null);
            Assert.That(catalog.Find("unknown"), Is.Null);
            Assert.That(catalog.Find(null), Is.Null);
        }

        [Test] public void CostAndMassCountAMultiCellPartOnceAndNeverRequireVisualAssets()
        {
            var blueprint = Ship(Part("gun", BlockShape.Laser, 1, 0, 0));
            var report = Validate(blueprint);
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.PartCount, Is.EqualTo(2)); Assert.That(report.Cost, Is.EqualTo(5)); Assert.That(report.Mass, Is.EqualTo(20));
            foreach (var definition in database.modules) { Assert.That(definition.mesh, Is.Null); Assert.That(definition.material, Is.Null); }
            mission.baseBudget = 4;
            Assert.That(DeploymentValidator.Validate(blueprint, database, mission, MissionDifficulty.Relaxed).IsValid, Is.True);
            Assert.That(Has(Validate(blueprint).Errors, "deploy.error.budget"), Is.True);
            foreach (var definition in database.modules) definition.cost = 0;
            mission.baseBudget = 0;
            Assert.That(Validate(blueprint).IsValid, Is.True, "Zero-cost production definitions must remain valid until balancing is requested.");
        }

        [Test] public void ProvisionalPricesAreOptInAndOnlyReplaceZeroCostDefinitions()
        {
            var core = database.modules.Find(d => d.shape == BlockShape.Core);
            var cube = database.modules.Find(d => d.shape == BlockShape.Cube);
            var gun = database.modules.Find(d => d.shape == BlockShape.Laser);
            core.cost = 0; gun.cost = 0;
            var blueprint = Ship(Part("cube", BlockShape.Cube, -1, 0, 0), Part("gun", BlockShape.Laser, 1, 0, 0));
            Assert.That(mission.useProvisionalPricing, Is.False);
            Assert.That(mission.CostFor(core), Is.Zero);
            Assert.That(Validate(blueprint).Cost, Is.EqualTo(cube.cost));
            mission.useProvisionalPricing = true; mission.unpricedPartCost = 10; mission.baseBudget = 22;
            var report = Validate(blueprint);
            Assert.That(report.Cost, Is.EqualTo(23));
            Assert.That(report.ProvisionalParts, Is.EqualTo(2), "A multi-cell gun has one provisional part price.");
            Assert.That(Has(report.Errors, "deploy.error.budget"), Is.True);
            var warning = report.Warnings.Find(issue => issue.key == "deploy.warning.provisionalCosts");
            Assert.That(warning, Is.Not.Null); Assert.That(warning.args, Is.EqualTo(new object[] { 2, 10f }));
            Assert.That(mission.CostFor(cube), Is.EqualTo(3));
            Assert.That(core.cost, Is.Zero); Assert.That(gun.cost, Is.Zero);
        }

        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void EnabledProvisionalPricingRequiresAFinitePositiveRate(float rate)
        {
            mission.unpricedPartCost = rate; mission.useProvisionalPricing = true;
            Assert.That(Has(Validate(Ship()).Errors, "deploy.error.missionConfig"), Is.True);
            mission.useProvisionalPricing = false;
            Assert.That(Validate(Ship()).IsValid, Is.True, "Unused mission pricing must not gate a ship.");
        }

        [Test] public void RangeReadinessHasNoMissionBudgetProvisionalOrAggregateMassGate()
        {
            var blueprint = Ship(Part("cube", BlockShape.Cube, 1, 0, 0));
            foreach (var definition in database.modules) { definition.cost = float.MaxValue; definition.massKg = float.MaxValue; }
            var report = DeploymentValidator.ValidateForRange(blueprint, database);
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.PartCount, Is.EqualTo(2)); Assert.That(report.Cost, Is.Zero); Assert.That(report.Budget, Is.Zero);
            Assert.That(report.ProvisionalParts, Is.Zero);
            Assert.That(Has(report.Warnings, "deploy.warning.provisionalCosts"), Is.False);
            database.modules.Find(d => d.shape == BlockShape.Core).cost = float.NaN;
            Assert.That(DeploymentValidator.ValidateForRange(blueprint, database).IsValid, Is.True);
            blueprint.parts[1].position = new Vector3(2, .5f, 0);
            Assert.That(Has(DeploymentValidator.ValidateForRange(blueprint, database).Errors, "deploy.error.disconnected"), Is.True);
        }

        [TestCase(0)] [TestCase(2)] public void ExactlyOneCoreIsRequiredEvenWhenMetadataClaimsOtherwise(int count)
        {
            var blueprint = Ship(Part("second", count == 2 ? BlockShape.Core : BlockShape.Cube, 1, 0, 0));
            if (count == 0) blueprint.parts[0].shape = BlockShape.Cube;
            blueprint.hasCore = count == 0;
            Assert.That(Has(Validate(blueprint).Errors, "deploy.error.coreCount"), Is.True);
        }

        [Test] public void FaceConnectionUsesRotatedCompleteBodyCellsAndRejectsDiagonalContact()
        {
            var gun = Part("gun", BlockShape.Laser, 1, 0, 0); gun.rotation = Quaternion.Euler(0, 90, 0);
            Assert.That(Validate(Ship(gun, Part("far-end", BlockShape.Cube, 4, 0, 0))).IsValid, Is.True);
            Assert.That(Has(Validate(Ship(Part("diagonal", BlockShape.Cube, 1, 1, 0))).Errors, "deploy.error.disconnected"), Is.True);
            Assert.That(Has(Validate(Ship(gun, Part("overlap", BlockShape.Cube, 3, 0, 0))).Errors, "deploy.error.overlap"), Is.True);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] public void FractionalOffAxisAndNonfinitePosesAreRejected(int kind)
        {
            var part = Part("bad-pose", BlockShape.Cube, 1, 0, 0);
            if (kind == 0) part.position += Vector3.right * .1f;
            if (kind == 1) part.rotation = Quaternion.Euler(0, 45, 0);
            if (kind == 2) part.position = new Vector3(float.NaN, .5f, 0);
            Assert.That(Has(Validate(Ship(part)).Errors, "deploy.error.gridPose"), Is.True);
        }

        [Test] public void MissingDefinitionsDuplicateIdsAndMissingWeaponConfigurationBlockDeployment()
        {
            Assert.That(Has(Validate(Ship(Part("core", BlockShape.Cube, 1, 0, 0))).Errors, "deploy.error.partId"), Is.True);
            Assert.That(Has(Validate(Ship(Part("missing", BlockShape.Thruster1, 1, 0, 0))).Errors, "deploy.error.moduleMissing"), Is.True);
            database.modules.Find(d => d.shape == BlockShape.Laser).weapon = null;
            Assert.That(Has(Validate(Ship(Part("gun", BlockShape.Laser, 1, 0, 0))).Errors, "deploy.error.moduleConfig"), Is.True);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] public void ModuleCellDefinitionsMustBeNonemptyUniqueAndConnected(int kind)
        {
            var cube = database.modules.Find(d => d.shape == BlockShape.Cube);
            cube.occupiedCells = kind == 0 ? Array.Empty<Vector3Int>() : kind == 1
                ? new[] { Vector3Int.zero, Vector3Int.zero } : new[] { Vector3Int.zero, Vector3Int.right * 2 };
            Assert.That(Has(Validate(Ship(Part("cube", BlockShape.Cube, 1, 0, 0))).Errors, "deploy.error.moduleConfig"), Is.True);
        }

        [Test] public void OptionalMassIsTheOnlyAdditionalShipConstraintAndWarningsDoNotBlock()
        {
            var blueprint = Ship(); var report = Validate(blueprint);
            Assert.That(report.IsValid, Is.True);
            Assert.That(Has(report.Warnings, "deploy.warning.noPower"), Is.True);
            Assert.That(Has(report.Warnings, "deploy.warning.noWeapons"), Is.True);
            mission.maximumMassKg = 9;
            Assert.That(Has(Validate(blueprint).Errors, "deploy.error.mass"), Is.True);
            mission.maximumMassKg = 0;
            Assert.That(Validate(blueprint).IsValid, Is.True);
        }

        [Test] public void ThousandPlusPartsValidateWithoutAHiddenCapOrMutatingSource()
        {
            var blueprint = Ship();
            for (int i = 1; i < 1200; i++) blueprint.parts.Add(Part("part-" + i, BlockShape.Cube, i, 0, 0));
            string before = JsonUtility.ToJson(blueprint);
            var unrelated = Own(ScriptableObject.CreateInstance<ModuleDatabase>()); BlockModules.Configure(unrelated);
            var report = Validate(blueprint);
            Assert.That(report.IsValid, Is.True); Assert.That(report.PartCount, Is.EqualTo(1200));
            Assert.That(report.Cost, Is.EqualTo(2 + 1199 * 3)); Assert.That(report.Mass, Is.EqualTo(12000));
            Assert.That(JsonUtility.ToJson(blueprint), Is.EqualTo(before));
        }

        [Test] public void LegacyMigrationUsesCopiesAndIgnoresLegacyUninitializedFullPose()
        {
            var blueprint = Ship(Part("cube", BlockShape.Cube, 0, 0, 0)); blueprint.version = 1;
            blueprint.parts[0].cell = Vector3Int.zero; blueprint.parts[1].cell = Vector3Int.right;
            blueprint.parts[1].yawQuarterTurns = 3; blueprint.parts[1].rotation = default;
            string before = JsonUtility.ToJson(blueprint);
            Assert.That(Validate(blueprint).IsValid, Is.True);
            Assert.That(JsonUtility.ToJson(blueprint), Is.EqualTo(before));
        }

        [Test] public void InvalidMissionAndModuleNumbersFailWithoutNaNReportTotals()
        {
            mission.baseBudget = float.PositiveInfinity;
            Assert.That(Has(Validate(Ship()).Errors, "deploy.error.missionConfig"), Is.True);
            mission.baseBudget = 10000;
            database.modules.Find(d => d.shape == BlockShape.Core).massKg = float.NaN;
            var report = Validate(Ship());
            Assert.That(Has(report.Errors, "deploy.error.moduleConfig"), Is.True);
            Assert.That(float.IsNaN(report.Mass), Is.False); Assert.That(float.IsNaN(report.Cost), Is.False);
        }

        DeploymentReport Validate(BlueprintData blueprint) => DeploymentValidator.Validate(blueprint, database, mission, MissionDifficulty.Standard);
        static bool Has(List<DeploymentIssue> issues, string key) => issues.Exists(issue => issue.key == key);
        static BlueprintData Ship(params BlockRecord[] extra)
        {
            var blueprint = new BlueprintData { name = "Deployment fixture" };
            blueprint.parts.Add(Part("core", BlockShape.Core, 0, 0, 0)); blueprint.parts.AddRange(extra); return blueprint;
        }
        static BlockRecord Part(string id, BlockShape shape, int x, int y, int z) => new BlockRecord
        { id = id, shape = shape, position = new Vector3(x, y + .5f, z), rotation = Quaternion.identity };
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
    }
}
