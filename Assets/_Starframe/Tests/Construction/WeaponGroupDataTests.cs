using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponGroupDataTests
    {
        string directory;
        [SetUp]public void SetUp(){BlockModules.Configure(null);directory=Path.Combine(Application.dataPath,"..","Temp","WeaponGroupTests",Guid.NewGuid().ToString("N"));}
        [TearDown]public void TearDown(){BlockModules.Configure(null);if(Directory.Exists(directory))Directory.Delete(directory,true);}

        static BlueprintData Example()=>new BlueprintData{name="Weapon groups",parts=new List<BlockRecord>{
            new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0)},
            new BlockRecord{id="left",shape=BlockShape.Laser,position=new Vector3(-1,.5f,0)},
            new BlockRecord{id="right",shape=BlockShape.Laser,position=new Vector3(1,.5f,0)}
        }};

        [TestCase(1)][TestCase(2)]
        public void OldJsonWithoutOptionalFieldsGetsOneLeftMouseGroup(int version)
        {
            string json="{\"version\":"+version+",\"name\":\"legacy\",\"parts\":[{\"id\":\"gun\",\"shape\":7,\"cell\":{\"x\":0,\"y\":0,\"z\":0},\"position\":{\"x\":0,\"y\":0.5,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}]}";
            var original=JsonUtility.FromJson<BlueprintData>(json);var data=BlueprintRepository.Upgrade(original);
            Assert.That(data.version,Is.EqualTo(2));Assert.That(data.weaponGroups.Count,Is.EqualTo(1));
            Assert.That(data.weaponGroups[0].id,Is.Zero);Assert.That(data.weaponGroups[0].binding.source,Is.EqualTo(WeaponInputSource.MouseLeft));
            Assert.That(data.weaponAssignments.Count,Is.EqualTo(1));Assert.That(data.weaponAssignments[0].partId,Is.EqualTo("gun"));
            Assert.That(data.weaponAssignments[0].groupId,Is.Zero);
        }

        [Test]public void RepositoryRoundTripKeepsIndividualAssignmentsNamesAndMouseBindings()
        {
            var data=Example();var group=WeaponGroups.AddGroup(data,"  Precision fire  ",new WeaponInputBinding{source=WeaponInputSource.MouseRight});
            Assert.That(WeaponGroups.Assign(data,"right",group.id),Is.True);
            var repository=new BlueprintRepository(directory);string path=repository.Save(data);var loaded=repository.Load(Path.GetFileName(path));
            Assert.That(loaded.weaponGroups.Count,Is.EqualTo(2));Assert.That(WeaponGroups.FindGroup(loaded,group.id).name,Is.EqualTo("Precision fire"));
            Assert.That(WeaponGroups.FindGroup(loaded,group.id).binding.source,Is.EqualTo(WeaponInputSource.MouseRight));
            Assert.That(WeaponGroups.GetGroupId(loaded,"right"),Is.EqualTo(group.id));Assert.That(WeaponGroups.GetGroupId(loaded,"left"),Is.Zero);
            loaded.weaponGroups[0].name="Changed";Assert.That(data.weaponGroups[0].name,Is.Not.EqualTo("Changed"));
        }

        [Test]public void KeyboardBindingSurvivesSerializationAndInvalidKeyBecomesUnbound()
        {
            var data=Example();var keyboard=JsonUtility.FromJson<WeaponInputBinding>("{\"source\":4,\"key\":1}");
            var group=WeaponGroups.AddGroup(data,"Keyboard",keyboard);WeaponGroups.Normalize(data);
            Assert.That(WeaponGroups.FindGroup(data,group.id).binding.source,Is.EqualTo(WeaponInputSource.Keyboard));
            StringAssert.Contains("\"key\":1",JsonUtility.ToJson(WeaponGroups.FindGroup(data,group.id).binding));
            group=WeaponGroups.FindGroup(data,group.id);group.binding=JsonUtility.FromJson<WeaponInputBinding>("{\"source\":4,\"key\":99999}");
            WeaponGroups.Normalize(data);Assert.That(WeaponGroups.FindGroup(data,group.id).binding.source,Is.EqualTo(WeaponInputSource.None));
        }

        [Test]public void NormalizeRepairsIdsAndDropsUnknownDeletedAndNonWeaponAssignments()
        {
            var data=Example();data.weaponGroups=new List<WeaponGroupRecord>{
                new WeaponGroupRecord{id=7,name="  Rail\n guns  ",binding=new WeaponInputBinding{source=WeaponInputSource.MouseMiddle}},
                new WeaponGroupRecord{id=7,name="Duplicate"},new WeaponGroupRecord{id=-1,name="Invalid"},null};
            data.weaponAssignments=new List<WeaponGroupAssignment>{
                new WeaponGroupAssignment{partId="left",groupId=999},new WeaponGroupAssignment{partId="right",groupId=7},
                new WeaponGroupAssignment{partId="right",groupId=0},new WeaponGroupAssignment{partId="deleted",groupId=7},
                new WeaponGroupAssignment{partId="core",groupId=7},null};
            WeaponGroups.Normalize(data);
            Assert.That(data.weaponGroups.ConvertAll(g=>g.id),Is.EqualTo(new[]{0,7}));
            Assert.That(WeaponGroups.FindGroup(data,7).name,Is.EqualTo("Rail  guns"));
            Assert.That(data.weaponAssignments.Count,Is.EqualTo(2));Assert.That(WeaponGroups.GetGroupId(data,"left"),Is.Zero);
            Assert.That(WeaponGroups.GetGroupId(data,"right"),Is.EqualTo(7));
            data.parts.RemoveAll(p=>p.id=="right");data.parts.Add(new BlockRecord{id="new-gun",shape=BlockShape.Laser});WeaponGroups.Normalize(data);
            Assert.That(data.weaponAssignments.Exists(a=>a.partId=="right"),Is.False);
            Assert.That(data.weaponAssignments.Exists(a=>a.partId=="new-gun"&&a.groupId==0),Is.True);
        }

        [Test]public void GroupEditsRejectUnknownPartsAndRemovingGroupReturnsWeaponsToDefault()
        {
            var data=Example();var group=WeaponGroups.AddGroup(data,"Auxiliary");
            Assert.That(WeaponGroups.Assign(data,"left",group.id),Is.True);Assert.That(WeaponGroups.Assign(data,"core",group.id),Is.False);
            Assert.That(WeaponGroups.Assign(data,"deleted",group.id),Is.False);Assert.That(WeaponGroups.Assign(data,"right",99),Is.False);
            Assert.That(WeaponGroups.RemoveGroup(data,group.id),Is.True);Assert.That(WeaponGroups.GetGroupId(data,"left"),Is.Zero);
            Assert.That(WeaponGroups.RemoveGroup(data,0),Is.False);Assert.That(WeaponGroups.AddGroup(data).id,Is.EqualTo(group.id));
        }

        [Test]public void UpgradeAndCopyDoNotShareMetadataObjectsWithSource()
        {
            var data=Example();WeaponGroups.AddGroup(data,"Second",new WeaponInputBinding{source=WeaponInputSource.MouseMiddle});WeaponGroups.Assign(data,"right",1);
            var upgraded=BlueprintRepository.Upgrade(data);var exported=Example();WeaponGroups.CopyTo(upgraded,exported);
            exported.weaponGroups[1].binding.source=WeaponInputSource.MouseRight;exported.weaponAssignments[1].groupId=0;
            Assert.That(upgraded.weaponGroups[1].binding.source,Is.EqualTo(WeaponInputSource.MouseMiddle));
            Assert.That(WeaponGroups.GetGroupId(upgraded,"right"),Is.EqualTo(1));
            upgraded.weaponGroups[1].name="Changed";Assert.That(data.weaponGroups[1].name,Is.EqualTo("Second"));
        }
    }
}
