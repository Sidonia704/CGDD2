using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
    public sealed class BattleshipInterfaceTests
    {
        IDisposable storageScope;
        string storageRoot,originalLocale,originalMission,originalRoster;
        bool originalSuspended,originalContext,originalRangeReturn,originalSettingsReturn,originalGroupsReturn,originalLibraryReturn,originalCursorVisible,hadRoster,hadMode;
        int originalMode;float originalTimeScale;CursorLockMode originalCursorLock;
        AppPage originalReturnPage,originalEditorResume;MissionDifficulty originalDifficulty;GameSettingsData originalSettings;
        BlueprintData reference;ShipEditorController editor;ShipEditorUI ui;
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();

        [UnitySetUp]public IEnumerator SetUp()
        {
            originalSuspended=GameInput.Suspended;originalContext=AppNavigation.HasAppContext;
            originalReturnPage=AppNavigation.ReturnPage;originalEditorResume=AppNavigation.EditorResumePage;
            originalMission=AppNavigation.SelectedMissionId;originalDifficulty=AppNavigation.Difficulty;
            originalRangeReturn=AppNavigation.RangeReturnToEditor;originalSettingsReturn=AppNavigation.SettingsReturnToEditor;originalGroupsReturn=AppNavigation.WeaponGroupsReturnToEditor;
            originalLibraryReturn=AppNavigation.BlueprintsReturnToEditor;
            originalTimeScale=Time.timeScale;originalCursorLock=Cursor.lockState;originalCursorVisible=Cursor.visible;
            hadRoster=PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);originalRoster=PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            hadMode=PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey);originalMode=PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);
            originalSettings=GameSettings.Current.Clone();yield return L10n.EnsureReady();originalLocale=LocalizationSettings.SelectedLocale?.Identifier.Code;
            storageRoot=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeBattleshipInterfaceTests",Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(storageRoot);storageScope=ShipWorkspace.UseStorageRoot(storageRoot);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,"{\"entries\":[]}");PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey,(int)RangeBotMode.Stationary);
            AppNavigation.ReturnPage=AppPage.RangeSetup;AppNavigation.SelectedMissionId=null;AppNavigation.Difficulty=MissionDifficulty.Standard;
            AppNavigation.RangeReturnToEditor=AppNavigation.SettingsReturnToEditor=AppNavigation.WeaponGroupsReturnToEditor=false;
            AppNavigation.BlueprintsReturnToEditor=false;
            Time.timeScale=1;
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Documentation","Battleship-2026-11-02","Blueprints","Battleship-Maneuver.json"));
            Assert.That(File.Exists(path),Is.True,"Generate the actual battleship reference ships before running interface acceptance.");
            reference=BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(File.ReadAllText(path)));
            Assert.That(reference.parts.Count,Is.InRange(300,600),"Exercise the actual large assembly, not a small substitute fixture.");
            ShipWorkspace.Set(reference,true);
        }

        [UnityTearDown]public IEnumerator TearDown()
        {
            // Finish editor auto-save/unsubscribe callbacks before releasing the isolated workspace.
            var loaded=SceneManager.GetActiveScene();var cleanup=SceneManager.CreateScene("BattleshipInterfaceCleanup_"+Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(cleanup);
            if(loaded.IsValid()&&loaded.isLoaded&&!string.IsNullOrEmpty(loaded.path))yield return SceneManager.UnloadSceneAsync(loaded);
            yield return null;
            foreach(var value in owned)if(value)UnityEngine.Object.Destroy(value);owned.Clear();
            FlightSession.Restored();
            if(hadRoster)PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,originalRoster);else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            if(hadMode)PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey,originalMode);else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);
            PlayerPrefs.Save();
            if(originalSettings!=null){GameSettings.SetCurrent(originalSettings);GameSettings.Apply();}
            if(!string.IsNullOrEmpty(originalLocale))L10n.SelectLocale(originalLocale);
            AppNavigation.ReturnPage=originalReturnPage;AppNavigation.EditorResumePage=originalEditorResume;
            AppNavigation.SelectedMissionId=originalMission;AppNavigation.Difficulty=originalDifficulty;AppNavigation.HasAppContext=originalContext;
            AppNavigation.RangeReturnToEditor=originalRangeReturn;AppNavigation.SettingsReturnToEditor=originalSettingsReturn;AppNavigation.WeaponGroupsReturnToEditor=originalGroupsReturn;
            AppNavigation.BlueprintsReturnToEditor=originalLibraryReturn;
            GameInput.Suspended=originalSuspended;Time.timeScale=originalTimeScale;Cursor.lockState=originalCursorLock;Cursor.visible=originalCursorVisible;
            storageScope?.Dispose();storageScope=null;BlockModules.Configure(null);
            if(!string.IsNullOrEmpty(storageRoot)&&Directory.Exists(storageRoot)){
                string allowed=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeBattleshipInterfaceTests"))+Path.DirectorySeparatorChar;
                string resolved=Path.GetFullPath(storageRoot);
                Assert.That(resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase),Is.True,"Only this isolated test directory may be deleted.");
                Directory.Delete(resolved,true);
            }
        }

        [UnityTest]public IEnumerator RealReferenceHandlingIsBilingualFitsAndRefreshesAfterThrusterRemoval()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(FlightSession.EditorScene),Is.True);
            // This entire flow loads construction once; no flight-range or terrain loading is needed.
            AppNavigation.OpenEditor();
            yield return Await(()=>{
                editor=UnityEngine.Object.FindFirstObjectByType<ShipEditorController>();ui=UnityEngine.Object.FindFirstObjectByType<ShipEditorUI>();
                return editor&&ui&&editor.Assembly&&editor.Assembly.Count==reference.parts.Count&&Label("ModuleStatistics")&&editor.GetComponent<AssemblyAppNavigation>();
            },"The reference battleship did not become ready in the real assembly scene.");
            Assert.That(editor.Assembly.Count,Is.EqualTo(reference.parts.Count));
            float initialCost=AssemblyCost();
            Assert.That(initialCost,Is.GreaterThan(3000).And.LessThanOrEqualTo(3500),"The actual installed reference fits the new 3500 guide.");
            Assert.That(ui.Database.Get(BlockShape.Cube).cost,Is.Zero);

            Click("ShowShipPerformance");
            foreach(string locale in new[]{"en","zh-Hans"}){
                L10n.SelectLocale(locale);
                yield return Await(()=>L10n.IsEnglish==(locale=="en")&&Label("ModuleName")&&Label("ModuleName").text==L10n.T("editor.performance.title")&&
                    Label("ModuleStatistics")&&Label("ModuleStatistics").text.Contains(locale=="en"?"Speed cap":"统一限速"),"Handling did not refresh to the selected locale.");
                yield return null;Canvas.ForceUpdateCanvases();
                yield return SaveHandlingScreenshot(locale);
                string text=Label("ModuleStatistics").text;
                Assert.That(text,Does.Not.Contain("editor.performance."),"All handling keys must be installed in the native locale table.");
                Assert.That(text.Split('\n').Length,Is.EqualTo(8),"Keep the compact shared-speed, six-axis acceleration/braking and three-axis turning summary.");
                Assert.That(text,Does.Contain(locale=="en"?"Speed cap":"统一限速"));
                Assert.That(text.Split('\n')[0],Does.Contain("×"),"Boost is a performance multiplier, not another maximum speed.");
                Assert.That(Label("AssemblyCost").text,Is.EqualTo(L10n.T("editor.assemblyCost",initialCost,10000f)));
                AssertFits("ShowModuleInfoText");AssertFits("ShowShipPerformanceText");
                AssertFits("ModuleName");AssertFits("ModuleStatistics");AssertFits("AssemblyCost");
            }

            string before=Label("ModuleStatistics").text;int originalCount=editor.Assembly.Count;
            var snapshot=editor.Assembly.Export("removal connectivity check");
            var engine=editor.Assembly.Blocks.Where(block=>BlockModules.IsThruster(block.Record.shape))
                .OrderByDescending(block=>ui.Database.Get(block.Record.shape).thrustNewtons)
                .First(block=>BlockConnectivity.IsConnected(snapshot,block.Record.id));
            float engineCost=ui.Database.Get(engine.Record.shape).cost;
            Assert.That(editor.Assembly.Remove(engine.Record.id),Is.True);
            yield return null;Canvas.ForceUpdateCanvases();
            Assert.That(editor.Assembly.Count,Is.EqualTo(originalCount-1));
            Assert.That(Label("ModuleName").text,Is.EqualTo(L10n.T("editor.performance.title")),"Editing should update the selected handling pane without switching it away.");
            Assert.That(Label("ModuleStatistics").text,Is.Not.EqualTo(before),"Removing a real high-thrust module must refresh its displayed maneuver forecast.");
            Assert.That(AssemblyCost(),Is.EqualTo(initialCost-engineCost).Within(.001f));
            Assert.That(Label("AssemblyCost").text,Is.EqualTo(L10n.T("editor.assemblyCost",initialCost-engineCost,10000f)));
            AssertFits("ModuleStatistics");Assert.That(editor.Dirty,Is.True);

            // Exercise the real catalogue button, which must leave the handling tab and show the selected module.
            Click("CubeSlot");yield return null;
            Assert.That(editor.SelectedShape,Is.EqualTo(BlockShape.Cube));
            Assert.That(Label("ModuleName").text,Is.EqualTo(L10n.T("module.Cube")));
            Assert.That(Label("ModuleStatistics").text,Does.Contain(L10n.T("module.stat.freeHull")));
            Assert.That(Label("ModuleStatistics").text,Does.Not.Contain("module.stat.freeHull"));
            AssertFits("ModuleStatistics");
            L10n.SelectLocale("en");
            // Wait for the actual localized view; selecting a locale can finish
            // asynchronously, and this test assembly does not reference ResourceManager.
            yield return Await(()=>L10n.IsEnglish&&Label("ModuleStatistics")&&Label("ModuleStatistics").text.Contains("Free hull")&&
                Label("ModuleName")&&Label("ModuleName").text==L10n.T("module.Cube"),"The module pane did not refresh to English after locale initialization.");
            Assert.That(Label("ModuleStatistics").text,Does.Contain("Free hull"));AssertFits("ModuleStatistics");
            Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo(FlightSession.EditorScene));
            Assert.That(UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>(),Is.Null);
        }

        [UnityTest]public IEnumerator AlwaysVisibleShipSummaryMatchesPerformanceScrollsAtLargeTextAndHomeHasNoMissionImage()
        {
            var large=originalSettings.Clone();large.uiScale=1.25f;GameSettings.SetCurrent(large);GameSettings.Apply();
            AppNavigation.OpenEditor();
            yield return Await(()=>{
                editor=UnityEngine.Object.FindFirstObjectByType<ShipEditorController>();ui=UnityEngine.Object.FindFirstObjectByType<ShipEditorUI>();
                return editor&&ui&&editor.Assembly&&editor.Assembly.Count==reference.parts.Count&&Label("ShipSummary")&&editor.GetComponent<AssemblyAppNavigation>();
            },"The real assembly scene must expose its new ship summary by default.");
            var details=ui.GetComponentsInChildren<Transform>(true).Single(value=>value.name=="ShipSummaryDetails");
            Assert.That(details.gameObject.activeInHierarchy,Is.True,"The detailed sheet must start expanded without opening the handling tab.");
            var tuningField=typeof(ShipEditorUI).GetField("flightTuning",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(tuningField,Is.Not.Null);var tuning=(FlightTuning)tuningField.GetValue(ui);
            Assert.That(tuning,Is.Not.Null);Assert.That(AssemblyCost(),Is.LessThanOrEqualTo(3500),"The reference design remains a 3500-cost guide even with a larger editor budget.");
            foreach(string locale in new[]{"en","zh-Hans"}){
                L10n.SelectLocale(locale);
                yield return Await(()=>L10n.IsEnglish==(locale=="en")&&Label("ShipSummary")&&Label("ShipSummary").text.Contains(locale=="en"?"TOTAL HULL":"总耐久"),"The persistent readout did not refresh language.");
                yield return null;Canvas.ForceUpdateCanvases();
                AssertReadoutMatches(tuning);
                AssertFits("ShipSummary");AssertFits("ShipSummaryHeading");AssertFits("ShipSummaryCompact");AssertFits("AssemblyCost");
                Assert.That(Label("AssemblyCost").text,Is.EqualTo(L10n.T("editor.assemblyCost",AssemblyCost(),10000f)));
                var scroll=ui.GetComponentsInChildren<UnityEngine.UI.ScrollRect>().Single(value=>value.name=="ShipSummaryScroll");
                Assert.That(scroll.vertical&&scroll.verticalScrollbar,Is.True,"The summary requires an actual connected scrollbar.");
                Assert.That(scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>(),Is.Not.Null,"Only the scroll viewport should clip the long readout.");
                Assert.That(scroll.content.rect.height,Is.GreaterThan(scroll.viewport.rect.height+1),"At 125% text, long totals must scroll rather than be shrunk or cut.");
                scroll.verticalNormalizedPosition=1;Canvas.ForceUpdateCanvases();float top=scroll.content.anchoredPosition.y;
                ExecuteEvents.Execute<IScrollHandler>(scroll.gameObject,new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-3)},ExecuteEvents.scrollHandler);
                yield return null;Assert.That(scroll.content.anchoredPosition.y,Is.GreaterThan(top+.5f),"The real mouse-wheel handler must move the content.");
                scroll.verticalScrollbar.value=0;Canvas.ForceUpdateCanvases();
                Assert.That(scroll.verticalNormalizedPosition,Is.LessThan(.01f),"Dragging the real scrollbar must reach the bottom.");
                Click("CubeSlot");yield return null;
                Assert.That(details.gameObject.activeInHierarchy,Is.True,"Selecting a component must leave the ship-wide summary visible.");
                Click("ToggleShipSummary");yield return null;
                Assert.That(details.gameObject.activeInHierarchy,Is.False);Assert.That(Label("ShipSummaryCompact").gameObject.activeInHierarchy,Is.True,"Collapsing keeps the compact totals visible.");
                Click("ToggleShipSummary");yield return null;Assert.That(details.gameObject.activeInHierarchy,Is.True);
            }
            // Use an in-memory tuning copy to deterministically exceed generation. Installed assets
            // and balance values are never changed by this presentation acceptance test.
            var peakTuning=UnityEngine.Object.Instantiate(tuning);owned.Add(peakTuning);
            float generation=editor.Assembly.Blocks.Sum(block=>Mathf.Max(0,ui.Database.Get(block.Record.shape).generationWatts));
            peakTuning.boostPowerWatts=generation+100;tuningField.SetValue(ui,peakTuning);ui.UpdateStats(editor.Assembly.Count,editor.Dirty);
            yield return null;AssertReadoutMatches(peakTuning);
            string peak=Label("ShipSummary").text.Split('\n').Single(row=>row.Contains(L10n.IsEnglish?"PEAK DEMAND":"峰值需求"));
            Assert.That(peak,Does.Contain("<color=#"+ColorUtility.ToHtmlStringRGB(UiTheme.Current.amber)+">"),"Peak overload must be highlighted with the shared warning colour.");
            tuningField.SetValue(ui,tuning);ui.UpdateStats(editor.Assembly.Count,editor.Dirty);
            AppNavigation.ReturnPage=AppPage.Home;yield return SceneManager.LoadSceneAsync(AppNavigation.HomeScene);
            GameAppBootstrap app=null;yield return Await(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.CurrentPage==AppPage.Home;},"Hangar must be installed and render Home.");
            Assert.That(app.Surface.Root.GetComponentsInChildren<Transform>(true).Any(value=>value.name=="MissionPreview"),Is.False,"The home mission card must not recreate the removed thumbnail.");
            Assert.That(app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.RawImage>().Count(value=>value.name.StartsWith("Mission_",StringComparison.Ordinal)),Is.EqualTo(1));
            Assert.That(UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>(),Is.Null,"UI acceptance must not start combat or load the range map.");
        }

        void AssertReadoutMatches(FlightTuning tuning)
        {
            var p=ShipPerformance.CalculateAssembly(editor.Assembly,tuning);var summary=Label("ShipSummary");string raw=summary.text;
            var rows=Regex.Replace(raw,"<[^>]+>","").Split('\n');bool en=L10n.IsEnglish;
            string Row(string english,string chinese)=>rows.Single(line=>line.StartsWith(en?english:chinese,StringComparison.Ordinal));
            string mass=Row("MASS ","质量 ");Assert.That(mass,Does.Contain((p.totalMass/1000).ToString("0.0")+" t"));Assert.That(mass,Does.Contain(p.totalHp.ToString("N0")+" HP"));
            float generation=0,capacity=0,sustained=0,shield=0;
            foreach(var block in editor.Assembly.Blocks){var module=tuning.GetDefinition(block.Record.shape);generation+=Mathf.Max(0,module.generationWatts);capacity+=Mathf.Max(0,module.storageJoules);if(module.weapon)sustained+=Mathf.Max(0,module.weapon.sustainedPowerWatts);if(module.shieldCapacity>0&&module.shieldRecoveryPerSecond>0)shield+=Mathf.Max(0,module.shieldPowerWatts);}
            string power=Row("OUTPUT ","供电 ");Assert.That(power,Does.Contain(generation.ToString("N0")+" W"));Assert.That(power,Does.Contain(capacity.ToString("N0")+" J"));
            Assert.That(Row("SUSTAINED ","持续需求 "),Does.Contain(sustained.ToString("N0")+" W"));
            float peak=sustained+shield+Mathf.Max(0,tuning.boostPowerWatts);Assert.That(Row("PEAK DEMAND ","峰值需求 "),Does.Contain(peak.ToString("N0")+" W"));
            string peakRow=raw.Split('\n').Single(line=>line.Contains(en?"PEAK DEMAND":"峰值需求"));
            Assert.That(peakRow.Contains("<color=#"+ColorUtility.ToHtmlStringRGB(UiTheme.Current.amber)+">"),Is.EqualTo(peak>generation+.01f),"The overload warning must agree with actual installed power supply and peak consumers.");
            Assert.That(Row("SPEED CAP ","统一限速 "),Does.Contain(p.maxSpeed.ToString("0")+" m/s"));
            var plus=p.accelerationPositive/9.80665f;var minus=p.accelerationNegative/9.80665f;
            Assert.That(Row("FWD ","前 "),Does.Contain(plus.z.ToString("0.00")+" / "+minus.z.ToString("0.00")));Assert.That(Row("FWD ","前 "),Does.Contain(minus.z.ToString("0.00")+" / "+plus.z.ToString("0.00")));
            Assert.That(Row("LEFT ","左 "),Does.Contain(minus.x.ToString("0.00")+" / "+plus.x.ToString("0.00")));Assert.That(Row("LEFT ","左 "),Does.Contain(plus.x.ToString("0.00")+" / "+minus.x.ToString("0.00")));
            Assert.That(Row("UP ","上 "),Does.Contain(plus.y.ToString("0.00")+" / "+minus.y.ToString("0.00")));Assert.That(Row("UP ","上 "),Does.Contain(minus.y.ToString("0.00")+" / "+plus.y.ToString("0.00")));
            foreach(var axis in new[]{(en:"PITCH ",zh:"俯仰 ",index:0),(en:"YAW ",zh:"偏航 ",index:1),(en:"ROLL ",zh:"滚转 ",index:2)}){
                string line=Row(axis.en,axis.zh);Assert.That(line,Does.Contain(Mathf.Min(p.angularRatePositive[axis.index],p.angularRateNegative[axis.index]).ToString("0.0")+" °/s"));Assert.That(line,Does.Contain(Mathf.Min(p.angularAccelerationPositive[axis.index],p.angularAccelerationNegative[axis.index]).ToString("0.0")+" °/s²"));
            }
        }

        static IEnumerator SaveHandlingScreenshot(string locale)
        {
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Documentation","Battleship-2026-11-02","Interface"));
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"handling-"+locale+".png");
            if(File.Exists(path))File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            // CaptureScreenshot completes on the rendered frame; polling avoids
            // WaitForEndOfFrame's Game-view-focus dependency in Editor tests.
            yield return Await(()=>File.Exists(path)&&new FileInfo(path).Length>0,"Handling screenshot was not saved: "+path,10);
        }
        float AssemblyCost()=>editor.Assembly.Blocks.Sum(block=>ui.Database.Get(block.Record.shape).cost);
        TextMeshProUGUI Label(string name)=>ui?ui.GetComponentsInChildren<TextMeshProUGUI>().FirstOrDefault(label=>label.name==name):null;
        void Click(string name)
        {
            var button=ui.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(value=>value.name==name);
            Assert.That(button,Is.Not.Null,"Missing assembly action: "+name);Assert.That(button.IsInteractable(),Is.True);button.onClick.Invoke();
        }
        void AssertFits(string name)
        {
            var label=Label(name);Assert.That(label,Is.Not.Null,name);label.ForceMeshUpdate(true,true);
            Assert.That(label.rectTransform.rect.width,Is.GreaterThan(0),name);
            Assert.That(label.rectTransform.rect.height,Is.GreaterThan(0),name);
            Assert.That(label.isTextOverflowing,Is.False,name+" overflows its actual rendered rectangle.");
            Assert.That(label.isTextTruncated,Is.False,name+" must not hide content behind ellipsis.");
            var needed=label.GetPreferredValues(label.text,label.rectTransform.rect.width,Mathf.Infinity);
            Assert.That(needed.y,Is.LessThanOrEqualTo(label.rectTransform.rect.height+1),name+" needs more vertical room.");
        }
        static IEnumerator Await(Func<bool> condition,string message,float seconds=30)
        {
            float deadline=Time.realtimeSinceStartup+seconds;
            while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(condition(),Is.True,message);
        }
    }
}
