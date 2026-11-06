using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class AssemblyWeaponGroupGestureTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;AssemblyWeaponGroups overlay;BlueprintData data;
        Starframe.Flight.ModuleDatabase previous;
        HashSet<string> Selection=>(HashSet<string>)typeof(AssemblyWeaponGroups).GetField("selected",Private).GetValue(overlay);
        void Set(string field,object value)=>typeof(AssemblyWeaponGroups).GetField(field,Private).SetValue(overlay,value);
        void Call(string method,params object[] args)=>typeof(AssemblyWeaponGroups).GetMethod(method,Private).Invoke(overlay,args);
        [SetUp]public void SetUp()
        {
            previous=BlockModules.ConfiguredDatabase;BlockModules.Configure(null);
            host=new GameObject("Weapon group gesture fixture");overlay=host.AddComponent<AssemblyWeaponGroups>();
            data=new BlueprintData{parts=new List<BlockRecord>{new BlockRecord{id="small",shape=BlockShape.Gun1},new BlockRecord{id="large",shape=BlockShape.Gun3}}};
            WeaponGroups.Normalize(data);Set("data",data);
        }
        [TearDown]public void TearDown(){Object.DestroyImmediate(host);BlockModules.Configure(previous);}
        void StartDrag(bool moved,params string[] parts){Selection.Clear();foreach(var part in parts)Selection.Add(part);Set("dragging",true);Set("dragMoved",moved);}

        [Test]public void ReleaseCommitsAndLaterGroupClickOnlyInspects()
        {
            StartDrag(true,"small");Call("ReleasePointer",1);
            Assert.That(WeaponGroups.GetGroupId(data,"small"),Is.EqualTo(1));Assert.That(Selection,Is.Empty);
            Call("InspectGroup",0);
            Assert.That(WeaponGroups.GetGroupId(data,"small"),Is.EqualTo(1),"Inspecting another card must not reassign the last weapon.");
            Assert.That((int)typeof(AssemblyWeaponGroups).GetField("selectedGroup",Private).GetValue(overlay),Is.Zero);
        }
        [TestCase(-1)][TestCase(99)]public void InvalidReleaseDiscardsSelectionAndNeverArmsACardClick(int destination)
        {
            StartDrag(true,"small");Call("ReleasePointer",destination);Assert.That(Selection,Is.Empty);
            Call("InspectGroup",1);Assert.That(WeaponGroups.GetGroupId(data,"small"),Is.Zero);
        }
        [Test]public void SingleWeaponClickEndsAtMouseReleaseWithoutAssignment()
        {
            StartDrag(false,"small");Call("ReleasePointer",1);
            Assert.That(Selection,Is.Empty);Assert.That(WeaponGroups.GetGroupId(data,"small"),Is.Zero);
        }
        [Test]public void FamilyDropCommitsEverySelectedPartOnceThenClearsAll()
        {
            StartDrag(true,"small","large");Call("ReleasePointer",1);
            Assert.That(WeaponGroups.GetGroupId(data,"small"),Is.EqualTo(1));Assert.That(WeaponGroups.GetGroupId(data,"large"),Is.EqualTo(1));
            Assert.That(Selection,Is.Empty);Call("InspectGroup",0);
            Assert.That(WeaponGroups.GetGroupId(data,"large"),Is.EqualTo(1));
        }
        [Test]public void EscapeOrFocusCancellationClearsThePendingWeapon()
        {
            StartDrag(true,"small");Assert.That(overlay.CancelDrag(),Is.True);Assert.That(Selection,Is.Empty);
            Call("ReleasePointer",1);Assert.That(WeaponGroups.GetGroupId(data,"small"),Is.Zero);
        }
    }
}
