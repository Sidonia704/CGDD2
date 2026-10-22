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
        AppPage page,settingsReturn,groupsReturn;MissionDefinition mission;BlueprintData selected;
        WeaponGroupScreen weaponScreen;SettingsScreen settingsScreen;
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
            BlockModules.Configure(tuning.database);
            selected=ShipWorkspace.Ship;preview.Show(selected);mission=catalog?catalog.Find(AppNavigation.SelectedMissionId):null;
            if(!mission&&catalog&&catalog.missions.Count>0)mission=catalog.missions[0];
            L10n.Changed+=OnLocaleChanged;ready=true;AppNavigation.HasAppContext=true;ShowPage(AppNavigation.ReturnPage);
        }
        void OnLocaleChanged(){if(ready&&page!=AppPage.Settings&&page!=AppPage.WeaponGroups&&!dialogOpen)ShowPage(page);}
        void Update()
        {
            if(!ready||UnityEngine.InputSystem.Keyboard.current==null)return;
            if(UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame){
                foreach(var dropdown in ui.Root.GetComponentsInChildren<TMP_Dropdown>())if(dropdown.IsExpanded){dropdown.Hide();return;}
                if(dialogOpen){dialogCancel?.Invoke();return;}
                if(page==AppPage.WeaponGroups||page==AppPage.Settings)return;
                if(page==AppPage.Home)ShowQuit();else ShowHome();
            }
        }
        public void ShowPage(AppPage value)
        {
            if(!ready)return;
            switch(value){case AppPage.Deployment:ShowDeployment();break;case AppPage.RangeSetup:ShowRangeSetup();break;case AppPage.WeaponGroups:ShowWeaponGroups();break;case AppPage.Settings:ShowSettings();break;case AppPage.Help:ShowHelp();break;case AppPage.MissionReady:ShowMissionReady();break;case AppPage.Results:ShowResults();break;default:ShowHome();break;}
        }
        void Begin(AppPage next)
        {
            if(weaponScreen){weaponScreen.enabled=false;Destroy(weaponScreen);weaponScreen=null;}
            if(settingsScreen){settingsScreen.enabled=false;Destroy(settingsScreen);settingsScreen=null;}
            page=next;dialogOpen=false;dialogCancel=null;ui.Clear();GameInput.Suspended=true;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;preview.AllowOrbit=true;
        }
        void Header(string title,string subtitle,bool back=true)
        {
            ui.Label("Brand","STARFRAME",48,28,350,42,28);ui.Label("PageSubtitle",subtitle,50,77,900,24,13,GlassUiPalette.Muted);
            if(back)ui.Button("Back",T("app.backHangar"),1330,30,220,48,ShowHome);
            ui.Button("Settings",T("app.settings"),1570,30,150,48,ShowSettings);
            ui.Button("Help","?",1736,30,62,48,ShowHelp);
            ui.Button("Quit","×",1814,30,58,48,ShowQuit);
            if(!string.IsNullOrEmpty(title))ui.Label("PageTitle",title,48,122,1100,58,32);
        }
        static string T(string key,params object[] args)=>L10n.T(key,args);
        public void ShowHome()
        {
            Begin(AppPage.Home);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.70f,.61f);
            Header("",T("app.homeSubtitle"),false);
            ui.Panel("ShipIdentityGlass",48,132,438,432);
            ui.Label("ShipEyebrow",T("app.currentShip"),76,158,380,24,13,GlassUiPalette.Accent);
            UserName("CurrentShipName",selected.name,76,196,380,74,30);
            var stats=Summarize(selected);
            ui.Label("ShipPartCount",T("app.parts",selected.parts.Count),76,291,176,30,17);
            ui.Label("ShipMass",T("app.mass",stats.mass.ToString("N0")),270,291,188,30,17);
            ui.Label("ShipCost",T("app.cost",stats.cost.ToString("N0")),76,333,370,28,16,GlassUiPalette.Muted);
            ui.Label("ShipWorkspace",ShipWorkspace.Dirty?T("app.workspaceDraft"):T("app.workspaceSaved"),76,373,370,44,13,GlassUiPalette.Muted);
            ui.Button("EditShip",T("app.editShip"),76,443,382,54,OpenEditor,true);
            ui.Button("ChooseShip",T("app.changeShip"),76,509,183,36,ShowShipPicker);
            ui.Button("WeaponGroups",T("app.weaponGroups"),271,509,187,36,ShowWeaponGroups);
            ui.Label("OrbitHint",T("app.orbitHint"),670,555,730,28,13,GlassUiPalette.Muted).alignment=TextAlignmentOptions.Center;
            ui.Label("OperationsTitle",T("app.chooseOperation"),48,620,700,40,24);
            ui.Button("RangeSetup",T("app.practiceRange"),1582,615,290,46,()=>{AppNavigation.RangeReturnToEditor=false;ShowRangeSetup();});
            if(catalog)for(int i=0;i<Mathf.Min(2,catalog.missions.Count);i++)if(catalog.missions[i])MissionCard(catalog.missions[i],48+i*924,680);
            ui.Label("Footer",T("app.homeFooter"),48,1012,1500,24,12,GlassUiPalette.Muted);
            if(notice.Length>0){ui.Label("Notice",notice,1100,1012,772,25,13,UiTheme.Current.amber);notice="";}
        }
        void MissionCard(MissionDefinition definition,float x,float y)
        {
            ui.Panel("Mission_"+definition.id,x,y,900,290);
            if(definition.thumbnail){var image=MenuSurface.Rect(ui.Root,"MissionPreview",x,y,320,290).gameObject.AddComponent<UnityEngine.UI.RawImage>();image.texture=definition.thumbnail;image.color=new Color(.55f,.65f,.7f,.72f);image.raycastTarget=false;image.uvRect=new UnityEngine.Rect(.2f,0,.6f,1);}
            ui.Label("MissionRegion",T(definition.regionKey),x+348,y+24,516,24,12,GlassUiPalette.Accent);
            ui.Label("MissionTitle",T(definition.titleKey),x+348,y+62,516,48,25);
            ui.Label("MissionDescription",T(definition.descriptionKey),x+348,y+118,516,60,15,GlassUiPalette.Muted);
            ui.Label("MissionBudget",T("app.budgetFrom",definition.baseBudget.ToString("N0")),x+348,y+198,250,24,13,GlassUiPalette.Muted);
            ui.Button("Prepare_"+definition.id,T("app.prepare"),x+650,y+220,214,46,()=>SelectMission(definition.id),true);
        }
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
            Begin(AppPage.WeaponGroups);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.29f,.56f);preview.ResetView();
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
            dialogOpen=true;dialogCancel=cancel;preview.AllowOrbit=false;
            var blocker=MenuSurface.Rect(ui.Root,"DialogBlocker",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.5f);
            ui.Panel("ConfirmationGlass",610,370,700,340,true);ui.Label("ConfirmationTitle",title,648,405,624,48,26);
            ui.Label("ConfirmationBody",body,648,474,624,92,17,GlassUiPalette.Muted);
            ui.Button("ConfirmationCancel",T("app.cancel"),648,620,292,50,cancel);
            ui.Button("ConfirmationAccept",yes,960,620,312,50,action,true);
        }
        void OnDestroy(){L10n.Changed-=OnLocaleChanged;}
    }
}
