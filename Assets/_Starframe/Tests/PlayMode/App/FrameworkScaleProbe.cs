using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.Tests
{
    /// <summary>Manual measurement fixture; it deliberately retains all ships and healthy wreckage between stages.</summary>
    public sealed class FrameworkScaleProbe
    {
        const double WarmupSeconds = 2, SampleSeconds = 8;
        [Serializable] sealed class Counts
        {
            public int targets, actors, blueprintParts, presentShipParts, healthyDebrisParts;
        }
        [Serializable] sealed class Measurement
        {
            public string stage, mode;
            public int expectedTargets, expectedParts, frameSamples;
            public Counts before, after;
            public double warmupRealSeconds, sampleRealSeconds, simulatedSeconds, fixedSimulatedSeconds;
            public double dtP50Ms, dtP95Ms, dtMaxMs;
            public float maxPlayerSpeed, maxBotSpeed, maxSpeedRatio;
            public int nonfiniteActorObservations, outOfBoundsActorObservations, overspeedActorObservations;
            public string firstAnomaly;
            public int projectilesSpawned, totalProjectilesSpawned, peakActiveProjectiles;
            public int gcGeneration0, gcGeneration1, gcGeneration2;
            public long managedBytesBefore, managedBytesAfter;
        }
        [Serializable] sealed class Report
        {
            public string startedUtc, finishedUtc, unityVersion, platform, cpu, gpu, notes;
            public bool isEditor, completed, sourceBlueprintsUnchanged, isolatedBlueprintDirectoryUntouched;
            public int screenWidth, screenHeight, qualityLevel, vSyncCount, targetFrameRate, systemMemoryMb;
            public float fixedDeltaTime, maximumDeltaTime;
            public List<Measurement> measurements = new List<Measurement>();
        }

        IDisposable storageScope;
        string storageRoot, reportPath, savedRoster, savedLocale, savedMission;
        bool hadRoster, hadMode, savedSuspended, savedAppContext, savedRangeReturn, savedSettingsReturn, savedGroupsReturn, savedCursorVisible;
        int savedMode;
        float savedTimeScale;
        CursorLockMode savedCursorLock;
        UnityEngine.Random.State savedRandom;
        AppPage savedReturnPage,savedEditorResume;
        MissionDifficulty savedDifficulty;
        GameSettingsData savedSettings;
        BlueprintData savedFlightDraft, playerFixture, largeFixture, smallFixture;
        string playerJson, largeJson, smallJson;
        bool savedFlightDirty, savedFlightReturn;
        ModuleDatabase savedModuleDatabase;
        Scene rangeScene;
        RangeCombatWorld world;
        Report report;

        [UnitySetUp] public IEnumerator SetUp()
        {
            hadRoster = PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey); savedRoster = PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            hadMode = PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey); savedMode = PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);
            savedSettings = GameSettings.Current.Clone(); savedSuspended = GameInput.Suspended;
            savedAppContext = AppNavigation.HasAppContext; savedReturnPage = AppNavigation.ReturnPage;
            savedEditorResume=AppNavigation.EditorResumePage;
            savedMission = AppNavigation.SelectedMissionId; savedDifficulty = AppNavigation.Difficulty;
            savedRangeReturn = AppNavigation.RangeReturnToEditor; savedSettingsReturn = AppNavigation.SettingsReturnToEditor;
            savedGroupsReturn = AppNavigation.WeaponGroupsReturnToEditor;
            savedTimeScale = Time.timeScale; savedCursorLock = Cursor.lockState; savedCursorVisible = Cursor.visible;
            savedRandom = UnityEngine.Random.state;
            savedFlightDraft = FlightSession.EditorDraft; savedFlightDirty = FlightSession.EditorDirty; savedFlightReturn = FlightSession.ReturningToEditor;
            savedModuleDatabase = (ModuleDatabase)typeof(BlockModules).GetField("database", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            yield return L10n.EnsureReady(); savedLocale = LocalizationSettings.SelectedLocale?.Identifier.Code;

            reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ArcadeFrameworkScaleReport.json"));
            storageRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ArcadeFrameworkScaleProbe", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(storageRoot); storageScope = ShipWorkspace.UseStorageRoot(storageRoot);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, "{\"entries\":[]}");
            PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey, (int)RangeBotMode.FlyOnly);
            AppNavigation.HasAppContext = true; AppNavigation.ReturnPage = AppPage.RangeSetup; AppNavigation.SelectedMissionId = null;
            AppNavigation.Difficulty = MissionDifficulty.Standard; AppNavigation.RangeReturnToEditor = false;
            AppNavigation.SettingsReturnToEditor = false; AppNavigation.WeaponGroupsReturnToEditor = false;
            GameSettings.SetCurrent(savedSettings); GameInput.Suspended = false; Time.timeScale = 1;
            UnityEngine.Random.InitState(26107); BlockModules.Configure(null);
            playerFixture = BuildFixture(1000); largeFixture = BuildFixture(300); smallFixture = BuildFixture(50);
            playerJson = JsonUtility.ToJson(playerFixture); largeJson = JsonUtility.ToJson(largeFixture); smallJson = JsonUtility.ToJson(smallFixture);
            report = new Report
            {
                startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(), isEditor = Application.isEditor,
                cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName, systemMemoryMb = SystemInfo.systemMemorySize,
                screenWidth = Screen.width, screenHeight = Screen.height, qualityLevel = QualitySettings.GetQualityLevel(),
                vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
                fixedDeltaTime = Time.fixedDeltaTime, maximumDeltaTime = Time.maximumDeltaTime,
                notes = "P1-P3 from Documentation/ArcadeFramework/RequirementsAudit.md. Each stage runs FlyOnly and Combat, with 2 s real-time warmup and approximately 8 s real-time sampling per mode. Frame intervals include rendering, Editor/test-runner and probe overhead; they are not standalone-build FPS or CPU marker timings. GC counters cover this process. Ship slot counts include destroyed slots; present ship parts and healthy debris are reported separately. No targets or healthy debris are removed between stages. Player controls and held fire use the existing real flight/weapons execution."
            };
        }

        [UnityTest, Explicit("Manual P1-P3 scale measurement: creates 81 ships / 11000 ship slots; excluded from default full-suite runs.")]
        public IEnumerator MeasureP1P2P3WithRealFlightCombatAndAllAccumulatedWreckage()
        {
            try
            {
                Assert.That(Application.CanStreamedLevelBeLoaded(FlightSession.RangeScene), Is.True, "FlightRange must be installed in Build Settings.");
                FlightSession.Begin(playerFixture, true);
                yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);
                rangeScene = SceneManager.GetActiveScene();
                double deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (!(world = UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>()) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(world && world.Player != null && world.Targeting, Is.True, "The actual FlightRange world must finish initialization.");
                Assert.That(world.Bots.Count, Is.Zero, "The isolated empty roster must suppress default targets.");
                yield return RunStage("P1", 0, 1000);

                world.SpawnTargets(largeFixture, 6); world.SpawnTargets(smallFixture, 14);
                yield return RunStage("P2", 20, 3500);

                // Expand the existing P2 population to four equivalent groups; retain its damage and debris.
                world.SpawnTargets(largeFixture, 18); world.SpawnTargets(smallFixture, 42);
                yield return RunStage("P3", 80, 11000);
                report.sourceBlueprintsUnchanged = playerJson == JsonUtility.ToJson(playerFixture)
                    && largeJson == JsonUtility.ToJson(largeFixture) && smallJson == JsonUtility.ToJson(smallFixture);
                report.isolatedBlueprintDirectoryUntouched = Directory.GetFiles(storageRoot, "*.json", SearchOption.AllDirectories).Length == 0;
                Assert.That(report.sourceBlueprintsUnchanged, Is.True, "Runtime instances must never mutate the memory blueprint fixtures.");
                Assert.That(report.isolatedBlueprintDirectoryUntouched, Is.True, "The probe must not save named blueprints or workspace files.");
                report.completed = true;
            }
            finally
            {
                report.finishedUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            }
        }

        IEnumerator RunStage(string stage, int expectedTargets, int expectedParts)
        {
            foreach (var mode in new[] { RangeBotMode.FlyOnly, RangeBotMode.Combat })
            {
                var measurement = new Measurement { stage = stage, mode = mode.ToString(), expectedTargets = expectedTargets, expectedParts = expectedParts, before = ReadCounts() };
                Assert.That(measurement.before.targets, Is.EqualTo(expectedTargets));
                Assert.That(measurement.before.blueprintParts, Is.EqualTo(expectedParts), "Do not silently reduce a scale fixture.");
                report.measurements.Add(measurement);
                world.SetBotMode(mode, false);
                double warmStart = Time.realtimeSinceStartupAsDouble;
                while (Time.realtimeSinceStartupAsDouble - warmStart < WarmupSeconds)
                {
                    DrivePlayer(mode, Time.realtimeSinceStartupAsDouble - warmStart); yield return null;
                }
                measurement.warmupRealSeconds = Time.realtimeSinceStartupAsDouble - warmStart;
                var intervals = new List<double>(2048);
                int shots = world.Projectiles.TotalSpawned;
                int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                measurement.managedBytesBefore = GC.GetTotalMemory(false);
                double start = Time.realtimeSinceStartupAsDouble, previous = start;
                double simStart = Time.timeAsDouble, fixedStart = Time.fixedTimeAsDouble;
                do
                {
                    DrivePlayer(mode, Time.realtimeSinceStartupAsDouble - start); yield return null;
                    double now = Time.realtimeSinceStartupAsDouble;
                    intervals.Add((now - previous) * 1000); previous = now;
                    ObserveActors(measurement);
                } while (Time.realtimeSinceStartupAsDouble - start < SampleSeconds);
                measurement.sampleRealSeconds = Time.realtimeSinceStartupAsDouble - start;
                measurement.simulatedSeconds = Time.timeAsDouble - simStart;
                measurement.fixedSimulatedSeconds = Time.fixedTimeAsDouble - fixedStart;
                measurement.frameSamples = intervals.Count; intervals.Sort();
                measurement.dtP50Ms = Percentile(intervals, .50); measurement.dtP95Ms = Percentile(intervals, .95);
                measurement.dtMaxMs = intervals[intervals.Count - 1];
                measurement.gcGeneration0 = GC.CollectionCount(0) - gc0;
                measurement.gcGeneration1 = GC.CollectionCount(1) - gc1;
                measurement.gcGeneration2 = GC.CollectionCount(2) - gc2;
                measurement.managedBytesAfter = GC.GetTotalMemory(false);
                measurement.projectilesSpawned = world.Projectiles.TotalSpawned - shots;
                measurement.totalProjectilesSpawned = world.Projectiles.TotalSpawned;
                measurement.after = ReadCounts();
                WriteReport();
                Debug.Log("Framework scale " + stage + " / " + mode + ": " + measurement.after.targets + " targets, " + measurement.after.blueprintParts
                    + " ship slots; " + measurement.frameSamples + " frames over " + measurement.sampleRealSeconds.ToString("F2")
                    + " real s; dt p50/p95/max " + measurement.dtP50Ms.ToString("F2") + "/" + measurement.dtP95Ms.ToString("F2") + "/" + measurement.dtMaxMs.ToString("F2") + " ms.");
                Assert.That(measurement.after.targets, Is.EqualTo(expectedTargets));
                Assert.That(measurement.after.blueprintParts, Is.EqualTo(expectedParts));
            }
        }

        void DrivePlayer(RangeBotMode mode, double elapsed)
        {
            world.Player.flight.SetExternalInput(Vector3.forward, new Vector2(Mathf.Sin((float)elapsed * .7f) * .3f, Mathf.Cos((float)elapsed * .5f) * .2f), boost: elapsed % 4 < 2);
            // Coroutine resumes after Update; the next real FixedUpdate executes this held input.
            world.Player.weapons.UseGroupedInput = false; world.Player.weapons.FireRequested = mode == RangeBotMode.Combat;
        }
        void ObserveActors(Measurement measurement)
        {
            foreach (var actor in world.Actors)
            {
                Vector3 position = actor.body.position, velocity = actor.flight.Velocity; Quaternion rotation = actor.body.rotation;
                float speed = actor.flight.CurrentSpeed, limit = Mathf.Max(1, actor.flight.Tuning.arenaHalfExtent - actor.ship.HullRadius - 1);
                if (!Finite(position) || !Finite(velocity) || !Finite(speed) || !Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w))
                {
                    measurement.nonfiniteActorObservations++;
                    if (measurement.firstAnomaly == null) measurement.firstAnomaly = actor.body.name + ": nonfinite pose or velocity";
                    continue;
                }
                if (actor == world.Player) measurement.maxPlayerSpeed = Mathf.Max(measurement.maxPlayerSpeed, speed);
                else measurement.maxBotSpeed = Mathf.Max(measurement.maxBotSpeed, speed);
                float maxSpeed = actor.ship.Performance.maxSpeed;
                if (maxSpeed > 0) measurement.maxSpeedRatio = Mathf.Max(measurement.maxSpeedRatio, speed / maxSpeed);
                if (speed > maxSpeed + .05f)
                {
                    measurement.overspeedActorObservations++;
                    if (measurement.firstAnomaly == null) measurement.firstAnomaly = actor.body.name + ": speed " + speed + " > " + maxSpeed;
                }
                if (Mathf.Abs(position.x) > limit + .05f || Mathf.Abs(position.y) > limit + .05f || Mathf.Abs(position.z) > limit + .05f)
                {
                    measurement.outOfBoundsActorObservations++;
                    if (measurement.firstAnomaly == null) measurement.firstAnomaly = actor.body.name + ": position " + position + ", centre limit " + limit;
                }
            }
            measurement.peakActiveProjectiles = Mathf.Max(measurement.peakActiveProjectiles, world.Projectiles.ActiveCount);
        }
        Counts ReadCounts()
        {
            var result = new Counts { targets = world.Bots.Count, actors = world.Actors.Count, healthyDebrisParts = world.Debris.PartCount };
            foreach (var actor in world.Actors)
            {
                result.blueprintParts += actor.ship.Parts.Count;
                foreach (var part in actor.ship.Parts) if (part.present && part.hp > 0) result.presentShipParts++;
            }
            return result;
        }
        static BlueprintData BuildFixture(int partCount)
        {
            var data = FlightRangeBootstrap.DefaultShip();
            Assert.That(data.parts.Count, Is.EqualTo(10), "Revisit the audit fixture if the default ship changes.");
            data.name = "Framework scale probe " + partCount;
            data.parts.Add(Cube("scale-bridge", 2, 1, 0));
            for (int z = -1; z >= -10 && data.parts.Count < partCount; z--)
                for (int y = 1; y <= 10 && data.parts.Count < partCount; y++)
                    for (int x = 2; x <= 11 && data.parts.Count < partCount; x++)
                        data.parts.Add(Cube("scale-hull-" + data.parts.Count, x, y, z));
            BlueprintRepository.Validate(data);
            Assert.That(data.parts.Count, Is.EqualTo(partCount));
            return data;
        }
        static BlockRecord Cube(string id, int x, int y, int z) => new BlockRecord
        { id = id, shape = BlockShape.Cube, position = new Vector3(x, y + .5f, z), rotation = Quaternion.identity };
        static double Percentile(List<double> sorted, double percentile) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * percentile) - 1)];
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        void WriteReport()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            if (report != null) { report.finishedUtc = DateTime.UtcNow.ToString("O"); WriteReport(); }
            if (rangeScene.IsValid() && rangeScene.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("FrameworkScaleCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup); yield return SceneManager.UnloadSceneAsync(rangeScene);
            }
            yield return null;
            if (hadRoster) PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, savedRoster); else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            if (hadMode) PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey, savedMode); else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);
            PlayerPrefs.Save();
            if (savedSettings != null) { GameSettings.SetCurrent(savedSettings); GameSettings.Apply(); }
            if (!string.IsNullOrEmpty(savedLocale)) L10n.SelectLocale(savedLocale);
            AppNavigation.HasAppContext = savedAppContext; AppNavigation.ReturnPage = savedReturnPage;
            AppNavigation.EditorResumePage=savedEditorResume;
            AppNavigation.SelectedMissionId = savedMission; AppNavigation.Difficulty = savedDifficulty;
            AppNavigation.RangeReturnToEditor = savedRangeReturn; AppNavigation.SettingsReturnToEditor = savedSettingsReturn;
            AppNavigation.WeaponGroupsReturnToEditor = savedGroupsReturn; GameInput.Suspended = savedSuspended;
            Time.timeScale = savedTimeScale; Cursor.lockState = savedCursorLock; Cursor.visible = savedCursorVisible; UnityEngine.Random.state = savedRandom;
            RestoreFlightProperty(nameof(FlightSession.EditorDraft), savedFlightDraft);
            RestoreFlightProperty(nameof(FlightSession.EditorDirty), savedFlightDirty);
            RestoreFlightProperty(nameof(FlightSession.ReturningToEditor), savedFlightReturn);
            storageScope?.Dispose(); storageScope = null; BlockModules.Configure(savedModuleDatabase);
            if (!string.IsNullOrEmpty(storageRoot) && Directory.Exists(storageRoot))
            {
                string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ArcadeFrameworkScaleProbe")) + Path.DirectorySeparatorChar;
                Assert.That(Path.GetFullPath(storageRoot).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
                Directory.Delete(storageRoot, true);
            }
        }
        static void RestoreFlightProperty(string name, object value)
            => typeof(FlightSession).GetProperty(name, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);
    }
}
