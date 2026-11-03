using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class CountermeasureDeploymentTests
    {
        readonly List<Object> owned = new List<Object>();
        ModuleDatabase database;
        ModuleDefinition launcher;
        FlareDefinition flare;
        MissionDefinition mission;

        [SetUp] public void SetUp()
        {
            database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            flare = Own(ScriptableObject.CreateInstance<FlareDefinition>());
            foreach (var shape in new[] { BlockShape.Core, BlockShape.FlareLauncher, BlockShape.Laser })
            {
                var definition = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape = shape; definition.massKg = 10; definition.cost = 3;
                definition.occupiedCells = new[] { Vector3Int.zero };
                definition.category = shape == BlockShape.Core ? ModuleCategory.Core : ModuleCategory.Weapon;
                if (shape == BlockShape.FlareLauncher) { definition.countermeasure = flare; launcher = definition; }
                if (shape == BlockShape.Laser) definition.weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
                database.modules.Add(definition);
            }
            mission = Own(ScriptableObject.CreateInstance<MissionDefinition>());
            mission.id = "countermeasure-validation"; mission.baseBudget = 1000;
        }

        [TearDown] public void TearDown()
        {
            BlockModules.Configure(null);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test] public void ValidCountermeasureDeploysWithoutPretendingToBeAnOffensiveWeapon()
        {
            var blueprint = Ship();
            string before = JsonUtility.ToJson(blueprint);
            foreach (var report in new[] { DeploymentValidator.ValidateForRange(blueprint, database), DeploymentValidator.Validate(blueprint, database, mission, MissionDifficulty.Standard) })
            {
                Assert.That(report.IsValid, Is.True, "A Weapon-category countermeasure has a FlareDefinition instead of a WeaponDefinition.");
                Assert.That(report.Warnings.Exists(issue => issue.key == "deploy.warning.noWeapons"), Is.True,
                    "Countermeasures do not remove the warning that this ship has no offensive weapons.");
            }
            Assert.That(JsonUtility.ToJson(blueprint), Is.EqualTo(before));
        }

        [Test] public void FlareLauncherWithoutItsCountermeasureDefinitionCannotDeploy()
        {
            launcher.countermeasure = null;
            AssertInvalid();
        }

        [TestCase("cooldownSeconds", -1f)]
        [TestCase("cooldownSeconds", float.NaN)]
        [TestCase("emissionSpeed", float.PositiveInfinity)]
        [TestCase("spreadHalfAngleDegrees", -1f)]
        [TestCase("spreadHalfAngleDegrees", 90f)]
        [TestCase("lifetime", 0f)]
        [TestCase("decoyChance", -1f)]
        [TestCase("decoyChance", 1.01f)]
        [TestCase("decoyRadius", -1f)]
        [TestCase("sparkSize", 0f)]
        [TestCase("glowIntensity", float.NaN)]
        public void InvalidCountermeasureNumbersAreRejectedBeforeEnteringTheRange(string field, float value)
        {
            typeof(FlareDefinition).GetField(field).SetValue(flare, value);
            AssertInvalid();
        }

        [Test] public void EmptySalvoIsRejectedAndValidZeroIntensityCountermeasureSettingsRemainUsable()
        {
            flare.burstCount = 0; AssertInvalid();
            flare.burstCount = 1; flare.cooldownSeconds = flare.emissionSpeed = flare.decoyChance = flare.decoyRadius = flare.glowIntensity = 0;
            flare.spreadHalfAngleDegrees = 89;
            Assert.That(DeploymentValidator.ValidateForRange(Ship(), database).IsValid, Is.True);
        }

        [Test] public void NormalizationRemovesLegacyFlareAssignmentButPreservesTheGunGroup()
        {
            var blueprint = Ship();
            blueprint.parts.Add(new BlockRecord { id = "gun", shape = BlockShape.Laser, position = new Vector3(1, 1.5f, 0), rotation = Quaternion.identity });
            blueprint.weaponGroups.Add(new WeaponGroupRecord { id = 2, name = "Main guns", binding = WeaponInputBinding.MouseLeft() });
            blueprint.weaponGroups.Add(new WeaponGroupRecord { id = 7, name = "Old flare assignment", binding = WeaponInputBinding.MouseLeft() });
            blueprint.weaponAssignments.Add(new WeaponGroupAssignment { partId = "gun", groupId = 2 });
            blueprint.weaponAssignments.Add(new WeaponGroupAssignment { partId = "flare", groupId = 7 });
            WeaponGroups.Normalize(blueprint, database);
            Assert.That(blueprint.weaponAssignments.Count, Is.EqualTo(1));
            Assert.That(blueprint.weaponAssignments[0].partId, Is.EqualTo("gun"));
            Assert.That(blueprint.weaponAssignments[0].groupId, Is.EqualTo(2));
            Assert.That(WeaponGroups.Assign(blueprint, "flare", 2, database), Is.False);
            Assert.That(blueprint.weaponAssignments.Count, Is.EqualTo(1));
        }

        void AssertInvalid()
        {
            foreach (var report in new[] { DeploymentValidator.ValidateForRange(Ship(), database), DeploymentValidator.Validate(Ship(), database, mission, MissionDifficulty.Standard) })
                Assert.That(report.Errors.Exists(issue => issue.key == "deploy.error.moduleConfig"), Is.True);
        }

        static BlueprintData Ship()
        {
            var blueprint = new BlueprintData { name = "Countermeasure deployment fixture" };
            blueprint.parts.Add(new BlockRecord { id = "core", shape = BlockShape.Core, position = Vector3.up * .5f, rotation = Quaternion.identity });
            blueprint.parts.Add(new BlockRecord { id = "flare", shape = BlockShape.FlareLauncher, position = Vector3.up * 1.5f, rotation = Quaternion.identity });
            return blueprint;
        }
        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    }
}
