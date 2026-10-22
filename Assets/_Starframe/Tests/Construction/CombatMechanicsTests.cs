using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;

namespace Starframe.Construction.Tests
{
    public sealed class CombatMechanicsTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<string> temporaryDirectories = new List<string>();

        [TearDown]
        public void TearDown()
        {
            BlockModules.Configure(null);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
            foreach (string path in temporaryDirectories)
                if (Directory.Exists(path)) Directory.Delete(path, true);
            temporaryDirectories.Clear();
        }

        [Test]
        public void DamageReducesHpWithoutChangingMassOrFunctionUntilPartIsLost()
        {
            var ship = CreateShip(3, maxHp: 100);
            float mass = ship.runtime.Performance.totalMass;
            float hp = ship.runtime.Performance.currentHp;
            var cube = Part(ship, BlockShape.Cube, 1);

            Assert.That(ship.runtime.ApplyDamage(cube.record.id, 10), Is.EqualTo(10).Within(.001f));
            Assert.That(ship.runtime.Performance.totalMass, Is.EqualTo(mass).Within(.001f));
            Assert.That(ship.runtime.Performance.currentHp, Is.EqualTo(hp - 10).Within(.001f));
            Assert.That(cube.present, Is.True);
        }

        [Test]
        public void OverkillAndRepeatedHitsCountOnlyHpActuallyRemoved()
        {
            var ship = CreateShip(2);
            var cube = Part(ship, BlockShape.Cube, 1);
            Assert.That(ship.runtime.ApplyDamage(cube.record.id, 1000), Is.EqualTo(cube.maxHp).Within(.001f));
            Assert.That(ship.runtime.ApplyDamage(cube.record.id, 1000), Is.Zero);
            Assert.That(ship.runtime.DamageTaken, Is.EqualTo(cube.maxHp).Within(.001f));
        }

        [Test]
        public void DestroyingBridgeCullsDisconnectedPartsAndTheirMassAndThrust()
        {
            var ship = CreateShip(4);
            float mass = ship.runtime.Performance.totalMass;
            float thrust = ship.runtime.Performance.totalThrust;
            var bridge = Part(ship, BlockShape.Cube, 1);
            var farThruster = Part(ship, BlockShape.Thruster1);

            ship.runtime.ApplyDamage(bridge.record.id, bridge.maxHp);

            Assert.That(bridge.present, Is.False);
            Assert.That(farThruster.present, Is.False);
            Assert.That(ship.runtime.Performance.totalMass, Is.LessThan(mass));
            Assert.That(ship.runtime.Performance.totalThrust, Is.LessThan(thrust));
        }

        [Test]
        public void ExistingPartRecoversAfterOneSecondOutOfCombat()
        {
            var ship = CreateShip(2, recoverySeconds: 10, maxHp: 100);
            var cube = Part(ship, BlockShape.Cube, 1);
            ship.runtime.ApplyDamage(cube.record.id, 20);
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            Assert.That(cube.hp, Is.EqualTo(cube.maxHp - 20).Within(.02f));
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            Assert.That(cube.hp, Is.EqualTo(cube.maxHp - 10).Within(.02f));
        }

        [Test]
        public void MissingPartRebuildsWhenSupportedByPresentNeighbour()
        {
            var ship = CreateShip(3, recoverySeconds: 1);
            var cube = Part(ship, BlockShape.Cube, 1);
            ship.runtime.ApplyDamage(cube.record.id, cube.maxHp);
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            Assert.That(cube.present, Is.False, "The one-second delay has elapsed, but rebuild time has not.");
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            ship.runtime.FlushChanges();
            Assert.That(cube.present, Is.True);
            Assert.That(cube.hp, Is.GreaterThan(0));
        }

