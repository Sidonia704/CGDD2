using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Presentation;
using UnityEngine.Localization.Settings;
using L10n = Starframe.Presentation.L10n;

namespace Starframe.Tests
{
    public sealed class Thruster6AssetTests
    {
        const string Root="Assets/_Starframe/";
        [Test] public void Thruster6AppendsStableShapeAndHasCompleteSingleCellAssets()
        {
            Assert.That((int)BlockShape.Cube,Is.EqualTo(0));Assert.That((int)BlockShape.Shield,Is.EqualTo(9));Assert.That((int)BlockShape.Sword2,Is.EqualTo(35));Assert.That((int)BlockShape.Thruster6,Is.EqualTo(36));
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            Assert.That(database.modules.Select(d=>d.shape).Distinct().Count(),Is.EqualTo(Enum.GetValues(typeof(BlockShape)).Length));
            var module=database.Get(BlockShape.Thruster6);Assert.That(module,Is.Not.Null);Assert.That(module.category,Is.EqualTo(ModuleCategory.Propulsion));
            Assert.That(module.occupiedCells,Is.EqualTo(new[]{Vector3Int.zero}));Assert.That(module.mesh,Is.Not.Null);Assert.That(module.icon,Is.Not.Null);Assert.That(module.selectedIcon,Is.Not.Null);
            Assert.That(module.material,Is.SameAs(database.Get(BlockShape.Thruster1).material));Assert.That(module.mesh.vertexCount,Is.GreaterThan(10000));
            var bounds=module.mesh.bounds;Assert.That(bounds.min.y,Is.EqualTo(-.5f).Within(.001f));
            Assert.That(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z),Is.EqualTo(.98f).Within(.001f));
            Assert.That(Mathf.Min(bounds.size.x,bounds.size.y,bounds.size.z),Is.GreaterThan(.90f),"Preserve the supplied authored root scale before uniform normalization.");
            Assert.That(module.weapon,Is.Null);
            var main=database.Get(BlockShape.Thruster1);
            Assert.That(module.thrustNewtons,Is.InRange(main.thrustNewtons/5,main.thrustNewtons/4),"One maneuvering nozzle provides roughly one fifth of a main engine, not five simultaneous main engines.");
            Assert.That(module.cost,Is.GreaterThan(0).And.LessThan(main.cost));
            Assert.That(module.massKg,Is.GreaterThan(0).And.LessThan(main.massKg));Assert.That(module.maxHp,Is.GreaterThan(0));
        }
        [Test] public void FiveMeasuredMouthsMatchTheirExhaustAxesAndKeepTheSideOffset()
        {
            var module=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset").Get(BlockShape.Thruster6);
            var expected=new[]{Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};
            Assert.That(module.localExhaustDirections,Is.EqualTo(expected));Assert.That(module.localNozzlePositions.Length,Is.EqualTo(5));
            for(int i=0;i<5;i++)
            {
                var position=module.localNozzlePositions[i];
                Assert.That(Vector3.Dot(position,expected[i]),Is.GreaterThan(.40f),"Mouth should lie on the measured outer throat plane.");
                Assert.That(module.mesh.bounds.Contains(position),Is.True,"Mouth must remain inside the actual physical frame.");
                if(i>0)Assert.That(position.y,Is.InRange(.08f,.26f),"Side mouths are offset above the thick mounting plate, rather than guessed at the block centre.");
            }
        }
        [Test] public void NativeNamesAndLibrarySlotAreReadyInBothLanguageDataAndRuntime()
        {
            var csv=File.ReadAllText(Root+"Localization/GameUI.csv");
            StringAssert.Contains("\"module.Thruster6\"",csv);StringAssert.Contains("五向微调推进器",csv);StringAssert.Contains("Five-way Maneuvering Thruster",csv);
            // EditMode has no selected locale until a Play session picks one; resolve English explicitly.
            var previous=LocalizationSettings.SelectedLocale;
            try
            {
                LocalizationSettings.SelectedLocale=LocalizationSettings.AvailableLocales.GetLocale("en");
                Assert.That(L10n.T("module.Thruster6"),Is.Not.EqualTo("module.Thruster6"),"Refresh the native localization table after the CSV update.");
                var summary=L10n.T("module.stat.multithrust",5,45,.5f);StringAssert.Contains("45",summary);StringAssert.Contains("0.5",summary);Assert.That(summary,Is.Not.EqualTo("module.stat.multithrust"));
            }
            finally{if(previous)LocalizationSettings.SelectedLocale=previous;}
            var order=typeof(ShipEditorUI).GetMethod("ShapeOrder",BindingFlags.Static|BindingFlags.NonPublic);
            Assert.That((int)order.Invoke(null,new object[]{BlockShape.Thruster6}),Is.GreaterThan((int)order.Invoke(null,new object[]{BlockShape.Thruster5})));
            var size=typeof(ShipEditorUI).GetMethod("IconSize",BindingFlags.Static|BindingFlags.NonPublic);
            Assert.That((Vector2)size.Invoke(null,new object[]{BlockShape.Thruster6}),Is.EqualTo(new Vector2(48,48)));
        }
        [Test] public void SuppliedThruster6SourceFilesRemainUnchanged()
        {
            foreach(var line in File.ReadAllLines("Documentation/Thruster6-Weapons-2026-10-25/Before/SourceAssetHashes.sha256"))
            {
                var parts=line.Split(new[]{"  "},StringSplitOptions.None);
                using(var stream=File.OpenRead(parts[1]))using(var hash=System.Security.Cryptography.SHA256.Create())
                    Assert.That(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-",""),Is.EqualTo(parts[0]),parts[1]);
            }
        }
    }
}
