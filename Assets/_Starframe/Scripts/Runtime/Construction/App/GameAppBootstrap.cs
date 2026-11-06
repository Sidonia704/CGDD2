// @role Initialize the hangar, selected ship and application screens.
// @calls ShipWorkspace, MenuSurface, HangarShipPreview, GameAppScreens, GameResultsScreen

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;
using Starframe.Presentation;

namespace Starframe.App
{
    public sealed partial class GameAppBootstrap:MonoBehaviour
    {
        public ShipAssembly assemblyPrefab;
        public FlightTuning tuning;
        public MissionCatalog catalog;
        public Camera previewCamera;
        MenuSurface ui;HangarShipPreview preview;
        AppPage page,settingsReturn,groupsReturn,libraryReturn;MissionDefinition mission;BlueprintData selected;
        WeaponGroupScreen weaponScreen;SettingsScreen settingsScreen;BlueprintLibraryScreen libraryScreen;
        bool ready,dialogOpen;Action dialogCancel;string notice="";
        public AppPage CurrentPage=>page;
        public bool IsReady=>ready;
        public MenuSurface Surface=>ui;
        public HangarShipPreview Preview=>preview;
        public DeploymentReport CurrentDeployment{get;private set;}
        IEnumerator Start()
        {
            var preferences=GameSettings.Current;yield return L10n.EnsureReady();GameSettings.Apply();
            if(!previewCamera)previewCamera=Camera.main;
            ui=gameObject.AddComponent<MenuSurface>();ui.Initialize(previewCamera);
            ui.OperationFailed+=error=>{if(page==AppPage.WeaponGroups&&weaponScreen){weaponScreen.ReportSaveFailure();return;}ConfirmDialog(T("app.errorTitle"),T("app.storageError"),T("app.ok"),()=>ShowPage(page),()=>ShowPage(page));};
            preview=gameObject.AddComponent<HangarShipPreview>();preview.Initialize(assemblyPrefab,tuning,previewCamera);
            var grid=GetComponent<FigmaHangarGrid>();if(!grid)grid=gameObject.AddComponent<FigmaHangarGrid>();grid.Initialize(preview);
            BlockModules.Configure(tuning.database);
            selected=ShipWorkspace.Ship;preview.Show(selected);mission=catalog?catalog.Find(AppNavigation.SelectedMissionId):null;
            if(!mission&&catalog&&catalog.missions.Count>0)mission=catalog.missions[0];
            L10n.Changed+=OnLocaleChanged;ready=true;AppNavigation.HasAppContext=true;ShowPage(AppNavigation.ReturnPage);
        }
        void OnLocaleChanged(){if(ready&&page!=AppPage.Settings&&page!=AppPage.WeaponGroups&&page!=AppPage.BlueprintLibrary&&!dialogOpen)ShowPage(page);}
        void Update()
        {
            if(!ready||UnityEngine.InputSystem.Keyboard.current==null)return;
            if(UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame){
                foreach(var dropdown in ui.Root.GetComponentsInChildren<TMP_Dropdown>())if(dropdown.IsExpanded){dropdown.Hide();return;}
                if(dialogOpen){dialogCancel?.Invoke();return;}
                if(page==AppPage.WeaponGroups||page==AppPage.Settings||page==AppPage.BlueprintLibrary)return;
                if(page==AppPage.Home)ShowQuit();else ShowHome();
            }
        }
        public void ShowPage(AppPage value)
        {
            if(!ready)return;
            switch(value){case AppPage.Deployment:ShowDeployment();break;case AppPage.RangeSetup:ShowRangeSetup();break;case AppPage.WeaponGroups:ShowWeaponGroups();break;case AppPage.Settings:ShowSettings();break;case AppPage.Help:ShowHelp();break;case AppPage.MissionReady:ShowMissionReady();break;case AppPage.Results:ShowResults();break;case AppPage.BlueprintLibrary:ShowBlueprintLibrary();break;default:ShowHome();break;}
        }
        void Begin(AppPage next)
        {
            if(weaponScreen){weaponScreen.enabled=false;Destroy(weaponScreen);weaponScreen=null;}
            if(settingsScreen){settingsScreen.enabled=false;Destroy(settingsScreen);settingsScreen=null;}
            if(libraryScreen){libraryScreen.enabled=false;Destroy(libraryScreen);libraryScreen=null;}
            page=next;dialogOpen=false;dialogCancel=null;ui.Clear();GameInput.Suspended=true;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;preview.AllowOrbit=true;
        }
        void Header(string title,string subtitle,bool back=true)
        {
            ui.Panel("NavigationGlass",48,24,1824,84);
            var brand=ui.Label("Brand","STARFRAME",72,42,350,40,25);brand.characterSpacing=5;
            if(back)ui.Button("Back",T("app.backHangar"),1304,41,240,50,ShowHome);
            ui.Button("Settings",T("app.settings"),1560,41,150,50,ShowSettings);
            var help=ui.Button("Help","?",1726,41,56,50,ShowHelp);ui.Hint(help,T("app.helpTitle"));
            var quit=ui.Button("Quit","×",1798,41,50,50,ShowQuit);ui.Hint(quit,T("app.quit"));
            if(!string.IsNullOrEmpty(title)){
                ui.Label("PageTitle",title,48,132,1100,50,32);
                ui.Label("PageSubtitle",subtitle,50,187,1100,30,15,GlassUiPalette.Muted);
            }else ui.Label("PageSubtitle",subtitle,448,49,820,32,15,GlassUiPalette.Muted);
        }
        static string T(string key,params object[] args)=>L10n.T(key,args);
        public void ShowHome()
        {
            Begin(AppPage.Home);selected=ShipWorkspace.Ship;preview.Show(selected);preview.ResetView();preview.SetFocus(.53f,.64f);preview.FitViewport(.67f,.65f);
            FigmaMenuHeader("",false);
            var title=ui.Label("Brand","STAR FRAME",50,18,1350,200,165,Color.white);FigmaVisuals.SetFont(title,false,true);title.textWrappingMode=TextWrappingModes.NoWrap;
            var stripeClip=MenuSurface.Rect(ui.Root,"BrandAccentClip",58.5f,166,323.25f,73.5f);stripeClip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var stripe=MenuSurface.Rect(ui.Root,"BrandAccent",0,0,1,1).gameObject.AddComponent<UnityEngine.UI.RawImage>();stripe.texture=Resources.Load<Texture2D>("UI/Figma/MainStripe");stripe.color=GlassUiPalette.Accent;stripe.raycastTarget=false;
            stripe.rectTransform.SetParent(stripeClip,false);stripe.rectTransform.anchorMin=stripe.rectTransform.anchorMax=new Vector2(0,1);stripe.rectTransform.pivot=new Vector2(.5f,.5f);stripe.rectTransform.sizeDelta=new Vector2(115.676f,616.961f);stripe.rectTransform.anchoredPosition=new Vector2(174.980f,-55.412f);stripe.rectTransform.localRotation=Quaternion.Euler(0,0,90);
            var stripeShader=Shader.Find("Starframe/UI/FigmaAlphaMask");if(stripeShader){stripe.material=new Material(stripeShader);stripe.gameObject.AddComponent<MenuIconMaterialOwner>().value=stripe.material;}
            ui.Panel("ShipIdentityGlass",50,514,461,319);
            ui.Label("ShipEyebrow",MenuCopy("当前舰船：","CURRENT SHIP:"),78,543,420,34,22,Color.white);
            var name=ui.Label("CurrentShipName",selected.name,78,588,420,74,40,GlassUiPalette.Accent);FigmaVisuals.SetFont(name,true);L10n.ApplyUserContentFont(name);name.textWrappingMode=TextWrappingModes.NoWrap;
            MenuShipMetrics.Summary(ui,"HomeStat",MenuShipMetrics.Calculate(selected,tuning,mission),86,687,411,22);
            MenuButton("EditShip",MenuCopy("编辑舰船","EDIT SHIP"),50,857,224,56,OpenEditor,24);
            MenuButton("ChooseShip",MenuCopy("蓝图库","BLUEPRINTS"),288,857,223,56,ShowShipPicker,24);
            MenuButton("RangeSetup",MenuCopy("试飞","TEST FLIGHT"),50,929,461,56,()=>{AppNavigation.RangeReturnToEditor=false;ShowRangeSetup();},24);
            var play=MenuButton("Play",MenuCopy("开始","PLAY"),1546,955,320,82,()=>{if(!mission&&catalog)mission=catalog.missions.FirstOrDefault(value=>value);ShowDeployment();},32,true);
            play.interactable=mission||catalog&&catalog.missions.Any(value=>value);
            if(notice.Length>0){ui.Label("Notice",notice,563,1010,800,42,18,GlassUiPalette.Accent);notice="";}
        }
        void MissionCard(MissionDefinition definition,float x,float y)
        {
            ui.Panel("Mission_"+definition.id,x,y,1824,266);
            ui.Label("MissionRegion",T(definition.regionKey),x+32,y+30,1350,24,14,GlassUiPalette.Muted);
            ui.Label("MissionTitle",T(definition.titleKey),x+32,y+73,1350,48,30);
            ui.Label("MissionDescription",T(definition.descriptionKey),x+32,y+141,1350,74,18,GlassUiPalette.Muted);
            ui.Label("MissionBudget",T("app.budgetFrom",definition.baseBudget.ToString("N0")),x+1478,y+76,310,30,18);
            if(!HasOperationScene(definition))ui.Label("MissionPending_"+definition.id,T("app.ui.missionPending"),x+1478,y+34,310,24,13,UiTheme.Current.amber);
            ui.Button("Prepare_"+definition.id,T("app.prepare"),x+1478,y+174,310,60,()=>SelectMission(definition.id),true);
        }
        static bool HasOperationScene(MissionDefinition definition)=>definition&&!string.IsNullOrWhiteSpace(definition.sceneName)&&Application.CanStreamedLevelBeLoaded(definition.sceneName);
        public void SelectMission(string id)
        {
            mission=catalog.Find(id);AppNavigation.SelectedMissionId=id;ShowDeployment();
        }
        void UserName(string name,string text,float x,float y,float w,float h,float size)
        {
            var label=ui.Label(name,text,x,y,w,h,size);L10n.ApplyUserContentFont(label);
        }
        (float mass,float cost,float power,float shield) Summarize(BlueprintData blueprint)
        {
            float mass=0,cost=0,power=0,shield=0;
            foreach(var p in blueprint.parts){var d=tuning.database.Get(p.shape);if(!d)continue;mass+=d.massKg;cost+=mission?mission.CostFor(d):d.cost;power+=d.storageJoules;shield+=d.shieldCapacity;}return(mass,cost,power,shield);
        }
        public void OpenEditor()
        {
            AppNavigation.ReturnPage=page==AppPage.Deployment||page==AppPage.RangeSetup?page:AppPage.Home;AppNavigation.OpenEditor();
        }
        public void ShowSettings()
        {
            settingsReturn=page==AppPage.Settings?AppPage.Home:page;Begin(AppPage.Settings);preview.SetFocus(.66f,.7f);
            settingsScreen=gameObject.AddComponent<SettingsScreen>();settingsScreen.Initialize(ui,()=>{if(AppNavigation.SettingsReturnToEditor){AppNavigation.SettingsReturnToEditor=false;AppNavigation.ReturnPage=AppNavigation.EditorResumePage;AppNavigation.OpenEditor();}else ShowPage(settingsReturn);});
        }
        public void ShowWeaponGroups()
        {
            groupsReturn=page==AppPage.WeaponGroups?AppPage.Home:page;
            Begin(AppPage.WeaponGroups);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.31f,.38f);preview.ResetView();preview.FitViewport(.60f,.34f);
            weaponScreen=gameObject.AddComponent<WeaponGroupScreen>();weaponScreen.Initialize(ui,preview.Assembly,previewCamera,selected,updated=>{
                ShipWorkspace.Set(updated,true,ShipWorkspace.SourceFile);notice=T("app.groupsApplied");CloseWeaponGroups();
            },CloseWeaponGroups);
        }
        void CloseWeaponGroups()
        {
            if(AppNavigation.WeaponGroupsReturnToEditor){AppNavigation.WeaponGroupsReturnToEditor=false;AppNavigation.ReturnPage=AppNavigation.EditorResumePage;AppNavigation.OpenEditor();}
            else ShowPage(groupsReturn);
        }
        void ShowQuit()
        {
            ConfirmDialog(T("app.quitTitle"),T("app.quitBody"),T("app.quit"),()=>{
                ShipWorkspace.Persist();GameSettings.Save();
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying=false;
                #else
                Application.Quit();
                #endif
            },()=>ShowPage(page));
        }
        public void ConfirmDialog(string title,string body,string yes,Action action,Action cancel)
        {
            // A picker can open a second confirmation. Remove the old modal first so its controls
            // cannot receive clicks through the replacement or accumulate in the keyboard order.
            if(dialogOpen){
                foreach(string name in new[]{"PickerBlocker","ShipPickerGlass","PickerTitle","PickerSubtitle","ShipPickerSearch","ShipPickerCount","ShipPickerScroll","NoBlueprints","NoSearchResults","ClosePicker"}){
                    var existing=ui.Root.Find(name);if(existing){existing.gameObject.SetActive(false);Destroy(existing.gameObject);}
                }
            }
            dialogOpen=true;dialogCancel=cancel;preview.AllowOrbit=false;
            var blocker=MenuSurface.Rect(ui.Root,"DialogBlocker",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.5f);
            ui.Panel("ConfirmationGlass",540,310,840,460,true);ui.Label("ConfirmationTitle",title,584,350,752,68,28);
            ui.Label("ConfirmationBody",body,584,438,752,184,18,GlassUiPalette.Muted);
            ui.Button("ConfirmationCancel",T("app.cancel"),584,674,360,54,cancel);
            ui.Button("ConfirmationAccept",yes,960,674,376,54,action,true);
            UiModalScope.Protect(ui.Root,blocker.transform);
        }
        void OnDestroy(){L10n.Changed-=OnLocaleChanged;}
    }
}