        [Test]
        public void NewRebuildCannotSupportNextPartUntilFollowingTick()
        {
            var ship = CreateShip(4, recoverySeconds: 1);
            var first = PartAt(ship, 0, .5f, 1);
            var second = PartAt(ship, 0, .5f, 2);
            ship.runtime.ApplyDamage(first.record.id, first.maxHp);
            ship.runtime.ApplyDamage(second.record.id, second.maxHp);

            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            ship.runtime.FlushChanges();
            Assert.That(first.present, Is.True);
            Assert.That(second.present, Is.False);
            ship.runtime.BeginStep(.1f);
            ship.runtime.AdvanceRecovery(.1f);
            ship.runtime.FlushChanges();
            Assert.That(second.present, Is.False);
            ship.runtime.BeginStep(.9f);
            ship.runtime.AdvanceRecovery(.9f);
            ship.runtime.FlushChanges();
            Assert.That(second.present, Is.True);
        }

        [Test]
        public void TakingDamageResetsMissingPartProgressButPreservesRecoveredHp()
        {
            var ship = CreateShip(3, recoverySeconds: 2, maxHp: 100);
            var damaged = PartAt(ship, 0, .5f, 1);
            var missing = PartAt(ship, 0, .5f, 2);
            ship.runtime.ApplyDamage(damaged.record.id, 80);
            ship.runtime.ApplyDamage(missing.record.id, missing.maxHp);
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            Assert.That(damaged.hp, Is.EqualTo(70).Within(.02f));
            Assert.That(ship.runtime.GetRebuildProgress(missing.record.id), Is.EqualTo(1).Within(.02f));
            ship.runtime.ApplyDamage(damaged.record.id, 5);
            Assert.That(ship.runtime.GetRebuildProgress(missing.record.id), Is.Zero);
            Assert.That(damaged.hp, Is.EqualTo(65).Within(.02f));
        }

        [Test]
        public void DeadCoreStopsRecoveryAndEmitsDeathOnlyOnce()
        {
            var ship = CreateShip(2);
            int deaths = 0;
            ship.runtime.Died += () => deaths++;
            var core = Part(ship, BlockShape.Core);
            ship.runtime.ApplyDamage(core.record.id, core.maxHp);
            ship.runtime.BeginStep(10);
            ship.runtime.AdvanceRecovery(10);
            ship.runtime.FlushChanges();
            ship.runtime.AdvanceRecovery(10);
            Assert.That(ship.runtime.IsAlive, Is.False);
            Assert.That(core.hp, Is.Zero);
            Assert.That(deaths, Is.EqualTo(1));
        }

        [Test]
        public void RespawnRestoresFullShipKeepsPartIdsAndDoesNotEditBlueprint()
        {
            var ship = CreateShip(2);
            string id = ship.blueprint.parts[1].id;
            ship.runtime.ApplyDamage(Part(ship, BlockShape.Cube, 1).record.id, 8);
            ship.runtime.Respawn();
            Assert.That(Part(ship, BlockShape.Cube, 1).hp, Is.EqualTo(Part(ship, BlockShape.Cube, 1).maxHp));
            Assert.That(ship.runtime.Parts.Find(p => p.record.id == id), Is.Not.Null);
            Assert.That(ship.blueprint.parts[1].id, Is.EqualTo(id));
        }

        [Test]
        public void PowerBusClampsShrunkCapacityAndDoesNotChargeForRecovery()
        {
            var bus = new ShipPowerBus();
            bus.Configure(100, 0, true);
            bus.SetEnergy(80);
            bus.Configure(20, 0, false);
            Assert.That(bus.Energy, Is.EqualTo(20).Within(.001f));
            bus.Configure(20, 0, true);
            Assert.That(bus.Energy, Is.EqualTo(20).Within(.001f));
            bus.Configure(100, 0, false);
            Assert.That(bus.Energy, Is.EqualTo(20).Within(.001f));
        }

