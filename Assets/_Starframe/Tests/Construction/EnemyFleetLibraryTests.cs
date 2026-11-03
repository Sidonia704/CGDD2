using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;

namespace Starframe.Tests
{
    public sealed class EnemyFleetLibraryTests
    {
        [Test] public void ImportNumbersDuplicateNamesAndPreservesTheWorkingShipAndSources()
        {
            string root=Path.Combine(Application.temporaryCachePath,"EnemyFleetLibraryTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var primary=new TextAsset(JsonUtility.ToJson(Ship("Enemy copy")));
            var variant=new TextAsset(JsonUtility.ToJson(Ship("Enemy copy - Siege")));
            var enemy=ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();enemy.id="a";enemy.blueprint=primary;enemy.alternateBlueprint=variant;
            try{
                using(ShipWorkspace.UseStorageRoot(root)){
                    var ship=Ship("Enemy copy");ShipWorkspace.Set(ship,true,"working-source.json");
                    var repository=ShipWorkspace.Repository;string original=repository.Save(ship);
                    string workspaceBefore=File.ReadAllText(ShipWorkspace.WorkspacePath),savedBefore=File.ReadAllText(original),sourceBefore=primary.text;
                    var first=EnemyFleetLibrary.ImportInto(repository,new[]{enemy});
                    Assert.That(first.Length,Is.EqualTo(2));Assert.That(repository.Load(Path.GetFileName(first[0])).name,Is.EqualTo("Enemy copy (2)"));
                    var second=EnemyFleetLibrary.ImportInto(repository,new[]{enemy});
                    Assert.That(repository.Load(Path.GetFileName(second[0])).name,Is.EqualTo("Enemy copy (3)"));
                    Assert.That(repository.Load(Path.GetFileName(second[1])).name,Is.EqualTo("Enemy copy - Siege (2)"));
                    Assert.That(repository.List().Length,Is.EqualTo(5));
                    Assert.That(File.ReadAllText(original),Is.EqualTo(savedBefore));
                    Assert.That(File.ReadAllText(ShipWorkspace.WorkspacePath),Is.EqualTo(workspaceBefore));
                    Assert.That(ShipWorkspace.Ship.name,Is.EqualTo("Enemy copy"));Assert.That(ShipWorkspace.Dirty,Is.True);
                    Assert.That(ShipWorkspace.SourceFile,Is.EqualTo("working-source.json"));Assert.That(primary.text,Is.EqualTo(sourceBefore));
                }
            }finally{UnityEngine.Object.DestroyImmediate(enemy);UnityEngine.Object.DestroyImmediate(primary);UnityEngine.Object.DestroyImmediate(variant);RemoveFixture(root);}
        }

        [Test] public void AnInvalidTemplateStopsThePackBeforeAnyFilesAreWritten()
        {
            string root=Path.Combine(Application.temporaryCachePath,"EnemyFleetLibraryTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var source=new TextAsset(JsonUtility.ToJson(Ship("Valid enemy")));
            var valid=ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();valid.id="a";valid.blueprint=source;
            var invalid=ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();invalid.id="z";
            try{
                var repository=new BlueprintRepository(root);
                Assert.Throws<InvalidOperationException>(()=>EnemyFleetLibrary.ImportInto(repository,new[]{valid,invalid}));
                Assert.That(repository.List(),Is.Empty);
            }finally{UnityEngine.Object.DestroyImmediate(valid);UnityEngine.Object.DestroyImmediate(invalid);UnityEngine.Object.DestroyImmediate(source);RemoveFixture(root);}
        }
        static BlueprintData Ship(string name)
        {
            var ship=new BlueprintData{name=name};ship.parts.Add(new BlockRecord{id="core",shape=BlockShape.Core,position=new Vector3(0,.5f,0),rotation=Quaternion.identity});return ship;
        }
        static void RemoveFixture(string directory)
        {
            string allowed=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"EnemyFleetLibraryTests"))+Path.DirectorySeparatorChar;
            string resolved=Path.GetFullPath(directory);Assert.That(resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase),Is.True);
            if(Directory.Exists(resolved))Directory.Delete(resolved,true);
        }
    }
}
