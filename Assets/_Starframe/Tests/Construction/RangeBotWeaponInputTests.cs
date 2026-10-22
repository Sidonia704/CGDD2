using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class RangeBotWeaponInputTests
    {
        readonly List<Object> owned = new List<Object>();
        ProjectileWorld projectiles;

        [TearDown] public void TearDown()
        {
            projectiles?.Clear(); BlockModules.Configure(null);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(WeaponActivationMode.Hold)]
        [TestCase(WeaponActivationMode.Press)]
        [TestCase(WeaponActivationMode.Toggle)]
        [TestCase(WeaponActivationMode.ChargeRelease)]
        public void AimedBotUsesOrdinaryTriggerEdgesAndImmediatelyStopsWhenAimIsUnsafe(WeaponActivationMode mode)
        {
            var input = Create(mode, out var weapons, out _);
            for (int i = 0; i < 150; i++)
            {
                input.Apply(true, true, i * .02);
                weapons.Prepare(); weapons.Execute(.02f, 1);
            }
            Assert.That(weapons.ShotsFired, Is.GreaterThanOrEqualTo(2), "Bots must repeat press weapons and release a fully charged weapon.");
            int shots = weapons.ShotsFired;
            input.Apply(false, true, 3);
            Assert.That(weapons.Prepare(), Is.Zero);
            weapons.Execute(1, 1);
            Assert.That(weapons.ShotsFired, Is.EqualTo(shots), "Breaking away or changing to a no-fire drill must cancel a latched toggle or charged shot.");
            if (mode == WeaponActivationMode.Hold) Assert.That(weapons.UseGroupedInput, Is.False);
        }

        [Test] public void BotLockRequirementAndChargeProgressStillUseSharedWeaponRules()
        {
            var input = Create(WeaponActivationMode.ChargeRelease, out var weapons, out var definition);
            definition.requireTargetLock = true;
            input.Apply(true, false, 0); Assert.That(weapons.Prepare(), Is.Zero); weapons.Execute(2, 1);
            Assert.That(weapons.ShotsFired, Is.Zero);
            for (int i = 0; i < 40; i++)
            {
                input.Apply(true, true, i * .02); weapons.Prepare(); weapons.Execute(.02f, 0);
            }
            Assert.That(weapons.ShotsFired, Is.Zero, "An AI request must not finish a charge without electricity.");
            for (int i = 0; i < 50; i++)
            {
                input.Apply(true, true, 1 + i * .02); weapons.Prepare(); weapons.Execute(.02f, 1);
            }
            Assert.That(weapons.ShotsFired, Is.GreaterThan(0));
        }

        RangeBotWeaponInput Create(WeaponActivationMode mode, out ShipWeapons weapons, out WeaponDefinition weapon)
        {
            var database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning = Own(ScriptableObject.CreateInstance<FlightTuning>()); tuning.database = database;
            weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            weapon.activationMode = mode; weapon.shotsPerSecond = 5; weapon.chargeSeconds = .3f;
            database.modules = new List<ModuleDefinition>();
            foreach (var shape in new[] { BlockShape.Core, BlockShape.Laser })
            {
                var definition = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape = shape; definition.massKg = 10; definition.maxHp = 100;
                definition.occupiedCells = new[] { Vector3Int.zero };
                definition.category = shape == BlockShape.Laser ? ModuleCategory.Weapon : ModuleCategory.Core;
                if (shape == BlockShape.Laser) { definition.weapon = weapon; definition.muzzleLocalPosition = Vector3.forward * .6f; }
                database.modules.Add(definition);
            }
            var root = Own(new GameObject("Range bot weapon input fixture"));
            var assembly = root.AddComponent<ShipAssembly>(); assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core, new Vector3(0, .5f, 0), Quaternion.identity), Is.True);
            Assert.That(assembly.Place(BlockShape.Laser, new Vector3(1, .5f, 0), Quaternion.identity), Is.True);
            var blueprint = assembly.Export("Bot input fixture");
            var runtime = root.AddComponent<FlightShipRuntime>(); runtime.Initialize(assembly, blueprint, tuning);
            Physics.SyncTransforms(); projectiles = new ProjectileWorld(); projectiles.Initialize(null);
            weapons = new ShipWeapons(); weapons.Initialize(runtime, projectiles);
            var input = new RangeBotWeaponInput(); input.Initialize(weapons, runtime); return input;
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