        [Test]
        public void PowerBusScalesDemandAndHandlesFreeDemandAndNoSupply()
        {
            var bus = new ShipPowerBus();
            bus.Configure(100, 0, true);
            Assert.That(bus.Allocate(1, 40), Is.EqualTo(1).Within(.001f));
            Assert.That(bus.Allocate(1, 80), Is.EqualTo(.75f).Within(.001f));
            Assert.That(bus.Allocate(1, 0), Is.EqualTo(1).Within(.001f));
            bus.Configure(0, 0, true);
            Assert.That(bus.Allocate(1, 1), Is.Zero);
            bus.Configure(100, 0, true);
            bus.SetEnergy(10);
            Assert.That(bus.Allocate(1, 40), Is.EqualTo(.25f).Within(.001f));
            Assert.That(bus.Energy, Is.EqualTo(0).Within(.001f));
            float ratio = bus.Ratio;
            Assert.That(bus.Allocate(0, 100), Is.EqualTo(ratio).Within(.001f));
        }

        [Test]
        public void DestroyedAndRestoredPowerModuleUpdatesGenerationAndCapacity()
        {
            var ship = CreateShip(0, includePower: true, storageJoules: 100, generationWatts: 40);
            var battery = Part(ship, BlockShape.Slab);
            Assert.That(ship.runtime.Power.Capacity, Is.EqualTo(100).Within(.001f));
            Assert.That(ship.runtime.Power.Generation, Is.EqualTo(40).Within(.001f));
            ship.runtime.Power.SetEnergy(80);
            ship.runtime.ApplyDamage(battery.record.id, 10);
            Assert.That(ship.runtime.Power.Capacity, Is.EqualTo(100).Within(.001f));
            Assert.That(ship.runtime.Power.Generation, Is.EqualTo(40).Within(.001f));
            ship.runtime.ApplyDamage(battery.record.id, battery.hp);
            Assert.That(battery.present, Is.False);
            Assert.That(ship.runtime.Power.Capacity, Is.Zero);
            Assert.That(ship.runtime.Power.Generation, Is.Zero);
            Assert.That(ship.runtime.Power.Energy, Is.Zero);
            ship.runtime.SetPartPresent(battery.record.id, true);
            Assert.That(ship.runtime.Power.Capacity, Is.EqualTo(100).Within(.001f));
            Assert.That(ship.runtime.Power.Generation, Is.EqualTo(40).Within(.001f));
            Assert.That(ship.runtime.Power.Energy, Is.Zero);
        }

        [Test]
        public void ThrusterKeepsForceWhileDamagedAndLosesAndRegainsItWithPresence()
        {
            var ship = CreateShip(3, recoverySeconds: 1);
            var thruster = Part(ship, BlockShape.Thruster1);
            Assert.That(ship.runtime.Performance.totalThrust, Is.EqualTo(100).Within(.001f));
            ship.runtime.ApplyDamage(thruster.record.id, 20);
            Assert.That(ship.runtime.Performance.totalThrust, Is.EqualTo(100).Within(.001f));
            ship.runtime.ApplyDamage(thruster.record.id, thruster.hp);
            Assert.That(ship.runtime.Performance.totalThrust, Is.Zero);
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            ship.runtime.BeginStep(1);
            ship.runtime.AdvanceRecovery(1);
            Assert.That(thruster.present, Is.True);
            Assert.That(ship.runtime.Performance.totalThrust, Is.EqualTo(100).Within(.001f));
        }

