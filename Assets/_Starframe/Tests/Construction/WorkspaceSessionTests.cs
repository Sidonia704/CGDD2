using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Starframe.App;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class WorkspaceSessionTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        string root;
        IDisposable storage;
        ModuleDatabase database;
        MissionDefinition mission;

        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "StarframeWorkspaceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            storage = ShipWorkspace.UseStorageRoot(root);
            BlockModules.Configure(null);
            MissionSession.Clear();
            database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            foreach (var shape in new[] { BlockShape.Core, BlockShape.Cube })
            {
                var definition = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                definition.shape = shape; definition.cost = 10; definition.massKg = 5;
                definition.category = shape == BlockShape.Core ? ModuleCategory.Core : ModuleCategory.Hull;
                definition.occupiedCells = new[] { Vector3Int.zero };
                database.modules.Add(definition);
            }
            mission = Own(ScriptableObject.CreateInstance<MissionDefinition>());
            mission.id = "isolated-mission"; mission.baseBudget = 100;
        }

        [TearDown] public void TearDown()
        {
            MissionSession.Clear();
            BlockModules.Configure(null);
            storage?.Dispose();
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (!Directory.Exists(root)) return;
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(root).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(Path.GetFileName(root), Does.StartWith("StarframeWorkspaceTests_"));
            Directory.Delete(root, true);
        }

        [Test] public void WorkspaceCopiesInputsAndGettersAndPersistsDirtySourceMetadata()
        {
            var input = Ship("Workspace snapshot");
            ShipWorkspace.Set(input, true, "named-source.json");
            input.name = "Input changed"; input.parts[0].id = "input-change";
            var exposed = ShipWorkspace.Ship;
            exposed.name = "Getter changed"; exposed.parts.Clear();
            ShipWorkspace.Reload();
            Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Workspace snapshot"));
            Assert.That(ShipWorkspace.Ship.parts[0].id, Is.EqualTo("core"));
            Assert.That(ShipWorkspace.Dirty, Is.True);
            Assert.That(ShipWorkspace.SourceFile, Is.EqualTo("named-source.json"));
            Assert.That(ShipWorkspace.WorkspacePath, Is.EqualTo(Path.Combine(root, "Workspace", "current-ship.json")));
        }

        [Test] public void CorruptPrimaryWorkspaceRecoversPreviousValidBackup()
        {
            ShipWorkspace.Set(Ship("Backup ship"), false, "backup-source.json");
            ShipWorkspace.Set(Ship("Current ship"), true, "current-source.json");
            Assert.That(File.Exists(ShipWorkspace.WorkspacePath + ".bak"), Is.True);
            File.WriteAllText(ShipWorkspace.WorkspacePath, "{broken workspace");
            ShipWorkspace.Reload();
            Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Backup ship"));
            Assert.That(ShipWorkspace.Dirty, Is.False);
            Assert.That(ShipWorkspace.SourceFile, Is.EqualTo("backup-source.json"));
        }

        [Test] public void WellFormedWorkspaceWithInvalidGeometryRecoversBackupWithoutReplacingEitherFile()
        {
            ShipWorkspace.Set(Ship("Valid geometry backup"), false, "valid.json");
            ShipWorkspace.Set(Ship("Current workspace"), true);
            string backup = File.ReadAllText(ShipWorkspace.WorkspacePath + ".bak");
            var invalid = Ship("Overlapping workspace");
            invalid.parts[1].position = invalid.parts[0].position;
            string rejected = "{\"version\":1,\"ship\":" + JsonUtility.ToJson(invalid) + ",\"dirty\":true}";
            File.WriteAllText(ShipWorkspace.WorkspacePath, rejected);
            ShipWorkspace.Reload();
            Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Valid geometry backup"));
            Assert.That(ShipWorkspace.Dirty, Is.False);
            Assert.That(ShipWorkspace.SourceFile, Is.EqualTo("valid.json"));
            Assert.That(File.ReadAllText(ShipWorkspace.WorkspacePath), Is.EqualTo(rejected));
            Assert.That(File.ReadAllText(ShipWorkspace.WorkspacePath + ".bak"), Is.EqualTo(backup));
        }

        [Test] public void InvalidGeometryNamedBlueprintIsSkippedWhenRecoveringWorkspace()
        {
            var valid = ShipWorkspace.Repository.Save(Ship("Recoverable named ship"));
            var invalid = Ship("Invalid named ship"); invalid.parts[1].id = invalid.parts[0].id;
            string badFile = Path.Combine(ShipWorkspace.BlueprintDirectory, "invalid.json");
            string rejected = JsonUtility.ToJson(invalid);
            File.WriteAllText(badFile, rejected);
            File.SetLastWriteTimeUtc(badFile, DateTime.UtcNow.AddSeconds(2));
            Assert.That(ShipWorkspace.Repository.List()[0], Is.EqualTo("invalid.json"));
            ShipWorkspace.Reload();
            Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Recoverable named ship"));
            Assert.That(ShipWorkspace.SourceFile, Is.EqualTo(Path.GetFileName(valid)));
            Assert.That(File.ReadAllText(badFile), Is.EqualTo(rejected));
        }

        [Test] public void CorruptWorkspaceAndBackupFallBackToAnIsolatedNamedBlueprint()
        {
            var path = ShipWorkspace.Repository.Save(Ship("Named recovery"));
            string original = File.ReadAllText(path);
            ShipWorkspace.Set(Ship("First workspace"), true);
            ShipWorkspace.Set(Ship("Second workspace"), true);
            File.WriteAllText(ShipWorkspace.WorkspacePath, "{broken primary");
            File.WriteAllText(ShipWorkspace.WorkspacePath + ".bak", "{broken backup");
            ShipWorkspace.Reload();
            Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Named recovery"));
            Assert.That(ShipWorkspace.SourceFile, Is.EqualTo(Path.GetFileName(path)));
            Assert.That(ShipWorkspace.Dirty, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        [Test] public void FailedWorkspaceWritePreservesMemoryDiskMetadataAndChangeNotifications()
        {
            ShipWorkspace.Set(Ship("Committed workspace"), false, "committed.json");
            string before = File.ReadAllText(ShipWorkspace.WorkspacePath);
            Directory.CreateDirectory(ShipWorkspace.WorkspacePath + ".tmp");
            int notifications = 0;
            Action changed = () => notifications++;
            ShipWorkspace.Changed += changed;
            try
            {
                var error = Assert.Catch(() => ShipWorkspace.Set(Ship("Uncommitted workspace"), true, "uncommitted.json"));
                Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
                Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Committed workspace"));
                Assert.That(ShipWorkspace.Dirty, Is.False);
                Assert.That(ShipWorkspace.SourceFile, Is.EqualTo("committed.json"));
                Assert.That(File.ReadAllText(ShipWorkspace.WorkspacePath), Is.EqualTo(before));
                Assert.That(notifications, Is.Zero);
            }
            finally { ShipWorkspace.Changed -= changed; }
        }

        [Test] public void SelectingAndEditingAnIsolatedBlueprintNeverRewritesItsNamedFile()
        {
            var path = ShipWorkspace.Repository.Save(Ship("Named source"));
            string before = File.ReadAllText(path), file = Path.GetFileName(path);
            ShipWorkspace.Select(file);
            Assert.That(ShipWorkspace.SourceFile, Is.EqualTo(file)); Assert.That(ShipWorkspace.Dirty, Is.False);
            var edit = ShipWorkspace.Ship; edit.name = "Workspace edit";
            ShipWorkspace.Set(edit, true, ShipWorkspace.SourceFile);
            ShipWorkspace.Persist(); ShipWorkspace.Reload();
            Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Workspace edit"));
            Assert.That(ShipWorkspace.Dirty, Is.True); Assert.That(ShipWorkspace.SourceFile, Is.EqualTo(file));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            Assert.That(ShipWorkspace.Repository.List(), Is.EqualTo(new[] { file }));
        }

        [Test] public void NestedStorageScopeRestoresThePreviousInMemoryWorkspace()
        {
            ShipWorkspace.Set(Ship("Outer workspace"), true, "outer.json");
            string outerPath = ShipWorkspace.WorkspacePath;
            using (ShipWorkspace.UseStorageRoot(Path.Combine(root, "nested")))
            {
                ShipWorkspace.Set(Ship("Nested workspace"), false, "nested.json");
                Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Nested workspace"));
            }
            Assert.That(ShipWorkspace.WorkspacePath, Is.EqualTo(outerPath));
            Assert.That(ShipWorkspace.Ship.name, Is.EqualTo("Outer workspace"));
            Assert.That(ShipWorkspace.Dirty, Is.True); Assert.That(ShipWorkspace.SourceFile, Is.EqualTo("outer.json"));
        }

        [Test] public void DeploymentSnapshotIsIndependentOfInputEventPayloadAndEveryGetter()
        {
            var input = Ship("Launch snapshot");
            WeaponGroups.Normalize(input); input.weaponGroups[0].name = "Original group";
            DeploymentRequest eventPayload = null;
            Action<DeploymentRequest> launched = request =>
            {
                eventPayload = request;
                request.missionId = "event mutation";
                request.ship.parts[0].id = "event mutation";
                request.ship.weaponGroups[0].name = "event mutation";
            };
            MissionSession.LaunchRequested += launched;
            try
            {
                Assert.That(MissionSession.Confirm(input, database, mission, MissionDifficulty.Expert, out var report), Is.True);
                Assert.That(eventPayload, Is.Not.Null);
                input.name = "input mutation"; input.parts[0].id = "input mutation";
                input.weaponGroups[0].name = "input mutation";
                mission.id = "asset mutation"; mission.baseBudget = 1;
                var exposed = MissionSession.Current;
                Assert.That(exposed.missionId, Is.EqualTo("isolated-mission"));
                Assert.That(exposed.difficulty, Is.EqualTo(MissionDifficulty.Expert));
                Assert.That(exposed.budget, Is.EqualTo(75)); Assert.That(exposed.cost, Is.EqualTo(report.Cost));
                Assert.That(exposed.ship.name, Is.EqualTo("Launch snapshot"));
                Assert.That(exposed.ship.parts[0].id, Is.EqualTo("core"));
                Assert.That(exposed.ship.weaponGroups[0].name, Is.EqualTo("Original group"));
                Assert.That(DateTime.TryParse(exposed.CreatedUtc, out _), Is.True);
                exposed.ship.parts.Clear(); exposed.ship.weaponGroups[0].name = "getter mutation";
                exposed.missionId = "getter mutation";
                Assert.That(MissionSession.Current.ship.parts.Count, Is.EqualTo(2));
                Assert.That(MissionSession.Current.ship.weaponGroups[0].name, Is.EqualTo("Original group"));
                Assert.That(MissionSession.Current.missionId, Is.EqualTo("isolated-mission"));
            }
            finally { MissionSession.LaunchRequested -= launched; }
        }

        [Test] public void FailedConfirmationClearsThePreviousRequestWithoutLaunchingAgain()
        {
            int launches = 0;
            Action<DeploymentRequest> launched = request => launches++;
            MissionSession.LaunchRequested += launched;
            try
            {
                Assert.That(MissionSession.Confirm(Ship("Accepted"), database, mission, MissionDifficulty.Standard, out _), Is.True);
                var invalid = Ship("Rejected"); invalid.parts.RemoveAt(0);
                Assert.That(MissionSession.Confirm(invalid, database, mission, MissionDifficulty.Standard, out var report), Is.False);
                Assert.That(report.Errors.Exists(issue => issue.key == "deploy.error.coreCount"), Is.True);
                Assert.That(MissionSession.Current, Is.Null); Assert.That(launches, Is.EqualTo(1));
            }
            finally { MissionSession.LaunchRequested -= launched; }
        }

        [Test] public void LegacyWorkspaceAndMissionSnapshotsUpgradeWithoutMutatingTheirInput()
        {
            var legacy = Ship("Legacy ship"); legacy.version = 1;
            legacy.parts[0].cell = Vector3Int.zero;
            legacy.parts[1].cell = Vector3Int.right; legacy.parts[1].yawQuarterTurns = 3;
            legacy.parts[1].position = Vector3.zero; legacy.parts[1].rotation = default;
            string before = JsonUtility.ToJson(legacy);
            ShipWorkspace.Set(legacy, true); ShipWorkspace.Reload();
            var stored = ShipWorkspace.Ship;
            Assert.That(stored.version, Is.EqualTo(2));
            Assert.That(stored.parts[1].position, Is.EqualTo(new Vector3(1, .5f, 0)));
            Assert.That(Quaternion.Angle(stored.parts[1].rotation, Quaternion.Euler(0, 270, 0)), Is.LessThan(.001f));
            Assert.That(MissionSession.Confirm(legacy, database, mission, MissionDifficulty.Standard, out _), Is.True);
            Assert.That(MissionSession.Current.ship.version, Is.EqualTo(2));
            Assert.That(MissionSession.Current.ship.parts[1].position, Is.EqualTo(stored.parts[1].position));
            Assert.That(JsonUtility.ToJson(legacy), Is.EqualTo(before));
        }

        [Test] public void MissionReportIsImmutableEmitsOnceAndNeverWritesTheWorkspace()
        {
            var ship=Ship("Report ship");ShipWorkspace.Set(ship,true);string workspace=File.ReadAllText(ShipWorkspace.WorkspacePath);
            Assert.That(MissionSession.Confirm(ship,database,mission,MissionDifficulty.Standard,out _),Is.True);
            int calls=0;Action<MissionResult> completed=result=>{calls++;result.request.ship.parts.Clear();result.summary.damageDealt=0;};
            MissionResults.Completed+=completed;
            try
            {
                var summary=new MissionRunSummary{durationSeconds=125,enemiesDestroyed=4,damageDealt=400,damageTaken=90,survivingParts=1};
                Assert.That(MissionResults.Finish(MissionOutcome.Victory,summary),Is.True);summary.damageDealt=99;
                Assert.That(calls,Is.EqualTo(1));Assert.That(MissionResults.Last.summary.damageDealt,Is.EqualTo(400));Assert.That(MissionResults.Last.request.ship.parts.Count,Is.EqualTo(2));
                var exposed=MissionResults.Last;exposed.request.ship.parts.Clear();exposed.summary.damageTaken=0;
                Assert.That(MissionResults.Last.summary.damageTaken,Is.EqualTo(90));Assert.That(MissionSession.Current.ship.parts.Count,Is.EqualTo(2));
                Assert.That(MissionResults.Finish(MissionOutcome.Defeat,new MissionRunSummary()),Is.False);Assert.That(calls,Is.EqualTo(1));
                Assert.That(File.ReadAllText(ShipWorkspace.WorkspacePath),Is.EqualTo(workspace));
                Assert.That(MissionSession.Confirm(ship,database,mission,MissionDifficulty.Standard,out _),Is.True);Assert.That(MissionResults.Last,Is.Null);
            }
            finally{MissionResults.Completed-=completed;}
        }
        [Test] public void MissionReportRequiresAConfirmedFlightAndClampsInvalidMetrics()
        {
            Assert.That(MissionResults.Finish(MissionOutcome.Defeat,new MissionRunSummary()),Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(()=>MissionResults.Finish((MissionOutcome)999,new MissionRunSummary()));
            Assert.That(MissionSession.Confirm(Ship("Metrics"),database,mission,MissionDifficulty.Standard,out _),Is.True);
            Assert.That(MissionResults.Finish(MissionOutcome.Aborted,new MissionRunSummary{durationSeconds=-1,damageDealt=-9,damageTaken=-3,enemiesDestroyed=-5,survivingParts=99}),Is.True);
            var summary=MissionResults.Last.summary;Assert.That(summary.durationSeconds,Is.Zero);Assert.That(summary.damageDealt,Is.Zero);Assert.That(summary.enemiesDestroyed,Is.Zero);Assert.That(summary.survivingParts,Is.EqualTo(2));
        }
        static BlueprintData Ship(string name) => new BlueprintData
        {
            name = name,
            parts = new List<BlockRecord>
            {
                new BlockRecord { id = "core", shape = BlockShape.Core, position = new Vector3(0, .5f, 0) },
                new BlockRecord { id = "hull", shape = BlockShape.Cube, position = new Vector3(1, .5f, 0) }
            }
        };

        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
    }
}
