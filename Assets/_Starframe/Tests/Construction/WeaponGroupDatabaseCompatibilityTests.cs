using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Starframe.Flight;
using Starframe.App;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponGroupDatabaseCompatibilityTests
    {
        [TestCase(BlockShape.Laser)]
        [TestCase(BlockShape.Gun1)][TestCase(BlockShape.Gun2)][TestCase(BlockShape.Gun3)]
        [TestCase(BlockShape.Laser2)][TestCase(BlockShape.Laser3)]
        [TestCase(BlockShape.Railgun1)][TestCase(BlockShape.Railgun2)][TestCase(BlockShape.Railgun3)]
        [TestCase(BlockShape.Missile1)][TestCase(BlockShape.Missile2)][TestCase(BlockShape.Missile3)]
        [TestCase(BlockShape.Sword1)][TestCase(BlockShape.Sword2)]
        public void NativeAssignmentSurvivesDatabaseGapAndWorkspaceReload(BlockShape shape)
        {
            var previous=BlockModules.ConfiguredDatabase;
            string parent=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Temp","WeaponGroupCompatibility"));
            string directory=Path.Combine(parent,Guid.NewGuid().ToString("N"));
            try
            {
                BlockModules.Configure(null);
                using(ShipWorkspace.UseStorageRoot(directory))
                {
                    var data=new BlueprintData{name="Native assignment",parts=new List<BlockRecord>{
                        new BlockRecord{id="weapon",shape=shape,position=Vector3.up*.5f}}};
                    WeaponGroups.Normalize(data);
                    Assert.That(WeaponGroups.Assign(data,"weapon",1),Is.True);
                    ShipWorkspace.Set(data,true,"Native assignment.json");
                    ShipWorkspace.Reload();
                    Assert.That(WeaponGroups.GetGroupId(ShipWorkspace.Ship,"weapon"),Is.EqualTo(1),
                        "Workspace upgrade must preserve the last assignment before database setup.");
                    var repository=ShipWorkspace.Repository;
                    var file=repository.Save(ShipWorkspace.Ship);
                    var loaded=repository.Load(Path.GetFileName(file));
                    Assert.That(WeaponGroups.GetGroupId(loaded,"weapon"),Is.EqualTo(1));
                    Assert.That(loaded.weaponAssignments.Count,Is.EqualTo(1));
                    Assert.That(WeaponGroups.FindGroup(loaded,1).binding.source,Is.EqualTo(WeaponInputSource.MouseRight));
                }
            }
            finally
            {
                BlockModules.Configure(previous);
                if(Directory.Exists(directory))
                {
                    Assert.That(Path.GetFullPath(directory).StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),Is.True);
                    Directory.Delete(directory,true);
                }
            }
        }

        [Test] public void ConfiguredWeaponModuleKeepsItsGroupThroughUpgradeAndRepositoryRoundTrip()
        {
            var previous = BlockModules.ConfiguredDatabase;
            var database = ScriptableObject.CreateInstance<ModuleDatabase>();
            var definition = ScriptableObject.CreateInstance<ModuleDefinition>();
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "WeaponGroupCompatibility"));
            string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            try
            {
                definition.shape = BlockShape.Cube; definition.category = ModuleCategory.Weapon; definition.weapon = weapon;
                definition.occupiedCells = new[] { Vector3Int.zero };
                database.modules = new List<ModuleDefinition> { definition };
                BlockModules.Configure(database);
                var blueprint = new BlueprintData { name = "Future weapon grouping", parts = new List<BlockRecord> {
                    new BlockRecord { id = "core", shape = BlockShape.Core, position = new Vector3(0, .5f, 0) },
                    new BlockRecord { id = "precision", shape = BlockShape.Cube, position = new Vector3(0, 1.5f, 0) }
                }};
                var group = WeaponGroups.AddGroup(blueprint, "Precision", new WeaponInputBinding { source = WeaponInputSource.MouseRight });
                Assert.That(WeaponGroups.Assign(blueprint, "precision", group.id), Is.True);
                string source = JsonUtility.ToJson(blueprint);
                Assert.That(WeaponGroups.GetGroupId(BlueprintRepository.Upgrade(blueprint), "precision"), Is.EqualTo(group.id));
                var repository = new BlueprintRepository(directory);
                string saved = repository.Save(blueprint);
                var loaded = repository.Load(Path.GetFileName(saved));
                Assert.That(WeaponGroups.GetGroupId(loaded, "precision"), Is.EqualTo(group.id));
                Assert.That(WeaponGroups.FindGroup(loaded, group.id).binding.source, Is.EqualTo(WeaponInputSource.MouseRight));
                Assert.That(JsonUtility.ToJson(blueprint), Is.EqualTo(source));

                var explicitDatabase = ScriptableObject.CreateInstance<ModuleDatabase>();
                try
                {
                    explicitDatabase.modules = new List<ModuleDefinition>();
                    Assert.That(WeaponGroups.IsWeapon(blueprint.parts[1], explicitDatabase), Is.False,
                        "An explicit database must take precedence over the configured fallback.");
                }
                finally { UnityEngine.Object.DestroyImmediate(explicitDatabase); }
            }
            finally
            {
                BlockModules.Configure(previous);
                UnityEngine.Object.DestroyImmediate(weapon);
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(database);
                if (Directory.Exists(directory))
                {
                    Assert.That(Path.GetFullPath(directory).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