        [Test]
        public void ThousandPartBlueprintRoundTripsAndRuntimeDisconnectsAndRebuildsBridge()
        {
            var timer = Stopwatch.StartNew();
            var database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning = Own(ScriptableObject.CreateInstance<FlightTuning>());
            tuning.database = database;
            var coreDefinition = MakeDefinition(BlockShape.Core, 100, 1);
            var cubeDefinition = MakeDefinition(BlockShape.Cube, 100, 1);
            database.modules = new List<ModuleDefinition> { coreDefinition, cubeDefinition };
            BlockModules.Configure(database);

            var data = new BlueprintData { name = "Large combat fixture" };
            for (int i = 0; i < 1000; i++)
            {
                var position = new Vector3(0, .5f, i);
                data.parts.Add(new BlockRecord
                {
                    id = "large-part-" + i,
                    shape = i == 0 ? BlockShape.Core : BlockShape.Cube,
                    position = position,
                    cell = BlockPlacement.GridHint(position),
                    rotation = Quaternion.identity
                });
            }
            string directory = Path.Combine(Path.GetTempPath(), "StarframeCombatTests_" + Guid.NewGuid().ToString("N"));
            temporaryDirectories.Add(directory);
            var repository = new BlueprintRepository(directory);
            string savedPath = repository.Save(data);
            var loaded = repository.Load(Path.GetFileName(savedPath));
            Assert.That(loaded.parts.Count, Is.EqualTo(1000));

            var assemblyObject = Own(new GameObject("Large combat fixture"));
            var assembly = assemblyObject.AddComponent<ShipAssembly>();
            typeof(ShipAssembly).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(assembly, null);
            assembly.Configure(database);
            assembly.Import(loaded);
            var runtime = assemblyObject.AddComponent<FlightShipRuntime>();
            var battle = Own(ScriptableObject.CreateInstance<BattleTuning>());
            battle.outOfCombatDelay = 1;
            runtime.Initialize(assembly, loaded, tuning, battle);
            Assert.That(runtime.Parts.Count, Is.EqualTo(1000));

            var bridge = runtime.Parts.Find(p => p.record.id == "large-part-1");
            var next = runtime.Parts.Find(p => p.record.id == "large-part-2");
            runtime.ApplyDamage(bridge.record.id, bridge.maxHp);
            Assert.That(runtime.Parts.FindAll(p => p.present).Count, Is.EqualTo(1));
            runtime.BeginStep(1);
            runtime.AdvanceRecovery(1);
            runtime.BeginStep(1);
            runtime.AdvanceRecovery(1);
            Assert.That(bridge.present, Is.True);
            Assert.That(next.present, Is.False);
            runtime.BeginStep(1);
            runtime.AdvanceRecovery(1);
            Assert.That(next.present, Is.True);
            timer.Stop();
            Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(10), "Guards broad regressions; it is not an FPS target.");
        }

        [Test]
        public void LaserOccupancyRotatesAndUsesOnePartIdentity()
        {
            var ship = CreateShip(1, laserCells: new[] { Vector3Int.zero, Vector3Int.forward }, includeLaser: true);
            var assembly = ship.assembly;
            var laser = ship.runtime.Parts.Find(p => p.record.shape == BlockShape.Laser);
            Assert.That(laser, Is.Not.Null);
            Assert.That(laser.record.id, Is.Not.Empty);
            Vector3Int rotatedCell = BlockPlacement.GridHint(laser.record.position) + Vector3Int.RoundToInt(laser.record.rotation * Vector3.forward);
            Vector3 rotatedPosition = new Vector3(rotatedCell.x, rotatedCell.y + .5f, rotatedCell.z);
            Assert.That(assembly.CanPlace(BlockShape.Cube, rotatedPosition, Quaternion.identity), Is.False);
            Vector3Int unrotatedCell = BlockPlacement.GridHint(laser.record.position) + Vector3Int.forward;
            Vector3 unrotatedPosition = new Vector3(unrotatedCell.x, unrotatedCell.y + .5f, unrotatedCell.z);
            Assert.That(assembly.CanPlace(BlockShape.Cube, unrotatedPosition, Quaternion.identity), Is.True);
            Assert.That(ship.runtime.Parts.FindAll(p => p.record.id == laser.record.id).Count, Is.EqualTo(1));
        }

        ShipFixture CreateShip(int cubeCount, float recoverySeconds = 1, Vector3Int[] laserCells = null, bool includeLaser = false, float maxHp = 100, bool includePower = false, float storageJoules = 0, float generationWatts = 0)
        {
            var database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning = Own(ScriptableObject.CreateInstance<FlightTuning>());
            tuning.database = database;
            var definitions = new List<ModuleDefinition>();
            foreach (BlockShape shape in new[] { BlockShape.Core, BlockShape.Cube, BlockShape.Thruster1, BlockShape.Laser, BlockShape.Slab })
            {
                var definition = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape = shape;
                definition.category = shape == BlockShape.Core ? ModuleCategory.Core : shape == BlockShape.Thruster1 ? ModuleCategory.Propulsion : ModuleCategory.Hull;
                definition.massKg = 10;
                definition.maxHp = maxHp;
                definition.recoverySeconds = recoverySeconds;
                definition.thrustNewtons = shape == BlockShape.Thruster1 ? 100 : 0;
                if (shape == BlockShape.Slab) { definition.storageJoules = storageJoules; definition.generationWatts = generationWatts; }
                definition.occupiedCells = shape == BlockShape.Laser ? (laserCells ?? new[] { Vector3Int.zero, Vector3Int.forward }) : new[] { Vector3Int.zero };
                definitions.Add(definition);
            }
            database.modules = definitions;
            BlockModules.Configure(database);
            var assemblyObject = Own(new GameObject("Combat Test Assembly"));
            var assembly = assemblyObject.AddComponent<ShipAssembly>();
            typeof(ShipAssembly).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(assembly, null);
            assembly.Configure(database);
            assembly.Place(BlockShape.Core, new Vector3(0, .5f, 0), Quaternion.identity);
            for (int i = 0; i < cubeCount; i++)
                assembly.Place(BlockShape.Cube, new Vector3(0, .5f, i + 1), Quaternion.identity);
            if (includeLaser)
                assembly.Place(BlockShape.Laser, new Vector3(0, .5f, cubeCount + 1), Quaternion.Euler(0, 270, 0), 3);
            if (cubeCount >= 3)
                assembly.Place(BlockShape.Thruster1, new Vector3(0, .5f, cubeCount + 1), Quaternion.identity);
            if (includePower)
                assembly.Place(BlockShape.Slab, new Vector3(0, .5f, cubeCount + 1), Quaternion.identity);
            var blueprint = assembly.Export("test");
            var runtime = assemblyObject.AddComponent<FlightShipRuntime>();
            var battle = Own(ScriptableObject.CreateInstance<BattleTuning>());
            battle.outOfCombatDelay = 1;
            battle.recoveryTick = .1f;
            runtime.Initialize(assembly, blueprint, tuning, battle);
            return new ShipFixture(assembly, runtime, blueprint);
        }

        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        ModuleDefinition MakeDefinition(BlockShape shape, float hp, float recoverySeconds)
        {
            var definition = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
            definition.shape = shape;
            definition.maxHp = hp;
            definition.recoverySeconds = recoverySeconds;
            definition.massKg = 10;
            definition.occupiedCells = new[] { Vector3Int.zero };
            return definition;
        }
        static ShipPartState Part(ShipFixture ship, BlockShape shape, int occurrence = 0)
        {
            int found = 0;
            foreach (var part in ship.runtime.Parts)
                if (part.record.shape == shape && found++ == occurrence) return part;
            Assert.Fail("Missing expected part " + shape + " at index " + occurrence);
            return null;
        }
        static ShipPartState PartAt(ShipFixture ship, float x, float y, float z)
        {
            var part = ship.runtime.Parts.Find(p => Mathf.Abs(p.record.position.x - x) < .001f &&
                Mathf.Abs(p.record.position.y - y) < .001f && Mathf.Abs(p.record.position.z - z) < .001f);
            Assert.That(part, Is.Not.Null, "No part at " + x + ", " + y + ", " + z);
            return part;
        }

        sealed class ShipFixture
        {
            public readonly ShipAssembly assembly;
            public readonly FlightShipRuntime runtime;
            public readonly BlueprintData blueprint;
            public ShipFixture(ShipAssembly a, FlightShipRuntime r, BlueprintData b) { assembly = a; runtime = r; blueprint = b; }
        }
    }
}
