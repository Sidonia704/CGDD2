using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.App;
using Starframe.Presentation;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Starframe.Tests
{
    public sealed class AssemblyPreviewUiTests
    {
        System.IDisposable storageScope;string storageRoot,savedLocale;bool savedContext,savedSuspended,savedCursorVisible;float savedScale;CursorLockMode savedCursorLock;
        ModuleDatabase savedDatabase;GameSettingsData savedSettings;BlueprintData savedDraft;bool savedDirty,savedReturning;UnityEngine.Random.State savedRandom;
        [UnitySetUp]public IEnumerator SetUp()
        {
            savedContext=AppNavigation.HasAppContext;savedSuspended=GameInput.Suspended;savedScale=Time.timeScale;savedCursorLock=Cursor.lockState;savedCursorVisible=Cursor.visible;savedDatabase=BlockModules.ConfiguredDatabase;savedRandom=UnityEngine.Random.state;
            savedSettings=GameSettings.Current.Clone();yield return L10n.EnsureReady();savedLocale=L10n.IsEnglish?"en":"zh-Hans";
            savedDraft=FlightSession.EditorDraft;savedDirty=FlightSession.EditorDirty;savedReturning=FlightSession.ReturningToEditor;
            storageRoot=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeAssemblyUiTests",System.Guid.NewGuid().ToString("N")));Directory.CreateDirectory(storageRoot);
            storageScope=ShipWorkspace.UseStorageRoot(storageRoot);AppNavigation.HasAppContext=true;FlightSession.Begin(FlightRangeBootstrap.DefaultShip(),false);FlightSession.Restored();Time.timeScale=1;
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            // Complete the last language transaction before unloading labels which own table requests.
            yield return L10n.EnsureReady();yield return null;
            // Dispose the assembly navigation before releasing its isolated storage scope.
            var cleanup=SceneManager.CreateScene("AssemblyUiCleanup_"+System.Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(cleanup);
            var scenes=new System.Collections.Generic.List<Scene>();for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene!=cleanup&&scene.path.StartsWith("Assets/_Starframe/Scenes/",System.StringComparison.OrdinalIgnoreCase))scenes.Add(scene);}
            foreach(var scene in scenes)if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
            if(savedSettings!=null){
                // The selected preview language and persisted preference may differ. Apply once
                // using the language to restore, wait for preloading, then restore the exact data
                // without applying a second competing locale transition.
                var restoration=savedSettings.Clone();if(!string.IsNullOrEmpty(savedLocale))restoration.localeCode=savedLocale;
                GameSettings.SetCurrent(restoration);GameSettings.Apply();yield return L10n.EnsureReady();
                GameSettings.SetCurrent(savedSettings);
            }else if(!string.IsNullOrEmpty(savedLocale)){L10n.SelectLocale(savedLocale);yield return L10n.EnsureReady();}
            yield return null;yield return null;
            typeof(FlightSession).GetProperty(nameof(FlightSession.EditorDraft),BindingFlags.Public|BindingFlags.Static).SetValue(null,savedDraft);
            typeof(FlightSession).GetProperty(nameof(FlightSession.EditorDirty),BindingFlags.Public|BindingFlags.Static).SetValue(null,savedDirty);
            typeof(FlightSession).GetProperty(nameof(FlightSession.ReturningToEditor),BindingFlags.Public|BindingFlags.Static).SetValue(null,savedReturning);
            storageScope?.Dispose();storageScope=null;AppNavigation.HasAppContext=savedContext;GameInput.Suspended=savedSuspended;Time.timeScale=savedScale;Cursor.lockState=savedCursorLock;Cursor.visible=savedCursorVisible;BlockModules.Configure(savedDatabase);UnityEngine.Random.state=savedRandom;
            if(!string.IsNullOrEmpty(storageRoot)&&Directory.Exists(storageRoot)){
                string allowed=Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"StarframeAssemblyUiTests"))+Path.DirectorySeparatorChar;
                Assert.That(Path.GetFullPath(storageRoot).StartsWith(allowed,System.StringComparison.OrdinalIgnoreCase),Is.True);Directory.Delete(storageRoot,true);
            }
        }
        static IEnumerator SelectLocaleReady(string locale)
        {
            // Localization resets database preload handles on every actual locale change. A
            // second change must wait for the first initialization rather than merely one frame.
            yield return L10n.EnsureReady();L10n.SelectLocale(locale);yield return L10n.EnsureReady();yield return null;
        }
        [UnityTest]public IEnumerator LibraryWheelAndScrollbarWorkAndShieldsStayNonPhysical()
        {
            yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);yield return null;
            var editor=Object.FindFirstObjectByType<ShipEditorController>();
            var blueprint=FlightRangeBootstrap.DefaultShip();blueprint.parts.Add(new BlockRecord{id="ui-shield",shape=BlockShape.Shield,position=new Vector3(-1,1.5f,0),rotation=Quaternion.identity});
            editor.Assembly.Import(blueprint);editor.SelectShape(BlockShape.Shield);
            yield return null;Canvas.ForceUpdateCanvases();
            var scroll=GameObject.Find("ModuleLibraryScroll").GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.That(GameObject.Find("Thruster6Slot"),Is.Not.Null,"The five-way thruster must occupy its own library slot.");
            editor.SelectShape(BlockShape.Thruster6);yield return null;
            var clearance=(Transform)typeof(ShipEditorController).GetField("clearancePreview",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
            Assert.That(clearance,Is.Not.Null);Assert.That(clearance.childCount,Is.EqualTo(5));
            foreach(Transform cell in clearance)Assert.That(cell.localScale.x,Is.EqualTo(.5f).Within(.001f),"The five-way clearance preview must be half a cell.");
            editor.SelectShape(BlockShape.Shield);yield return null;
            Assert.That(scroll.content.rect.height,Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.viewport.GetComponent<UnityEngine.UI.Mask>(),Is.Not.Null);
            scroll.verticalNormalizedPosition=1;scroll.StopMovement();Canvas.ForceUpdateCanvases();
            float top=scroll.content.anchoredPosition.y;
            var wheel=new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-6)};
            ExecuteEvents.ExecuteHierarchy(GameObject.Find("CubeIcon"),wheel,ExecuteEvents.scrollHandler);
            yield return null;Canvas.ForceUpdateCanvases();
            Assert.That(scroll.content.anchoredPosition.y,Is.GreaterThan(top+1));
            scroll.verticalScrollbar.value=0;yield return null;Canvas.ForceUpdateCanvases();
            Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(0).Within(.001f));
            var corners=new Vector3[4];GameObject.Find("Armor3Slot").GetComponent<RectTransform>().GetWorldCorners(corners);
            foreach(var corner in corners)Assert.That(scroll.viewport.rect.Contains(scroll.viewport.InverseTransformPoint(corner)),Is.True,"The last category must be reachable without expanding the panel.");
            string originalLocale=Starframe.Presentation.L10n.IsEnglish?"en":"zh-Hans";
            yield return SelectLocaleReady("zh-Hans");editor.SelectShape(BlockShape.Gun3);yield return null;
            var statisticText=GameObject.Find("ModuleStatistics").GetComponent("TextMeshProUGUI");
            var statisticFont=(UnityEngine.Object)statisticText.GetType().GetProperty("font").GetValue(statisticText);
            Assert.That(statisticFont.name,Is.EqualTo("UI Chinese"),"Dynamic new-module statistics must use the Chinese font after switching locale.");
            if(!string.IsNullOrEmpty(originalLocale))yield return SelectLocaleReady(originalLocale);editor.SelectShape(BlockShape.Shield);yield return null;
            var preview=Object.FindFirstObjectByType<AssemblyShieldPreview>();Assert.That(preview.SurfaceCount,Is.EqualTo(1));
            var surface=GameObject.Find("AssemblyShieldRange");Assert.That(surface.GetComponent<Collider>(),Is.Null);
            Assert.That(surface.GetComponent<MeshRenderer>().bounds.size.x,Is.EqualTo(16).Within(.01f));
            var placement=(MeshRenderer)typeof(ShipEditorController).GetField("shieldPreview",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
            Assert.That(placement,Is.Not.Null);Assert.That(placement.transform.localScale.x,Is.EqualTo(16));Assert.That(placement.gameObject.layer,Is.EqualTo(2));
            Assert.That(editor.Assembly.Remove("ui-shield"),Is.True);Assert.That(preview.SurfaceCount,Is.Zero);
            yield return null;
        }
    }
}
