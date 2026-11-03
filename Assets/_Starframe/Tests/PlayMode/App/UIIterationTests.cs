using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// <summary>Exercise the actual screens, user-sized bilingual text and staged interaction.</summary>
    public sealed class UIIterationTests
    {
        IDisposable storageScope;
        string storageRoot,originalLocale,originalMission,preferencesBefore,workspaceBefore;
        bool originalSuspended,originalContext,originalRangeReturn,originalSettingsReturn,originalGroupsReturn,originalLibraryReturn,originalCursorVisible;
        float originalTimeScale;CursorLockMode originalCursorLock;
        AppPage originalReturnPage,originalEditorResume;MissionDifficulty originalDifficulty;
        GameSettingsData originalSettings;BlueprintData seed;GameAppBootstrap app;

        [UnitySetUp] public IEnumerator SetUp()
        {
            originalSuspended=GameInput.Suspended;originalContext=AppNavigation.HasAppContext;
            originalReturnPage=AppNavigation.ReturnPage;originalEditorResume=AppNavigation.EditorResumePage;
            originalMission=AppNavigation.SelectedMissionId;originalDifficulty=AppNavigation.Difficulty;
            originalRangeReturn=AppNavigation.RangeReturnToEditor;originalSettingsReturn=AppNavigation.SettingsReturnToEditor;originalGroupsReturn=AppNavigation.WeaponGroupsReturnToEditor;
            originalLibraryReturn=AppNavigation.BlueprintsReturnToEditor;
            originalTimeScale=Time.timeScale;originalCursorLock=Cursor.lockState;originalCursorVisible=Cursor.visible;
            originalSettings=GameSettings.Current.Clone();preferencesBefore=ReadPreferences();
            yield return L10n.EnsureReady();originalLocale=LocalizationSettings.SelectedLocale?.Identifier.Code;
            storageRoot=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeUIIterationTests",Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(storageRoot);storageScope=ShipWorkspace.UseStorageRoot(storageRoot);
            AppNavigation.ReturnPage=AppPage.Home;AppNavigation.SelectedMissionId=null;AppNavigation.Difficulty=MissionDifficulty.Standard;
            AppNavigation.RangeReturnToEditor=AppNavigation.SettingsReturnToEditor=AppNavigation.WeaponGroupsReturnToEditor=false;
            AppNavigation.BlueprintsReturnToEditor=false;
            Time.timeScale=1;
            seed=BlueprintRepository.Upgrade(new BlueprintData{name="UI acceptance ship",parts=new List<BlockRecord>{
                new BlockRecord{id="ui-core",shape=BlockShape.Core,position=new Vector3(0,.5f,0)},
                new BlockRecord{id="ui-port-mount",shape=BlockShape.Cube,position=new Vector3(-1,.5f,0)},
                new BlockRecord{id="ui-starboard-mount",shape=BlockShape.Cube,position=new Vector3(1,.5f,0)},
                new BlockRecord{id="ui-port-gun",shape=BlockShape.Laser,position=new Vector3(-1,1.5f,0)},
                new BlockRecord{id="ui-starboard-gun",shape=BlockShape.Laser,position=new Vector3(1,1.5f,0)}
            }});
            ShipWorkspace.Set(seed,true);workspaceBefore=JsonUtility.ToJson(ShipWorkspace.Ship);
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            // Destroy callbacks before restoring the real storage root and global input state.
            var loaded=SceneManager.GetActiveScene();var cleanup=SceneManager.CreateScene("UIIterationCleanup_"+Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(cleanup);
            if(loaded.IsValid()&&loaded.isLoaded&&!string.IsNullOrEmpty(loaded.path))yield return SceneManager.UnloadSceneAsync(loaded);
            yield return null;
            if(originalSettings!=null){GameSettings.SetCurrent(originalSettings);GameSettings.Apply();}
            if(!string.IsNullOrEmpty(originalLocale))L10n.SelectLocale(originalLocale);
            AppNavigation.ReturnPage=originalReturnPage;AppNavigation.EditorResumePage=originalEditorResume;
            AppNavigation.SelectedMissionId=originalMission;AppNavigation.Difficulty=originalDifficulty;AppNavigation.HasAppContext=originalContext;
            AppNavigation.RangeReturnToEditor=originalRangeReturn;AppNavigation.SettingsReturnToEditor=originalSettingsReturn;AppNavigation.WeaponGroupsReturnToEditor=originalGroupsReturn;
            AppNavigation.BlueprintsReturnToEditor=originalLibraryReturn;
            GameInput.Suspended=originalSuspended;Time.timeScale=originalTimeScale;Cursor.lockState=originalCursorLock;Cursor.visible=originalCursorVisible;
            storageScope?.Dispose();storageScope=null;BlockModules.Configure(null);
            if(!string.IsNullOrEmpty(storageRoot)&&Directory.Exists(storageRoot)){
                string allowed=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeUIIterationTests"))+Path.DirectorySeparatorChar;
                string resolved=Path.GetFullPath(storageRoot);
                Assert.That(resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase),Is.True,"Only this test's isolated directory may be removed.");
                Directory.Delete(resolved,true);
            }
            Assert.That(ReadPreferences(),Is.EqualTo(preferencesBefore),"UI tests must not save or replace the player's preferences.");
        }

        IEnumerator Boot()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(AppNavigation.HomeScene),Is.True);
            yield return SceneManager.LoadSceneAsync(AppNavigation.HomeScene);
            yield return Await(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.Surface&&app.Preview&&app.Preview.Assembly;},"Hangar did not initialize.");
        }
        static string ReadPreferences()=>File.Exists(GameSettings.FilePath)?File.ReadAllText(GameSettings.FilePath):null;
        static IEnumerator Await(Func<bool> condition,string message,float seconds=30)
        {
            float deadline=Time.realtimeSinceStartup+seconds;
            while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(condition(),Is.True,message);
        }
        TextMeshProUGUI Label(string name)=>app.Surface.Root.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(value=>value.name==name);
        UnityEngine.UI.Button Button(string name)=>app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(value=>value.name==name);
        void Click(string name)
        {
            var button=Button(name);Assert.That(button,Is.Not.Null,"Missing action "+name);Assert.That(button.IsInteractable(),Is.True,name);button.onClick.Invoke();
        }
        void AssertFits(string name)
        {
            var label=Label(name);Assert.That(label,Is.Not.Null,name);label.ForceMeshUpdate(true,true);
            Assert.That(label.font,Is.Not.Null,name+" needs an actual localized font.");
            Assert.That(label.rectTransform.rect.width,Is.GreaterThan(0),name);
            Assert.That(label.rectTransform.rect.height,Is.GreaterThan(0),name);
            Assert.That(label.isTextOverflowing,Is.False,name+" must remain readable inside its rectangle.");
            Assert.That(label.isTextTruncated,Is.False,name+" must not truncate primary content.");
        }
        void AssertInsideSafePlane(RectTransform rect)
        {
            AssertInside(rect,app.Surface.Root);
        }
        static void AssertInside(RectTransform rect,RectTransform root)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            foreach(var corner in corners){Vector3 point=root.InverseTransformPoint(corner);Assert.That(point.x,Is.InRange(root.rect.xMin-.5f,root.rect.xMax+.5f),rect.name);Assert.That(point.y,Is.InRange(root.rect.yMin-.5f,root.rect.yMax+.5f),rect.name);}
        }
        static void VerifyFixedDesignPlane()
        {
            var parent=new GameObject("UI safe plane test",typeof(RectTransform));
            try{
                var parentRect=(RectTransform)parent.transform;
                var page=new GameObject("Page",typeof(RectTransform));var pageRect=(RectTransform)page.transform;pageRect.SetParent(parentRect,false);
                var card=MenuSurface.Rect(pageRect,"ProbeMissionCard",48,736,1824,266);
                foreach(var size in new[]{new Vector2(3440,1440),new Vector2(2560,1080),new Vector2(1280,1024)}){
                    parentRect.sizeDelta=size;UiStyle.FitSafePlane(pageRect);
                    Assert.That(pageRect.rect.width,Is.EqualTo(1920).Within(.01f));Assert.That(pageRect.rect.height,Is.EqualTo(1080).Within(.01f));
                    Assert.That(pageRect.anchoredPosition,Is.EqualTo(Vector2.zero));Assert.That(pageRect.localScale.x,Is.EqualTo(pageRect.localScale.y).Within(.0001f));
                    AssertInside(pageRect,parentRect);AssertInside(card,pageRect);
                }
            }finally{UnityEngine.Object.Destroy(parent);}
        }
        void AssertFocusInsideModal(string boundaryName)
        {
            var boundary=app.Surface.Root.Find(boundaryName);Assert.That(boundary,Is.Not.Null,boundaryName);
            Assert.That(EventSystem.current,Is.Not.Null);var selected=EventSystem.current.currentSelectedGameObject;
            Assert.That(selected,Is.Not.Null,"A modal should choose a reachable keyboard control.");
            var selectedRoot=selected.transform;Assert.That(selectedRoot.IsChildOf(app.Surface.Root),Is.True);
            while(selectedRoot.parent!=app.Surface.Root&&selectedRoot.parent)selectedRoot=selectedRoot.parent;
            Assert.That(selectedRoot.GetSiblingIndex(),Is.GreaterThanOrEqualTo(boundary.GetSiblingIndex()),"Keyboard focus must stay on the modal side of its pointer-blocking boundary.");
        }

        [UnityTest] public IEnumerator SingleOperationAndBlueprintSearchRemainReadableInBothLocalesAtLargeTextSize()
        {
            VerifyFixedDesignPlane();
            var large=originalSettings.Clone();large.uiScale=1.25f;GameSettings.SetCurrent(large);GameSettings.Apply();
            var alpha=ShipWorkspace.Clone(seed);alpha.name="Alpha Scout";ShipWorkspace.Repository.Save(alpha);
            var beta=ShipWorkspace.Clone(seed);beta.name="Beta Cruiser";ShipWorkspace.Repository.Save(beta);
            yield return Boot();
            foreach(string locale in new[]{"en","zh-Hans"}){
                L10n.SelectLocale(locale);
                yield return Await(()=>L10n.IsEnglish==(locale=="en")&&Label("OperationsTitle")&&Label("OperationsTitle").text==L10n.T("app.chooseOperation"),"Home did not localize.");
                yield return null;Canvas.ForceUpdateCanvases();
                var cards=app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.RawImage>().Where(value=>value.name.StartsWith("Mission_",StringComparison.Ordinal)).ToArray();
                Assert.That(cards.Length,Is.EqualTo(1),"Home should present exactly one operation without deleting future catalogue assets.");
                AssertInsideSafePlane((RectTransform)cards[0].transform);
                foreach(string name in new[]{"CurrentShipName","OperationsTitle","MissionTitle","MissionDescription","EditShipLabel","ChooseShipLabel","RangeSetupLabel"})AssertFits(name);
                app.ShowShipPicker();yield return null;
                Assert.That(app.CurrentPage,Is.EqualTo(AppPage.BlueprintLibrary),"Blueprint management is a full page, not a ship-picker modal.");
                Assert.That(app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Any(value=>value.name.StartsWith("RosterPlus_",StringComparison.Ordinal)||value.name.StartsWith("RosterMinus_",StringComparison.Ordinal)),Is.False,"Training targets belong only in range setup.");
                AssertFits("BlueprintLibraryTitle");AssertFits("LibrarySelectedName");
                Click("LibrarySaveCurrent");yield return null;
                var behindSave=Button("LibraryLoad");Assert.That(behindSave.navigation.mode,Is.EqualTo(UnityEngine.UI.Navigation.Mode.None));
                EventSystem.current.SetSelectedGameObject(behindSave.gameObject);yield return null;AssertFocusInsideModal("BlueprintLibraryModal");
                Click("LibraryModalCancel");yield return null;
                Assert.That(Button("LibraryLoad").navigation.mode,Is.Not.EqualTo(UnityEngine.UI.Navigation.Mode.None));
                var search=app.Surface.Root.GetComponentsInChildren<TMP_InputField>().Single(value=>value.name=="ShipPickerSearch");
                search.text="no-match-for-this-query";yield return null;
                Assert.That(Label("NoSearchResults").gameObject.activeInHierarchy,Is.True);
                Assert.That(app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Count(value=>value.name.StartsWith("PickShip_",StringComparison.Ordinal)),Is.EqualTo(0));
                search.text="ALPHA";yield return null;
                Assert.That(Label("NoSearchResults").gameObject.activeInHierarchy,Is.False);
                var matches=app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Where(value=>value.name.StartsWith("PickShip_",StringComparison.Ordinal)).ToArray();
                Assert.That(matches.Length,Is.EqualTo(1));Assert.That(matches[0].GetComponentInChildren<TextMeshProUGUI>().text,Is.EqualTo("Alpha Scout"));
                search.text="";yield return null;
                Assert.That(app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Count(value=>value.name.StartsWith("PickShip_",StringComparison.Ordinal)),Is.EqualTo(2));
                Click("ClosePicker");yield return null;
                Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home),"Leaving the library returns to its original page.");
                app.ConfirmDialog(L10n.T("app.quitTitle"),L10n.T("app.quitBody"),L10n.T("app.quit"),()=>Assert.Fail("This check must cancel rather than quit."),app.ShowHome);
                var behindConfirmation=Button("EditShip");Assert.That(behindConfirmation.navigation.mode,Is.EqualTo(UnityEngine.UI.Navigation.Mode.None));
                EventSystem.current.SetSelectedGameObject(behindConfirmation.gameObject);yield return null;AssertFocusInsideModal("DialogBlocker");
                Click("ConfirmationCancel");yield return null;
                Assert.That(Button("EditShip").navigation.mode,Is.Not.EqualTo(UnityEngine.UI.Navigation.Mode.None));
            }
            Assert.That(JsonUtility.ToJson(ShipWorkspace.Ship),Is.EqualTo(workspaceBefore),"Searching/cancelling must not replace the editable ship.");
        }

        [UnityTest] public IEnumerator BlueprintLibrarySavesConfirmsOverwriteAndDeletesWithoutRemovingTheWorkingShip()
        {
            yield return Boot();app.ShowShipPicker();yield return null;
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.BlueprintLibrary));
            Click("LibrarySaveCurrent");yield return null;
            app.Surface.Root.GetComponentsInChildren<TMP_InputField>().Single(field=>field.name=="LibraryNameInput").text="Managed design";
            Click("LibrarySaveConfirm");yield return null;
            var files=ShipWorkspace.Repository.List();Assert.That(files.Length,Is.EqualTo(1));string file=files[0];
            Assert.That(ShipWorkspace.Repository.Load(file).name,Is.EqualTo("Managed design"));Assert.That(ShipWorkspace.Dirty,Is.False);
            var revised=ShipWorkspace.Ship;revised.parts.Add(new BlockRecord{id="ui-additional-hull",shape=BlockShape.Cube,position=new Vector3(0,.5f,-1),rotation=Quaternion.identity});
            ShipWorkspace.Set(revised,true,ShipWorkspace.SourceFile);
            Click("LibrarySaveCurrent");yield return null;Click("LibrarySaveConfirm");yield return null;
            Assert.That(Label("LibraryConfirmTitle"),Is.Not.Null,"An existing name needs an explicit overwrite confirmation.");
            Assert.That(ShipWorkspace.Repository.Load(file).parts.Count,Is.EqualTo(seed.parts.Count),"The file must remain untouched until overwrite is confirmed.");
            Click("LibraryConfirmAccept");yield return null;
            Assert.That(ShipWorkspace.Repository.List().Length,Is.EqualTo(1));Assert.That(ShipWorkspace.Repository.Load(file).parts.Count,Is.EqualTo(seed.parts.Count+1));Assert.That(ShipWorkspace.Dirty,Is.False);
            string geometry=JsonUtility.ToJson(ShipWorkspace.Ship);
            Click("LibraryDelete");yield return null;
            Assert.That(ShipWorkspace.Repository.List().Length,Is.EqualTo(1),"Delete needs confirmation before touching the file.");
            Click("LibraryConfirmAccept");yield return null;
            Assert.That(ShipWorkspace.Repository.List(),Is.Empty);Assert.That(JsonUtility.ToJson(ShipWorkspace.Ship),Is.EqualTo(geometry),"Deleting a named file must retain every working-ship part.");
            Assert.That(ShipWorkspace.Dirty,Is.True);Assert.That(ShipWorkspace.SourceFile,Is.Null.Or.Empty,"A deleted source is no longer a saved working ship.");
            Click("ClosePicker");yield return null;Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home));
            Assert.That(ReadPreferences(),Is.EqualTo(preferencesBefore));
        }

        [UnityTest] public IEnumerator InterfacePreferencesAreRealStagedControlsAndDiscardLeavesSavedPreferencesUntouched()
        {
            yield return Boot();app.ShowSettings();yield return null;Click("SettingsTabAccessibility");yield return null;
            var settings=app.GetComponent<SettingsScreen>();Assert.That(settings,Is.Not.Null);
            string beforeCurrent=JsonUtility.ToJson(GameSettings.Current);
            var sliders=app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Slider>().ToDictionary(value=>value.name);
            float scale=Mathf.Abs(settings.Draft.uiScale-1.15f)>.01f?1.15f:.9f;
            float blur=settings.Draft.glassBlur>.5f?.25f:.85f,motion=settings.Draft.hudMotion>.5f?.2f:.9f;
            sliders["uiScaleSlider"].value=scale;sliders["glassBlurSlider"].value=blur;sliders["hudMotionSlider"].value=motion;
            Assert.That(settings.Draft.uiScale,Is.EqualTo(scale).Within(.001f));Assert.That(settings.Draft.glassBlur,Is.EqualTo(blur).Within(.001f));Assert.That(settings.Draft.hudMotion,Is.EqualTo(motion).Within(.001f));
            Assert.That(Button("SettingsApply").IsInteractable(),Is.True);
            Assert.That(JsonUtility.ToJson(GameSettings.Current),Is.EqualTo(beforeCurrent),"Dragging preferences must edit only the settings draft.");
            Click("SettingsBack");yield return null;Click("DiscardSettings");yield return null;
            Assert.That(app.CurrentPage,Is.EqualTo(AppPage.Home));Assert.That(JsonUtility.ToJson(GameSettings.Current),Is.EqualTo(beforeCurrent));
            Assert.That(ReadPreferences(),Is.EqualTo(preferencesBefore));
        }

        [UnityTest] public IEnumerator EightWeaponGroupsFitTheSafePlaneAndIndividualAssignmentsStayStaged()
        {
            var large=originalSettings.Clone();large.uiScale=1.25f;GameSettings.SetCurrent(large);GameSettings.Apply();
            yield return Boot();app.ShowWeaponGroups();yield return null;
            var screen=app.GetComponent<WeaponGroupScreen>();Assert.That(screen,Is.Not.Null);
            while(screen.Draft.weaponGroups.Count<8)Click("AddWeaponGroup");
            Assert.That(Button("AddWeaponGroup").IsInteractable(),Is.False);
            screen.SelectWeapon("ui-starboard-gun");Click("AssignGroup_7");yield return null;
            Assert.That(WeaponGroups.GetGroupId(screen.Draft,"ui-starboard-gun"),Is.EqualTo(7));
            Assert.That(WeaponGroups.GetGroupId(screen.Draft,"ui-port-gun"),Is.Zero);
            foreach(string locale in new[]{"en","zh-Hans"}){
                L10n.SelectLocale(locale);yield return Await(()=>L10n.IsEnglish==(locale=="en")&&Label("WeaponGroupsTitle")&&Label("WeaponGroupsTitle").text==L10n.T("ui.weaponGroups.title"),"Weapon groups did not localize.");
                yield return null;Canvas.ForceUpdateCanvases();
                foreach(var card in app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.RawImage>().Where(value=>value.name.StartsWith("WeaponGroup_",StringComparison.Ordinal)))AssertInsideSafePlane((RectTransform)card.transform);
                for(int id=0;id<8;id++)AssertFits("GroupNumber_"+id);
                AssertFits("WeaponGroupsTitle");Assert.That(screen.Draft.weaponGroups.Count,Is.EqualTo(8));
            }
            app.ShowHome();yield return null;
            Assert.That(JsonUtility.ToJson(ShipWorkspace.Ship),Is.EqualTo(workspaceBefore),"Leaving staged group edits must not persist a loadout.");
            Assert.That(ReadPreferences(),Is.EqualTo(preferencesBefore));
        }
    }
}
