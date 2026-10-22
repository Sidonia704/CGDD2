using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.App;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponBindingConflictTests
    {
        ModuleDatabase previous;
        [SetUp] public void SetUp(){previous=BlockModules.ConfiguredDatabase;BlockModules.Configure(null);}
        [TearDown] public void TearDown()=>BlockModules.Configure(previous);

        [TestCase("<Keyboard>/1", "Digit1", true)]
        [TestCase("<KEYBOARD>/dIgIt1", "Digit1", true)]
        [TestCase("<Keyboard>/0", "Digit0", true)]
        [TestCase("<Keyboard>/leftControl", "LeftCtrl", true)]
        [TestCase("<Keyboard>/CTRL", "RightCtrl", true)]
        [TestCase("<Keyboard>/rightCtrl", "LeftCtrl", false)]
        [TestCase("<Keyboard>/55", "LeftCtrl", false)]
        [TestCase("<Mouse>/leftButton", "Digit1", false)]
        public void KeyboardConflictsUseControlIdentityWithoutAnAttachedDevice(string path, string key, bool expected)
        {
            // Read the persisted key enum by name without coupling this test assembly to an input device.
            var binding = new WeaponInputBinding { source = WeaponInputSource.Keyboard };
            var field = typeof(WeaponInputBinding).GetField("key");
            field.SetValue(binding, Enum.Parse(field.FieldType, key));
            Assert.That(GameInput.MatchesWeaponBinding(path, binding), Is.EqualTo(expected));
        }

        [Test] public void MouseConflictMatchesCaseAndOnlyGroupsWithExistingWeaponsWarn()
        {
            var blueprint = Example();
            var empty = WeaponGroups.AddGroup(blueprint, "Empty", new WeaponInputBinding { source = WeaponInputSource.MouseMiddle });
            string original = JsonUtility.ToJson(blueprint);
            Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<MOUSE>/LEFTBUTTON"), Is.True);
            Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<Mouse>/middleButton"), Is.False);
            blueprint.weaponAssignments.Add(new WeaponGroupAssignment { partId = "missing", groupId = empty.id });
            blueprint.weaponAssignments.Add(new WeaponGroupAssignment { partId = "core", groupId = empty.id });
            Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<Mouse>/middleButton"), Is.False);
            blueprint.weaponAssignments.RemoveAll(assignment => assignment.partId == "missing" || assignment.partId == "core");
            Assert.That(JsonUtility.ToJson(blueprint), Is.EqualTo(original), "Conflict checks must not normalize or modify a draft.");
            Assert.That(WeaponGroups.Assign(blueprint, "gun", empty.id), Is.True);
            Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<Mouse>/middleButton"), Is.True);
            Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<Mouse>/leftButton"), Is.False,
                "The old default group is now empty.");
        }

        [Test] public void LegacyMetadataUsesDefaultWhileOptionalDatabaseRecognizesFutureWeaponModules()
        {
            var blueprint = Example();
            Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<Mouse>/leftButton"), Is.True);
            Assert.That(blueprint.weaponGroups, Is.Empty);
            Assert.That(blueprint.weaponAssignments, Is.Empty);
            var database = ScriptableObject.CreateInstance<ModuleDatabase>();
            var definition = ScriptableObject.CreateInstance<ModuleDefinition>();
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                definition.shape = BlockShape.Shield; definition.category = ModuleCategory.Weapon; definition.weapon = weapon;
                database.modules = new List<ModuleDefinition> { definition };
                blueprint.parts[1].shape = BlockShape.Shield;
                Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<Mouse>/leftButton"), Is.False);
                Assert.That(GameInput.HasWeaponBindingConflict(blueprint, "<Mouse>/leftButton", database), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(weapon);
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        static BlueprintData Example()=>new BlueprintData { parts = new List<BlockRecord> {
            new BlockRecord { id = "core", shape = BlockShape.Core },
            new BlockRecord { id = "gun", shape = BlockShape.Laser }
        }};
    }
}
