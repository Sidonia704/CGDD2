using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class RangeMenuFlowTests
    {
        IDisposable storageScope;
        string storageRoot, savedRoster, savedLocale, savedMission, workspaceJson;
        bool hadRoster, hadMode, savedSuspended, savedAppContext, savedRangeReturn, savedSettingsReturn, savedGroupsReturn, savedCursorVisible;
        int savedMode;
        float savedTimeScale;
        CursorLockMode savedCursorLock;
        UnityEngine.Random.State savedRandom;
        AppPage savedReturnPage,savedEditorResume;
        MissionDifficulty savedDifficulty;
        GameSettingsData savedSettings;
        BlueprintData savedFlightDraft;
        bool savedFlightDirty, savedFlightReturn;
        RangeCombatWorld world;
        RangeHUD hud;
        RangeAppNavigation navigation;
        Scene rangeScene;

        [UnitySetUp] public IEnumerator SetUp()
        {
            hadRoster = PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);
            savedRoster = PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            hadMode = PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey);
            savedMode = PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);
            savedSettings = GameSettings.Current.Clone();
            savedSuspended = GameInput.Suspended;
            savedAppContext = AppNavigation.HasAppContext;
            savedReturnPage = AppNavigation.ReturnPage;
            savedEditorResume=AppNavigation.EditorResumePage;
            savedMission = AppNavigation.SelectedMissionId;
            savedDifficulty = AppNavigation.Difficulty;
            savedRangeReturn = AppNavigation.RangeReturnToEditor;
            savedSettingsReturn = AppNavigation.SettingsReturnToEditor;
            savedGroupsReturn = AppNavigation.WeaponGroupsReturnToEditor;
            savedTimeScale = Time.timeScale;
            savedCursorLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            savedRandom = UnityEngine.Random.state;
            savedFlightDraft = FlightSession.EditorDraft;
            savedFlightDirty = FlightSession.EditorDirty;
            savedFlightReturn = FlightSession.ReturningToEditor;
            yield return L10n.EnsureReady();
            savedLocale = LocalizationSettings.SelectedLocale?.Identifier.Code;

            storageRoot = Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "StarframeRangeMenuTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(storageRoot);
            storageScope = ShipWorkspace.UseStorageRoot(storageRoot);
            AppNavigation.HasAppContext = true;
            AppNavigation.ReturnPage = AppPage.RangeSetup;
            AppNavigation.SelectedMissionId = null;
            AppNavigation.Difficulty = MissionDifficulty.Standard;
            AppNavigation.RangeReturnToEditor = false;
            AppNavigation.SettingsReturnToEditor = false;
            AppNavigation.WeaponGroupsReturnToEditor = false;
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, "{\"entries\":[]}");
            PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey, (int)RangeBotMode.Stationary);
            GameSettings.SetCurrent(savedSettings);
            GameInput.Suspended = false;
            Time.timeScale = 1;
            var ship = FlightRangeBootstrap.DefaultShip();
            ShipWorkspace.Set(ship, true);
            workspaceJson = JsonUtility.ToJson(ShipWorkspace.Ship);
            FlightSession.Begin(ship, true);
            Assert.That(Application.CanStreamedLevelBeLoaded(FlightSession.RangeScene), Is.True, "FlightRange must be installed in Build Settings.");
            yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);
            rangeScene = SceneManager.GetActiveScene();
            yield return Await(() =>
            {
                world = UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
                hud = UnityEngine.Object.FindFirstObjectByType<RangeHUD>();
                navigation = UnityEngine.Object.FindFirstObjectByType<RangeAppNavigation>();
                return world && world.Player != null && world.Targeting && hud && hud.RootCanvas && hud.WorldCamera && navigation;
            }, "The actual range scene did not initialize its HUD navigation.");
            yield return null;
            world.Player.flight.SetExternalInput(Vector3.zero, Vector2.zero, boost: true);
            Assert.That(world.Bots.Count, Is.Zero, "The fixture's empty roster must suppress implicit targets.");
            Assert.That(world.Player.flight.RequestedBoost, Is.True, "The input-lock assertions need an active player control request.");
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            // Dispose scene callbacks before restoring global input and the workspace scope.
            if (rangeScene.IsValid() && rangeScene.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("RangeMenuCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(rangeScene);
            }
            yield return null;
            if (hadRoster) PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, savedRoster);
            else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            if (hadMode) PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey, savedMode);
            else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);
            PlayerPrefs.Save();
            if (savedSettings != null) { GameSettings.SetCurrent(savedSettings); GameSettings.Apply(); }
            if (!string.IsNullOrEmpty(savedLocale)) L10n.SelectLocale(savedLocale);
            AppNavigation.HasAppContext = savedAppContext;
            AppNavigation.ReturnPage = savedReturnPage;
            AppNavigation.EditorResumePage=savedEditorResume;
            AppNavigation.SelectedMissionId = savedMission;
            AppNavigation.Difficulty = savedDifficulty;
            AppNavigation.RangeReturnToEditor = savedRangeReturn;
            AppNavigation.SettingsReturnToEditor = savedSettingsReturn;
            AppNavigation.WeaponGroupsReturnToEditor = savedGroupsReturn;
            GameInput.Suspended = savedSuspended;
            Time.timeScale = savedTimeScale;
            Cursor.lockState = savedCursorLock;
            Cursor.visible = savedCursorVisible;
            UnityEngine.Random.state = savedRandom;
            // These private setters also restore the initial null draft, which Begin cannot express.
            RestoreFlightProperty(nameof(FlightSession.EditorDraft), savedFlightDraft);
            RestoreFlightProperty(nameof(FlightSession.EditorDirty), savedFlightDirty);
            RestoreFlightProperty(nameof(FlightSession.ReturningToEditor), savedFlightReturn);
            storageScope?.Dispose();
            storageScope = null;
            BlockModules.Configure(null);
            if (!string.IsNullOrEmpty(storageRoot) && Directory.Exists(storageRoot))
            {
                string allowed = Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "StarframeRangeMenuTests")) + Path.DirectorySeparatorChar;
                Assert.That(Path.GetFullPath(storageRoot).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
                Directory.Delete(storageRoot, true);
            }
        }

        [UnityTest] public IEnumerator PracticeButtonsChangeEveryModeAndResetOnlyTargetsWhileWreckageKeepsDrifting()
        {
            world.SpawnTargets(FlightRangeBootstrap.DefaultShip(), 1);
            var bot = world.Bot;
            var player = world.Player;
            string roster = PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            hud.OpenExit();
            AssertHudInputLocked();
            Click(hud.RootCanvas.transform, "RangeOptions");
            Assert.That(navigation.IsOpen, Is.True);
            Assert.That(hud.InputOverlayOpen, Is.True);
            Assert.That(GameInput.Suspended, Is.True);
            Assert.That(Overlay().Root.GetComponentsInChildren<UnityEngine.UI.Button>().Count(value => value.name.StartsWith("LiveMode_", StringComparison.Ordinal)), Is.EqualTo(4));
            foreach (RangeBotMode mode in Enum.GetValues(typeof(RangeBotMode)))
            {
                Click(Overlay().Root, "LiveMode_" + mode);
                Assert.That(world.BotMode, Is.EqualTo(mode), "The visible mode action must update the live simulation.");
                Assert.That(RangePracticeSettings.Mode, Is.EqualTo(mode));
                Assert.That(PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey), Is.EqualTo((int)mode));
                Assert.That(world.Bots.Count, Is.EqualTo(1));
                AssertHudInputLocked();
            }
            Click(Overlay().Root, "LiveMode_Stationary");
            player.ship.InvulnerableUntil = 0;
            var playerCore = player.ship.Parts.Find(part => part.record.shape == BlockShape.Core);
            Assert.That(player.ship.ApplyDamage(playerCore.record.id, playerCore.maxHp * .25f, bot.ship), Is.GreaterThan(0));
            bot.ship.InvulnerableUntil = 0;
            Assert.That(bot.ship.ApplyDamage("range-mount", 100000, player.ship), Is.GreaterThan(0));
            Assert.That(world.Debris.PartCount, Is.GreaterThan(0), "Destroying the target's real mount must detach surviving modules.");
            var wreck = world.Debris.Clusters[0];
            var beforeDrift = wreck.Body.position;
            yield return new WaitForSeconds(.15f);
            Assert.That(Vector3.Distance(wreck.Body.position, beforeDrift), Is.GreaterThan(.001f));

            int botLife = bot.ship.LifeId, playerLife = player.ship.LifeId, debrisParts = world.Debris.PartCount;
            float playerHp = playerCore.hp, playerDamage = player.ship.DamageTaken;
            var wreckPosition = wreck.Body.position;
            Click(Overlay().Root, "ResetRangeTargets");
            Assert.That(bot.ship.LifeId, Is.GreaterThan(botLife));
            Assert.That(bot.ship.IsAlive, Is.True);
            foreach (var part in bot.ship.Parts) { Assert.That(part.present, Is.True); Assert.That(part.hp, Is.EqualTo(part.maxHp)); }
            Assert.That(bot.flight.CurrentSpeed, Is.Zero);
            Assert.That(player.ship.LifeId, Is.EqualTo(playerLife));
            Assert.That(playerCore.hp, Is.EqualTo(playerHp), "Target reset must not repair the player.");
            Assert.That(player.ship.DamageTaken, Is.EqualTo(playerDamage));
            Assert.That(world.Debris.PartCount, Is.EqualTo(debrisParts));
            Assert.That(world.Debris.Clusters.Contains(wreck), Is.True);
            Assert.That(wreck && wreck.Body && !wreck.Body.isKinematic && !wreck.Body.useGravity, Is.True);
            Assert.That(world.BotMode, Is.EqualTo(RangeBotMode.Stationary));
            Assert.That(PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey), Is.EqualTo(roster));
            yield return new WaitForSeconds(.15f);
            Assert.That(wreck && wreck.PartCount > 0, Is.True);
            Assert.That(Vector3.Distance(wreck.Body.position, wreckPosition), Is.GreaterThan(.001f), "Reset must retain the existing wreckage's physical drift.");

            Click(Overlay().Root, "CloseRangeOptions");
            yield return null;
            Assert.That(navigation.IsOpen, Is.False);
            Assert.That(hud.InputOverlayOpen, Is.False);
            Assert.That(GameInput.Suspended, Is.False, "Closing the overlay restores its own prior suspension state.");
            AssertHudInputLocked();
            Assert.That(JsonUtility.ToJson(ShipWorkspace.Ship), Is.EqualTo(workspaceJson));
            hud.CloseExit();
            Assert.That(player.flight.RequestedBoost, Is.True, "Only dismissing the underlying exit modal should release the HUD lock.");
        }

        [UnityTest] public IEnumerator SettingsBorrowTheHudRenderTargetAndClosingThemPreservesTheExitModalLock()
        {
            var worldCamera = hud.WorldCamera;
            var renderTarget = worldCamera.targetTexture;
            Assert.That(renderTarget && renderTarget.IsCreated(), Is.True);
            string preferences = JsonUtility.ToJson(GameSettings.Current);
            hud.OpenExit();
            Click(hud.RootCanvas.transform, "RangeSettings");
            var overlay = Overlay();
            var settings = overlay.GetComponent<SettingsScreen>();
            Assert.That(navigation.IsOpen && settings && settings.IsOpen, Is.True);
            Assert.That(overlay.Canvas, Is.SameAs(hud.RootCanvas));
            Assert.That(overlay.DisplayCamera, Is.SameAs(hud.RootCanvas.worldCamera));
            Assert.That(overlay.GetComponentsInChildren<Camera>(true), Is.Empty, "An overlay must borrow the existing HUD camera.");
            Assert.That(overlay.Root.GetComponentsInChildren<UnityEngine.UI.RawImage>().First(value => value.name == "SettingsContent").texture, Is.SameAs(renderTarget));
            Assert.That(GameInput.Suspended && hud.InputOverlayOpen, Is.True);
            AssertHudInputLocked();

            Click(overlay.Root, "SettingsTabControls");
            var sensitivity = overlay.Root.GetComponentsInChildren<UnityEngine.UI.Slider>().First(value => value.name == "sensitivitySlider");
            sensitivity.value = GameSettings.Current.mouseSensitivity < 1.5f ? 2f : .5f;
            Click(overlay.Root, "SettingsBack");
            Assert.That(settings.IsOpen && navigation.IsOpen, Is.True, "Unapplied settings must remain in their discard dialog.");
            AssertHudInputLocked();
            Click(overlay.Root, "DiscardSettings");
            yield return null;
            Assert.That((bool)overlay, Is.False, "Wait for overlay destruction before checking render-target ownership.");
            Assert.That((bool)settings, Is.False);
            AssertBorrowedTargetSurvives(renderTarget);
            Assert.That(GameInput.Suspended, Is.False);
            Assert.That(hud.InputOverlayOpen, Is.False);
            AssertHudInputLocked();
            Assert.That(JsonUtility.ToJson(GameSettings.Current), Is.EqualTo(preferences));

            // A second close must also preserve a suspension owned by another caller.
            GameInput.Suspended = true;
            Click(hud.RootCanvas.transform, "RangeSettings");
            Click(Overlay().Root, "SettingsBack");
            yield return null;
            AssertBorrowedTargetSurvives(renderTarget);
            Assert.That(navigation.IsOpen, Is.False);
            Assert.That(GameInput.Suspended, Is.True);
            AssertHudInputLocked();
            Assert.That(JsonUtility.ToJson(ShipWorkspace.Ship), Is.EqualTo(workspaceJson));
        }

        void AssertHudInputLocked()
        {
            Assert.That(hud.ExitDialogOpen, Is.True);
            Assert.That(hud.RootCanvas.transform.Find("ExitModal").gameObject.activeSelf, Is.True, "The original exit modal must remain behind the overlay.");
            Assert.That(world.Player.flight.RequestedBoost, Is.False, "The existing HUD lock must suppress an active flight request.");
        }
        void AssertBorrowedTargetSurvives(RenderTexture expected)
        {
            Assert.That(expected && expected.IsCreated(), Is.True, "Destroying a borrowed menu surface must not release the HUD render target.");
            Assert.That(hud.WorldCamera.targetTexture, Is.SameAs(expected));
            Assert.That(hud.RootCanvas.transform.Find("FlightView").GetComponent<UnityEngine.UI.RawImage>().texture, Is.SameAs(expected));
        }
        MenuSurface Overlay()
        {
            var result = hud.RootCanvas.GetComponentsInChildren<MenuSurface>().FirstOrDefault(value => value.name == "RangeOptionsOverlay");
            Assert.That(result, Is.Not.Null, "The HUD button must create the live range overlay.");
            return result;
        }
        static void Click(Transform root, string name)
        {
            var button = root.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(value => value.name == name);
            Assert.That(button, Is.Not.Null, "Missing action: " + name);
            Assert.That(button.IsInteractable(), Is.True, "Unavailable action: " + name);
            button.onClick.Invoke();
        }
        static IEnumerator Await(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }
        static void RestoreFlightProperty(string name, object value)
            => typeof(FlightSession).GetProperty(name, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);
    }
}
