using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponGroupStatefulLockTests
    {
        readonly List<Object> owned = new List<Object>();
        ProjectileWorld projectiles;
        ModuleDatabase previous;

        [SetUp] public void SetUp()=>previous=BlockModules.ConfiguredDatabase;

        [TearDown] public void TearDown()
        {
            projectiles?.Clear();
            BlockModules.Configure(previous);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(WeaponActivationMode.Press)]
        [TestCase(WeaponActivationMode.Toggle)]
        [TestCase(WeaponActivationMode.ChargeRelease)]
        public void LockLostAfterPreparationCancelsStatefulShotWithoutStoppingIndependentGroup(WeaponActivationMode mode)
        {
            var weapons = Create(mode, out var lockedPart);
            weapons.UseGroupedInput = true;
            weapons.HasValidTargetLock = true;
            weapons.SetGroupInput(0, true, true);
            weapons.SetGroupInput(1, true, true);
            Assert.That(weapons.Prepare(), Is.EqualTo(100));
            weapons.Execute(mode == WeaponActivationMode.ChargeRelease ? .25f : .05f, 1);
            Assert.That(weapons.ShotsFired, Is.Zero);

            if (mode == WeaponActivationMode.ChargeRelease)
            {
                Assert.That(weapons.GetChargeProgress(lockedPart), Is.EqualTo(1));
                weapons.SetGroupInput(0, false, false, true);
            }
            weapons.Prepare();
            weapons.HasValidTargetLock = false;
            weapons.Execute(1, 1);
            Assert.That(weapons.ShotsFired, Is.EqualTo(1),
                "Only the independent, unlocked group may emit a real projectile after lock loss.");
            Assert.That(projectiles.TotalSpawned, Is.EqualTo(1));
            Assert.That(weapons.GetChargeProgress(lockedPart), Is.Zero);

            weapons.HasValidTargetLock = true;
            Assert.That(weapons.Prepare(), Is.EqualTo(50),
                "Restoring a lock must not resurrect a cancelled press, toggle or charged release.");
            weapons.Execute(1, 1);
            Assert.That(projectiles.TotalSpawned, Is.EqualTo(2));
        }

        ShipWeapons Create(WeaponActivationMode mode, out string lockedPart)
        {
            var database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning = Own(ScriptableObject.CreateInstance<FlightTuning>()); tuning.database = database;
            var locked = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            locked.activationMode = mode; locked.requireTargetLock = true; locked.chargeSeconds = .25f;
            var ordinary = Own(ScriptableObject.CreateInstance<WeaponDefinition>()); ordinary.shotsPerSecond = 1;
            database.modules = new List<ModuleDefinition>();
            foreach (var shape in new[] { BlockShape.Core, BlockShape.Laser, BlockShape.Thruster1 })
            {
                var definition = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape = shape; definition.massKg = 10; definition.maxHp = 100;
                definition.occupiedCells = new[] { Vector3Int.zero };
                definition.category = shape == BlockShape.Core ? ModuleCategory.Core : ModuleCategory.Weapon;
                if (shape != BlockShape.Core)
                {
                    definition.weapon = shape == BlockShape.Laser ? locked : ordinary;
                    definition.muzzleLocalPosition = Vector3.forward * .6f;
                }
                database.modules.Add(definition);
            }
            var root = Own(new GameObject("Stateful weapon group lock fixture"));
            var assembly = root.AddComponent<ShipAssembly>(); assembly.Configure(database);
            Assert.That(assembly.Place(BlockShape.Core, new Vector3(0, .5f, 0), Quaternion.identity), Is.True);
            Assert.That(assembly.Place(BlockShape.Laser, new Vector3(-1, .5f, 0), Quaternion.identity), Is.True);
            Assert.That(assembly.Place(BlockShape.Thruster1, new Vector3(1, .5f, 0), Quaternion.identity), Is.True);
            var blueprint = assembly.Export("Stateful group lock fixture");
            lockedPart = blueprint.parts.Find(part => part.shape == BlockShape.Laser).id;
            var independent = blueprint.parts.Find(part => part.shape == BlockShape.Thruster1).id;
            var group = WeaponGroups.AddGroup(blueprint, "Independent", new WeaponInputBinding { source = WeaponInputSource.MouseRight }, database);
            Assert.That(WeaponGroups.Assign(blueprint, independent, group.id, database), Is.True);
            var runtime = root.AddComponent<FlightShipRuntime>(); runtime.Initialize(assembly, blueprint, tuning);
            Physics.SyncTransforms();
            projectiles = new ProjectileWorld(); projectiles.Initialize(null);
            var weapons = new ShipWeapons(); weapons.Initialize(runtime, projectiles);
            return weapons;
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
