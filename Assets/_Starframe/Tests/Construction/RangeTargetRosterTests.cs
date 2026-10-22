using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Construction;
namespace Starframe.Tests
{
    public sealed class RangeTargetRosterTests
    {
        string directory,key;BlueprintRepository repository;
        [SetUp]public void SetUp(){key="RangeRosterTests."+Guid.NewGuid().ToString("N");directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Temp",key));repository=new BlueprintRepository(directory);}
        [TearDown]public void TearDown(){PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();if(Directory.Exists(directory))Directory.Delete(directory,true);}
        static BlueprintData Ship(string name)=>new BlueprintData{name=name,parts=new System.Collections.Generic.List<BlockRecord>{new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0)}}};
        [Test]public void CountsPersistForMultipleBlueprintsWithoutChangingBlueprintFiles()
        {
            repository.Save(Ship("test"));repository.Save(Ship("another"));string original=File.ReadAllText(Path.Combine(directory,"test.json"));
            var roster=new RangeTargetRoster(directory,key);Assert.That(roster.GetCount("test.json"),Is.EqualTo(1));
            roster.SetCount("test.json",3);roster.SetCount("another.json",2);
            var reloaded=new RangeTargetRoster(directory,key);var plans=reloaded.Resolve(null);
            Assert.That(plans.Count,Is.EqualTo(2));Assert.That(plans[0].count,Is.EqualTo(3));Assert.That(plans[1].count,Is.EqualTo(2));
            Assert.That(File.ReadAllText(Path.Combine(directory,"test.json")),Is.EqualTo(original));
            reloaded.SetCount("test.json",0);Assert.That(reloaded.Resolve(null).Count,Is.EqualTo(1));
            reloaded.SetCount("another.json",0);Assert.That(new RangeTargetRoster(directory,key).Resolve(null),Is.Empty);
        }
        [Test]public void DefaultUsesSnapshotOnlyWhenSavedTestIsMissingAndRejectsShipsWithoutCore()
        {
            var snapshot=new TextAsset(JsonUtility.ToJson(Ship("test")));
            try{
                var roster=new RangeTargetRoster(directory,key);Assert.That(roster.Resolve(snapshot)[0].blueprint.parts.Count,Is.EqualTo(1));
                var saved=Ship("test");saved.parts.Add(new BlockRecord{id="cube",shape=BlockShape.Cube,position=new Vector3(1,.5f,0)});repository.Save(saved);
                Assert.That(roster.Resolve(snapshot)[0].blueprint.parts.Count,Is.EqualTo(2));
                repository.Save(new BlueprintData{name="no-core"});
                Assert.Throws<InvalidDataException>(()=>roster.SetCount("no-core.json",1));Assert.That(roster.GetCount("no-core.json"),Is.Zero);
                Assert.Throws<ArgumentException>(()=>roster.SetCount("../outside.json",1));
            }finally{UnityEngine.Object.DestroyImmediate(snapshot);}
        }
        [Test]public void MissingSavedTestHasAVisibleBuiltInOptionThatCanBeClearedAndRestoredAcrossReloads()
        {
            var snapshot=new TextAsset(JsonUtility.ToJson(Ship("Built-in training ship")));
            try{
                var roster=new RangeTargetRoster(directory,key);
                var visible=roster.AvailableTargets(snapshot);
                Assert.That(visible.Count,Is.EqualTo(1));Assert.That(visible[0].builtIn,Is.True);
                Assert.That(visible[0].file,Is.EqualTo("test.json"));Assert.That(visible[0].count,Is.EqualTo(1));
                Assert.That(visible[0].blueprint.name,Is.EqualTo("Built-in training ship"));
                Assert.That(roster.Resolve(snapshot)[0].count,Is.EqualTo(visible[0].count));
                Assert.That(PlayerPrefs.HasKey(key),Is.False,"Listing the implicit default must not write preferences.");

                roster.SetCount(visible[0].file,0,snapshot);
                var cleared=new RangeTargetRoster(directory,key);
                Assert.That(cleared.AvailableTargets(snapshot).Count,Is.EqualTo(1),"A zero-count built-in option must remain adjustable.");
                Assert.That(cleared.AvailableTargets(snapshot)[0].count,Is.Zero);Assert.That(cleared.Resolve(snapshot),Is.Empty);

                cleared.SetCount("test.json",4,snapshot);
                var restored=new RangeTargetRoster(directory,key);
                Assert.That(restored.AvailableTargets(snapshot)[0].count,Is.EqualTo(4));
                var plans=restored.Resolve(snapshot);Assert.That(plans.Count,Is.EqualTo(1));Assert.That(plans[0].count,Is.EqualTo(4));
                Assert.That(plans[0].blueprint.name,Is.EqualTo("Built-in training ship"));
                Assert.That(Directory.Exists(directory),Is.False,"Built-in counts must never create or replace blueprint files.");
                Assert.Throws<ArgumentOutOfRangeException>(()=>restored.SetCount("test.json",-1,snapshot));
                Assert.That(new RangeTargetRoster(directory,key).GetCount("test.json"),Is.EqualTo(4));
            }finally{UnityEngine.Object.DestroyImmediate(snapshot);}
        }
        [Test]public void SavedTestRemainsTheCompatibleSelectionAndTakesPrecedenceOverTheBuiltInOption()
        {
            var snapshot=new TextAsset(JsonUtility.ToJson(Ship("Built-in fallback")));
            try{
                var saved=Ship("test");saved.parts.Add(new BlockRecord{id="hull",shape=BlockShape.Cube,position=new Vector3(1,.5f,0)});
                string path=repository.Save(saved),original=File.ReadAllText(path);
                var roster=new RangeTargetRoster(directory,key);var visible=roster.AvailableTargets(snapshot);
                Assert.That(visible.Count,Is.EqualTo(1));Assert.That(visible[0].builtIn,Is.False);
                Assert.That(visible[0].file,Is.EqualTo("test.json"));Assert.That(visible[0].count,Is.EqualTo(1));
                Assert.That(visible[0].blueprint.parts.Count,Is.EqualTo(2));
                roster.SetCount("test.json",3,snapshot);
                var reloaded=new RangeTargetRoster(directory,key);
                Assert.That(reloaded.AvailableTargets(snapshot)[0].count,Is.EqualTo(3));
                Assert.That(reloaded.Resolve(snapshot)[0].blueprint.parts.Count,Is.EqualTo(2));
                Assert.That(File.ReadAllText(path),Is.EqualTo(original));
            }finally{UnityEngine.Object.DestroyImmediate(snapshot);}
        }
        [Test]public void InvalidGeometryAndMissingCoreOptionsDoNotHideValidRosterEntries()
        {
            repository.Save(Ship("valid"));
            repository.Save(new BlueprintData{name="no-core"});
            var invalid=Ship("overlap");
            invalid.parts.Add(new BlockRecord{id="overlap",shape=BlockShape.Cube,position=invalid.parts[0].position});
            string rejected=JsonUtility.ToJson(invalid),badPath=Path.Combine(directory,"overlap.json");
            File.WriteAllText(badPath,rejected);
            const string selections="{\"entries\":[null,{\"file\":\"valid.json\",\"count\":2}]}";
            PlayerPrefs.SetString(key,selections);
            var roster=new RangeTargetRoster(directory,key);
            var options=roster.AvailableTargets(null);
            Assert.That(options.Count,Is.EqualTo(1));Assert.That(options[0].file,Is.EqualTo("valid.json"));
            Assert.That(options[0].count,Is.EqualTo(2));Assert.That(roster.Resolve(null)[0].count,Is.EqualTo(2));
            Assert.That(PlayerPrefs.GetString(key),Is.EqualTo(selections));
            Assert.That(File.ReadAllText(badPath),Is.EqualTo(rejected));
        }
    }
}
