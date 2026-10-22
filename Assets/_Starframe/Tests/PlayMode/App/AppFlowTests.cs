using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.Tests
{
    public sealed class AppFlowTests
    {
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        IDisposable storageScope;string storageRoot,originalRoster,originalLocale,originalMission;
        bool hadRoster,hadMode,originalSuspended,originalAppContext,originalRangeReturn,originalCursorVisible,originalSettingsReturn,originalGroupsReturn;
        int originalMode;float originalTimeScale;CursorLockMode originalCursorLock;
        AppPage originalReturnPage,originalEditorResume;MissionDifficulty originalDifficulty;GameSettingsData originalSettings;
        BlueprintData seed;GameAppBootstrap app;

        [UnitySetUp]public IEnumerator SetUp()
        {
            hadRoster=PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);originalRoster=PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            hadMode=PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey);originalMode=PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);
            originalSuspended=GameInput.Suspended;originalReturnPage=AppNavigation.ReturnPage;originalMission=AppNavigation.SelectedMissionId;
            originalDifficulty=AppNavigation.Difficulty;originalAppContext=AppNavigation.HasAppContext;originalRangeReturn=AppNavigation.RangeReturnToEditor;
            originalSettingsReturn=AppNavigation.SettingsReturnToEditor;originalGroupsReturn=AppNavigation.WeaponGroupsReturnToEditor;
            originalEditorResume=AppNavigation.EditorResumePage;
            originalTimeScale=Time.timeScale;originalCursorLock=Cursor.lockState;originalCursorVisible=Cursor.visible;
            originalSettings=GameSettings.Current.Clone();yield return L10n.EnsureReady();
            originalLocale=LocalizationSettings.SelectedLocale?.Identifier.Code;
            storageRoot=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeAppFlowTests",Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(storageRoot);storageScope=ShipWorkspace.UseStorageRoot(storageRoot);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,"{\"entries\":[]}");PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey,(int)RangeBotMode.Stationary);
            AppNavigation.ReturnPage=AppPage.Home;AppNavigation.SelectedMissionId=null;AppNavigation.Difficulty=MissionDifficulty.Standard;AppNavigation.RangeReturnToEditor=false;
            AppNavigation.SettingsReturnToEditor=AppNavigation.WeaponGroupsReturnToEditor=false;
            Time.timeScale=1;MissionSession.Clear();
            seed=BlueprintRepository.Upgrade(new BlueprintData{name="App integration ship",parts=new List<BlockRecord>{
                new BlockRecord{id="integration-core",shape=BlockShape.Core,position=new Vector3(0,.5f,0)},
                new BlockRecord{id="port-gun",shape=BlockShape.Laser,position=new Vector3(-1,.5f,0)},
                new BlockRecord{id="starboard-gun",shape=BlockShape.Laser,position=new Vector3(1,.5f,0)}
            }});
            ShipWorkspace.Set(seed,true);
        }

        [UnityTearDown]public IEnumerator TearDown()
        {
            // Destroy app/editor callbacks while the isolated storage scope is still active.
            var loaded=SceneManager.GetActiveScene();var cleanup=SceneManager.CreateScene("AppFlowCleanup_"+Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(cleanup);
            if(loaded.IsValid()&&loaded.isLoaded)yield return SceneManager.UnloadSceneAsync(loaded);
            yield return null;
            foreach(var value in owned)if(value)UnityEngine.Object.Destroy(value);owned.Clear();
            MissionSession.Clear();FlightSession.Restored();
            if(hadRoster)PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,originalRoster);else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            if(hadMode)PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey,originalMode);else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);
            PlayerPrefs.Save();
            if(originalSettings!=null){GameSettings.SetCurrent(originalSettings);GameSettings.Apply();}
            if(!string.IsNullOrEmpty(originalLocale))L10n.SelectLocale(originalLocale);
            AppNavigation.ReturnPage=originalReturnPage;AppNavigation.SelectedMissionId=originalMission;AppNavigation.Difficulty=originalDifficulty;
            AppNavigation.HasAppContext=originalAppContext;AppNavigation.RangeReturnToEditor=originalRangeReturn;GameInput.Suspended=originalSuspended;
            AppNavigation.SettingsReturnToEditor=originalSettingsReturn;AppNavigation.WeaponGroupsReturnToEditor=originalGroupsReturn;
            AppNavigation.EditorResumePage=originalEditorResume;
            Time.timeScale=originalTimeScale;Cursor.lockState=originalCursorLock;Cursor.visible=originalCursorVisible;
            storageScope?.Dispose();storageScope=null;BlockModules.Configure(null);
            if(!string.IsNullOrEmpty(storageRoot)&&Directory.Exists(storageRoot)){
                string allowed=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeAppFlowTests"))+Path.DirectorySeparatorChar;
                string resolved=Path.GetFullPath(storageRoot);
                Assert.That(resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase),Is.True,"Only the named test directory may be deleted.");
                Directory.Delete(resolved,true);
            }
        }

        IEnumerator Boot()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(AppNavigation.HomeScene),Is.True,"Run the native app installer so Hangar is in Build Settings.");
            yield return SceneManager.LoadSceneAsync(AppNavigation.HomeScene);
            yield return Await(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.Surface&&app.Surface.Root&&app.Preview&&app.Preview.Assembly;},"Hangar did not become ready.");
        }
        static IEnumerator Await(Func<bool> condition,string message,float seconds=25)
        {
            float deadline=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(condition(),Is.True,message);
        }
        static UnityEngine.UI.Button Button(GameAppBootstrap app,string name)=>app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(value=>value.name==name);
        static TextMeshProUGUI Label(GameAppBootstrap app,string name)=>app.Surface.Root.GetComponentsInChildren<TextMeshProUGUI>().FirstOrDefault(value=>value.name==name);
        static void Click(GameAppBootstrap app,string name)
        {
            var button=Button(app,name);Assert.That(button,Is.Not.Null,"Missing action: "+name);Assert.That(button.IsInteractable(),Is.True,"Action should be available: "+name);button.onClick.Invoke();
        }

        [UnityTest]public IEnumerator HomeBootShowsIndependentCurrentShipInActualThreeDimensionalPreview()
        {
            yield return Boot();Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home));Assert.That(app.Preview.Assembly.Count,Is.EqualTo(seed.parts.Count));
            Assert.That(app.Preview.Assembly.GetComponentsInChildren<MeshRenderer>().Any(value=>value.enabled&&value.sharedMaterial),Is.True);
            Assert.That(Label(app,"CurrentShipName").text,Is.EqualTo(seed.name));
            var core=app.Preview.Assembly.Blocks.First(value=>value.Record.shape==BlockShape.Core);
            var point=app.previewCamera.WorldToViewportPoint(core.transform.position);
            Assert.That(point.z,Is.GreaterThan(0));Assert.That(point.x,Is.InRange(0f,1f));Assert.That(point.y,Is.InRange(0f,1f));
            var previewSnapshot=app.Preview.Assembly.Export(seed.name);previewSnapshot.parts.Clear();
            Assert.That(ShipWorkspace.Ship.parts.Count,Is.EqualTo(seed.parts.Count),"Preview exports must not share the persisted workspace collection.");
            Assert.That(GameInput.Suspended,Is.True);
        }

        [UnityTest]public IEnumerator DeploymentRevalidatesDifficultyAndKeepsConfirmedSnapshotImmutable()
        {
            yield return Boot();
            // Use explicit runtime prices without modifying the installed balance assets.
            var database=Own(ScriptableObject.CreateInstance<ModuleDatabase>());database.modules=new List<ModuleDefinition>();
            foreach(var source in app.tuning.database.modules){if(!source)continue;var definition=Own(UnityEngine.Object.Instantiate(source));definition.cost=source.shape==BlockShape.Core?300:200;database.modules.Add(definition);}
            var tuning=Own(UnityEngine.Object.Instantiate(app.tuning));tuning.database=database;app.tuning=tuning;
            Assert.That(app.catalog&&app.catalog.missions.Count>0,Is.True,"The installer must create at least one operation.");
            var mission=Own(UnityEngine.Object.Instantiate(app.catalog.missions[0]));mission.id="app-flow-budget";mission.sceneName="";mission.maximumMassKg=0;
            float cost=seed.parts.Sum(part=>database.Get(part.shape).cost);mission.baseBudget=cost*.95f;
            mission.relaxedBudgetMultiplier=1.25f;mission.standardBudgetMultiplier=1;mission.expertBudgetMultiplier=.75f;
            var catalog=Own(ScriptableObject.CreateInstance<MissionCatalog>());catalog.missions=new List<MissionDefinition>{mission};app.catalog=catalog;
            AppNavigation.Difficulty=MissionDifficulty.Expert;app.SelectMission(mission.id);
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Deployment));Assert.That(app.CurrentDeployment.IsValid,Is.False);
            Assert.That(Button(app,"ConfirmLaunch").interactable,Is.False);Assert.That(app.ConfirmLaunch(),Is.False);Assert.That(MissionSession.Current,Is.Null);
            var difficulty=app.Surface.Root.GetComponentsInChildren<TMP_Dropdown>().First(value=>value.name=="Difficulty");difficulty.value=(int)MissionDifficulty.Relaxed;
            yield return null;Assert.That(app.CurrentDeployment.IsValid,Is.True);Assert.That(app.CurrentDeployment.Cost,Is.EqualTo(cost));
            Assert.That(Button(app,"ConfirmLaunch").interactable,Is.True);Assert.That(app.ConfirmLaunch(),Is.True);
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.MissionReady));string confirmed=JsonUtility.ToJson(MissionSession.Current);
            var consumerCopy=MissionSession.Current;consumerCopy.ship.parts.Clear();consumerCopy.ship.name="Mutated consumer";
            Assert.That(JsonUtility.ToJson(MissionSession.Current),Is.EqualTo(confirmed),"Consumers must not edit the confirmed battle snapshot.");
            var nextDraft=ShipWorkspace.Ship;nextDraft.name="Later workspace draft";ShipWorkspace.Set(nextDraft,true);seed.name="Changed source";
            Assert.That(JsonUtility.ToJson(MissionSession.Current),Is.EqualTo(confirmed),"Workspace changes after confirmation must not alter the battle request.");
        }

        [UnityTest]public IEnumerator IndividualWeaponAssignmentAppliesAndSurvivesImportExportAndRepository()
        {
            yield return Boot();app.ShowWeaponGroups();yield return null;
            var screen=app.GetComponent<WeaponGroupScreen>();Assert.That(screen,Is.Not.Null);Click(app,"AddWeaponGroup");screen.SelectWeapon("starboard-gun");Click(app,"AssignGroup_1");
            Assert.That(WeaponGroups.GetGroupId(screen.Draft,"starboard-gun"),Is.EqualTo(1));Assert.That(WeaponGroups.GetGroupId(screen.Draft,"port-gun"),Is.Zero);
            Assert.That(WeaponGroups.GetGroupId(ShipWorkspace.Ship,"starboard-gun"),Is.Zero,"Draft edits should stay staged until Apply.");
            L10n.SelectLocale(L10n.IsEnglish?"zh-Hans":"en");yield return null;screen=app.GetComponent<WeaponGroupScreen>();
            Assert.That(WeaponGroups.GetGroupId(screen.Draft,"starboard-gun"),Is.EqualTo(1),"A locale refresh must preserve unapplied grouping edits.");
            Click(app,"WeaponGroupsBack");Click(app,"KeepEditingWeaponGroups");Assert.That(screen.HasUnsavedChanges,Is.True);Click(app,"ApplyWeaponGroups");
            yield return null;Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home));var applied=ShipWorkspace.Ship;
            Assert.That(WeaponGroups.GetGroupId(applied,"starboard-gun"),Is.EqualTo(1));Assert.That(WeaponGroups.GetGroupId(applied,"port-gun"),Is.Zero);
            string path=ShipWorkspace.Repository.Save(applied);var loaded=ShipWorkspace.Repository.Load(Path.GetFileName(path));
            app.Preview.Assembly.Import(loaded);var exported=app.Preview.Assembly.Export(loaded.name);
            Assert.That(WeaponGroups.GetGroupId(exported,"starboard-gun"),Is.EqualTo(1));Assert.That(WeaponGroups.GetGroupId(exported,"port-gun"),Is.Zero);
            Assert.That(exported.weaponGroups.Count,Is.EqualTo(2));Assert.That(WeaponGroups.GetGroupId(seed,"starboard-gun"),Is.Zero);
        }

        [UnityTest]public IEnumerator SettingsLocaleChangesLabelsAndFontsAndReturnsWithoutSavingPreferences()
        {
            yield return Boot();app.ShowSettings();L10n.SelectLocale("en");
            yield return Await(()=>{var label=Label(app,"SettingsTitle");return label&&label.text=="Settings"&&label.font;},"Settings did not render English.");
            var englishFont=Label(app,"SettingsTitle").font;string preferences=JsonUtility.ToJson(GameSettings.Current);
            Canvas.ForceUpdateCanvases();
            foreach(var arrow in app.Surface.Root.GetComponentsInChildren<MenuDropdownArrow>())
                Assert.That(arrow.canvasRenderer&&arrow.canvasRenderer.GetMesh()&&arrow.canvasRenderer.GetMesh().vertexCount>=3,Is.True,"A dropdown arrow must render actual geometry in the live UI.");
            L10n.SelectLocale("zh-Hans");
            yield return Await(()=>{var label=Label(app,"SettingsTitle");return label&&label.text=="设置"&&label.font&&label.font!=englishFont;},"Settings did not switch both translation and font.");
            var chineseFont=Label(app,"SettingsTitle").font;Click(app,"SettingsBack");yield return null;
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home));Assert.That(GameInput.Suspended,Is.True);
            app.ShowSettings();L10n.SelectLocale("en");
            yield return Await(()=>{var label=Label(app,"SettingsTitle");return label&&label.text=="Settings"&&label.font==englishFont;},"The English font was not restored.");
            Assert.That((bool)chineseFont,Is.True,"Switching locale must not destroy package-owned fonts.");Click(app,"SettingsBack");yield return null;
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home));Assert.That(JsonUtility.ToJson(GameSettings.Current),Is.EqualTo(preferences));
            Assert.That(ShipWorkspace.Ship.name,Is.EqualTo(seed.name));
        }

        [UnityTest]public IEnumerator RangeLaunchUsesTheVisibleTargetSelectionFromTheWorkspaceRepository()
        {
            yield return Boot();var target=ShipWorkspace.Clone(seed);target.name="Selected integration target";
            string file=Path.GetFileName(ShipWorkspace.Repository.Save(target));var roster=new RangeTargetRoster(ShipWorkspace.BlueprintDirectory);roster.SetCount(file,2);
            app.ShowRangeSetup();Assert.That(Label(app,"TotalTargets").text,Is.EqualTo(L10n.T("app.targetTotal",2)));app.EnterRange();
            RangeCombatWorld world=null;
            yield return Await(()=>{world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();return world&&world.Player!=null&&world.Targeting;},"Range did not initialize the selected drill.");
            Assert.That(world.Bots.Count,Is.EqualTo(2),"The launched drill must use the roster shown in setup, including the isolated blueprint directory.");
            Assert.That(world.Bots.All(bot=>bot.blueprintName==target.name),Is.True);Assert.That(world.Player.ship.BlueprintSnapshot.name,Is.EqualTo(seed.name));
        }

        [UnityTest]public IEnumerator NumberKeysToggleWeaponGroupsOnceAndHudShowsTheSelection()
        {
            yield return Boot();var vessel=ShipWorkspace.Ship;
            for(int i=1;i<4;i++)WeaponGroups.AddGroup(vessel,null,WeaponInputBinding.MouseLeft());
            WeaponGroups.Assign(vessel,"starboard-gun",3);ShipWorkspace.Set(vessel,true);app.ShowRangeSetup();app.EnterRange();
            RangeCombatWorld world=null;RangeHUD hud=null;
            yield return Await(()=>{world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();return world&&world.Player!=null&&world.Targeting&&hud&&hud.RootCanvas;},"Grouped range did not initialize.");
            world.enabled=false;var weapons=world.Player.weapons;var blueprint=world.Player.ship.BlueprintSnapshot;
            var panel=hud.RootCanvas.transform.Find("WeaponGroupHudGlass").GetComponent<UnityEngine.UI.RawImage>();
            Assert.That(panel.texture,Is.SameAs(hud.WorldCamera.targetTexture),"Late-created panels must use the live frosted-glass background.");
            Assert.That(weapons.Groups.All(group=>weapons.IsGroupEnabled(group.id)),Is.True);
            var keyboard=InputSystem.AddDevice<Keyboard>();
            try{
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit3,Key.Digit4));InputSystem.Update();
                WeaponGroupInputRouter.Apply(weapons,blueprint,false,true);
                Assert.That(weapons.IsGroupEnabled(0)&&weapons.IsGroupEnabled(1),Is.True);
                Assert.That(weapons.IsGroupEnabled(2)||weapons.IsGroupEnabled(3),Is.False);
                yield return null;
                WeaponGroupInputRouter.Apply(weapons,blueprint,false,true);
                Assert.That(weapons.IsGroupEnabled(2)||weapons.IsGroupEnabled(3),Is.False,"Holding a digit cannot repeatedly toggle.");
                yield return new WaitForSecondsRealtime(.16f);
                var label=hud.RootCanvas.transform.Find("WeaponGroupHudLabel_3").GetComponent<TMP_Text>();
                Assert.That(label.text,Does.Contain(L10n.T("hud.weaponGroupOff")));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit3));InputSystem.Update();
                WeaponGroupInputRouter.Apply(weapons,blueprint,false,false);Assert.That(weapons.IsGroupEnabled(2),Is.False,"Menus ignore number toggles.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit3));InputSystem.Update();
                WeaponGroupInputRouter.Apply(weapons,blueprint,false,true);
                Assert.That(weapons.IsGroupEnabled(2),Is.True);Assert.That(weapons.IsGroupEnabled(3),Is.False);
            }finally{InputSystem.RemoveDevice(keyboard);if(world)world.enabled=true;}
        }

        [UnityTest]public IEnumerator HomeEditorAndReturnPreserveUnsavedGeometryAndWeaponAssignments()
        {
            yield return Boot();var vessel=ShipWorkspace.Ship;var group=WeaponGroups.AddGroup(vessel,"Precision");WeaponGroups.Assign(vessel,"starboard-gun",group.id);
            vessel.name="Draft across editor";ShipWorkspace.Set(vessel,true);app.ShowHome();app.OpenEditor();
            ShipEditorController editor=null;AssemblyAppNavigation navigation=null;
            yield return Await(()=>{editor=UnityEngine.Object.FindFirstObjectByType<ShipEditorController>();navigation=UnityEngine.Object.FindFirstObjectByType<AssemblyAppNavigation>();return editor&&navigation&&editor.Assembly&&editor.Assembly.Count==vessel.parts.Count;},"Editor did not restore the workspace draft and app navigation.");
            yield return null;
            Assert.That(editor.Dirty,Is.True);Assert.That(WeaponGroups.GetGroupId(editor.Assembly.Export(vessel.name),"starboard-gun"),Is.EqualTo(group.id));
            Assert.That(editor.Assembly.Place(BlockShape.Cube,new Vector3(0,1.5f,0),Quaternion.identity),Is.True);Assert.That(editor.Dirty,Is.True);
            int count=editor.Assembly.Count;navigation.ReturnToHangar();
            yield return Await(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.CurrentPage==AppPage.Home&&app.Preview&&app.Preview.Assembly;},"Returning from editor did not reopen Hangar.");
            Assert.That(ShipWorkspace.Ship.parts.Count,Is.EqualTo(count));Assert.That(app.Preview.Assembly.Count,Is.EqualTo(count));Assert.That(ShipWorkspace.Dirty,Is.True);
            Assert.That(WeaponGroups.GetGroupId(ShipWorkspace.Ship,"starboard-gun"),Is.EqualTo(group.id));
            ShipWorkspace.Reload();Assert.That(ShipWorkspace.Ship.parts.Count,Is.EqualTo(count));Assert.That(ShipWorkspace.Ship.name,Is.EqualTo(vessel.name));
        }
        [UnityTest]public IEnumerator CompletedOperationReportKeepsTheBlueprintAndPreparesThroughValidationAgain()
        {
            yield return Boot();string original=JsonUtility.ToJson(ShipWorkspace.Ship);app.SelectMission(app.catalog.missions[0].id);Assert.That(app.ConfirmLaunch(),Is.True);
            Assert.That(MissionResults.Finish(MissionOutcome.Victory,new MissionRunSummary{durationSeconds=123,enemiesDestroyed=5,damageDealt=900,damageTaken=300,survivingParts=1}),Is.True);
            app.ShowPage(AppPage.Results);yield return null;Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Results));
            Assert.That(Label(app,"DurationValue").text,Is.EqualTo("02:03"));Assert.That(Label(app,"KillsValue").text,Is.EqualTo("5"));
            Assert.That(JsonUtility.ToJson(ShipWorkspace.Ship),Is.EqualTo(original));Click(app,"ResultsPrepare");yield return null;
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Deployment));Assert.That(app.CurrentDeployment.IsValid,Is.True);Assert.That(Button(app,"ConfirmLaunch").IsInteractable(),Is.True);
        }
        [UnityTest]public IEnumerator FailedWeaponGroupWriteRetainsTheSameDirtyDraftAndCanBeRetried()
        {
            yield return Boot();app.ShowWeaponGroups();var screen=app.GetComponent<WeaponGroupScreen>();Click(app,"AddWeaponGroup");screen.SelectWeapon("starboard-gun");Click(app,"AssignGroup_1");
            string before=File.ReadAllText(ShipWorkspace.WorkspacePath);string blockedTemp=ShipWorkspace.WorkspacePath+".tmp";
            Directory.CreateDirectory(blockedTemp);
            try
            {
                Click(app,"ApplyWeaponGroups");yield return null;
                Assert.That(app.CurrentPage,Is.EqualTo(AppPage.WeaponGroups));Assert.That(app.GetComponent<WeaponGroupScreen>(),Is.SameAs(screen));
                Assert.That(screen.HasUnsavedChanges,Is.True);Assert.That(WeaponGroups.GetGroupId(screen.Draft,"starboard-gun"),Is.EqualTo(1));
                Assert.That(WeaponGroups.GetGroupId(ShipWorkspace.Ship,"starboard-gun"),Is.Zero);Assert.That(File.ReadAllText(ShipWorkspace.WorkspacePath),Is.EqualTo(before));
                Assert.That(Button(app,"ApplyWeaponGroups").IsInteractable(),Is.True);
            }
            finally{Directory.Delete(blockedTemp);}
            Click(app,"ApplyWeaponGroups");yield return null;
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home));Assert.That(WeaponGroups.GetGroupId(ShipWorkspace.Ship,"starboard-gun"),Is.EqualTo(1));
        }
        [UnityTest]public IEnumerator DeploymentEditorGroupsAndSettingsRoundTripKeepsOriginalOperationAndDraft()
        {
            yield return Boot();string mission=app.catalog.missions[0].id;app.SelectMission(mission);app.OpenEditor();
            AssemblyAppNavigation navigation=null;
            yield return Await(()=>{navigation=UnityEngine.Object.FindFirstObjectByType<AssemblyAppNavigation>();return navigation;},"Editor navigation did not initialize.");
            navigation.ReturnToPage(AppPage.WeaponGroups);
            yield return Await(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.GetComponent<WeaponGroupScreen>();},"Weapon groups did not open from the editor.");
            Click(app,"AddWeaponGroup");app.GetComponent<WeaponGroupScreen>().SelectWeapon("starboard-gun");Click(app,"AssignGroup_1");Click(app,"ApplyWeaponGroups");
            yield return Await(()=>{navigation=UnityEngine.Object.FindFirstObjectByType<AssemblyAppNavigation>();return navigation;},"Applying groups did not return to the editor.");
            Assert.That(AppNavigation.ReturnPage,Is.EqualTo(AppPage.Deployment));Assert.That(WeaponGroups.GetGroupId(ShipWorkspace.Ship,"starboard-gun"),Is.EqualTo(1));
            navigation.ReturnToPage(AppPage.Settings);
            yield return Await(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.GetComponent<SettingsScreen>();},"Settings did not open from the editor.");
            Click(app,"SettingsBack");
            yield return Await(()=>{navigation=UnityEngine.Object.FindFirstObjectByType<AssemblyAppNavigation>();return navigation;},"Settings did not return to the editor.");
            Assert.That(AppNavigation.ReturnPage,Is.EqualTo(AppPage.Deployment));navigation.ReturnToHangar();
            yield return Await(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.CurrentPage==AppPage.Deployment;},"The original deployment was lost across subpages.");
            Assert.That(AppNavigation.SelectedMissionId,Is.EqualTo(mission));Assert.That(WeaponGroups.GetGroupId(ShipWorkspace.Ship,"starboard-gun"),Is.EqualTo(1));
        }
        T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
    }
}
