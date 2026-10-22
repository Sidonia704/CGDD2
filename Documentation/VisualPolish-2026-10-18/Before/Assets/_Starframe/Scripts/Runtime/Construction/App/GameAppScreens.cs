using System;
using System.Linq;
using TMPro;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;
using Starframe.Presentation;

namespace Starframe.App
{
    public sealed partial class GameAppBootstrap
    {
        public void ShowDeployment()
        {
            if(!mission){ShowHome();return;}
            Begin(AppPage.Deployment);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.52f,.32f);
            Header(T("app.confirmDeployment"),T("app.deploymentSubtitle"));
            ui.Label("SelectedOperation",T(mission.titleKey),48,200,1080,48,27);
            ui.Label("OperationRegion",T(mission.regionKey),48,257,900,28,14,GlassUiPalette.Accent);
            ui.Label("PreviewHint",T("app.orbitHint"),140,882,840,30,13,GlassUiPalette.Muted).alignment=TextAlignmentOptions.Center;
            ui.Panel("DeploymentGlass",1190,200,682,772);
            ui.Label("SelectedVesselLabel",T("app.deployingShip"),1224,225,610,24,13,GlassUiPalette.Accent);
            UserName("DeployShipName",selected.name,1224,266,610,58,27);
            var difficultyNames=new System.Collections.Generic.List<string>{T("app.difficulty.Relaxed"),T("app.difficulty.Standard"),T("app.difficulty.Expert")};
            ui.Dropdown("Difficulty",difficultyNames,(int)AppNavigation.Difficulty,1224,342,614,48,index=>{AppNavigation.Difficulty=(MissionDifficulty)index;ShowDeployment();});
            CurrentDeployment=DeploymentValidator.Validate(selected,tuning.database,mission,AppNavigation.Difficulty);
            var report=CurrentDeployment;
            ui.Label("BudgetValue",T("app.budgetValue",report.Cost.ToString("N0"),report.Budget.ToString("N0")),1224,418,614,42,25,report.Cost>report.Budget?UiTheme.Current.danger:GlassUiPalette.Ink);
            ui.Label("BudgetRemaining",T("app.budgetRemaining",Mathf.Max(0,report.Budget-report.Cost).ToString("N0")),1224,469,614,28,14,GlassUiPalette.Muted);
            ui.Label("FlightReadiness",report.IsValid?T("app.readinessPassed"):T("app.readinessFailed"),1224,533,614,34,21,report.IsValid?UiTheme.Current.success:UiTheme.Current.danger);
            string issues=string.Join("\n",report.Errors.Concat(report.Warnings).Take(5).Select(issue=>"• "+T(issue.key,issue.args)));
            ui.Label("DeploymentIssues",issues.Length>0?issues:T("app.readinessBody"),1224,580,614,144,16,report.Errors.Count>0?UiTheme.Current.danger:GlassUiPalette.Muted);
            ui.Button("EditBeforeLaunch",T("app.editShip"),1224,744,295,50,OpenEditor);
            ui.Button("ChangeBeforeLaunch",T("app.changeShip"),1535,744,303,50,ShowShipPicker);
            var launch=ui.Button("ConfirmLaunch",T("app.confirmLaunch"),1224,828,614,64,()=>ConfirmLaunch(),true);launch.interactable=report.IsValid;
            ui.Label("LaunchHint",T("app.launchHint"),1224,913,614,38,13,GlassUiPalette.Muted);
        }
        public bool ConfirmLaunch()
        {
            if(!MissionSession.Confirm(ShipWorkspace.Ship,tuning.database,mission,AppNavigation.Difficulty,out var report)){CurrentDeployment=report;ShowDeployment();return false;}
            AppNavigation.SelectedMissionId=mission.id;AppNavigation.ReturnPage=AppPage.Deployment;
            if(!string.IsNullOrWhiteSpace(mission.sceneName)&&Application.CanStreamedLevelBeLoaded(mission.sceneName)){
                GameInput.Suspended=false;SceneLoad(mission.sceneName);return true;
            }
            ShowMissionReady();return true;
        }
        static void SceneLoad(string name)=>UnityEngine.SceneManagement.SceneManager.LoadScene(name);
        public void ShowMissionReady()
        {
            if(MissionSession.Current==null){ShowDeployment();return;}
            Begin(AppPage.MissionReady);preview.SetFocus(.64f,.63f);Header(T("app.shipConfirmed"),T("app.deploymentSubtitle"));
            ui.Panel("ReadyGlass",48,240,620,530,true);
            ui.Label("ReadyEyebrow",T("app.readyBadge"),82,274,552,28,14,GlassUiPalette.Accent);
            ui.Label("ReadyTitle",T(mission.titleKey),82,326,552,80,32);
            ui.Label("ReadyBody",T("app.operationUnavailable"),82,440,552,120,18,GlassUiPalette.Muted);
            ui.Button("ReadyBack",T("app.backDeployment"),82,608,552,54,ShowDeployment,true);
            ui.Button("ReadyRange",T("app.practiceRange"),82,684,552,48,()=>{AppNavigation.RangeReturnToEditor=false;ShowRangeSetup();});
        }
        public void ShowShipPicker()
        {
            var origin=page;dialogOpen=true;dialogCancel=()=>ShowPage(origin);preview.AllowOrbit=false;
            var blocker=MenuSurface.Rect(ui.Root,"PickerBlocker",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.55f);
            ui.Panel("ShipPickerGlass",470,170,980,740,true);ui.Label("PickerTitle",T("app.changeShip"),510,205,820,52,28);
            ui.Label("PickerSubtitle",T("app.pickerSubtitle"),510,270,880,48,15,GlassUiPalette.Muted);
            var scroll=ui.Scroll("ShipPickerScroll",510,338,900,460,out var content);var files=ShipWorkspace.Repository.List();
            content.sizeDelta=new Vector2(0,files.Length*76);int row=0;
            foreach(var file in files){
                string chosen=file;BlueprintData data=null;try{data=ShipWorkspace.Repository.Load(file);}catch(Exception e)when(e is System.IO.IOException||e is System.IO.InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){}
                string caption=data!=null?data.name+"    ·    "+T("app.parts",data.parts.Count):System.IO.Path.GetFileNameWithoutExtension(file)+"    ·    "+T("app.invalidBlueprint");
                var button=ui.Button("PickShip_"+row,caption,0,0,880,62,()=>{
                    Action replace=()=>{ShipWorkspace.Select(chosen);selected=ShipWorkspace.Ship;preview.Show(selected);ShowPage(origin);};
                    if(ShipWorkspace.Dirty)ConfirmDialog(T("app.replaceDraftTitle"),T("app.replaceDraftBody"),T("app.changeShip"),replace,()=>ShowPage(origin));else replace();
                });button.interactable=data!=null;
                var rect=(RectTransform)button.transform;rect.SetParent(content,false);rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.sizeDelta=new Vector2(0,62);rect.anchoredPosition=new Vector2(0,-row*76);
                L10n.ApplyUserContentFont(button.GetComponentInChildren<TextMeshProUGUI>());row++;
            }
            if(files.Length==0)ui.Label("NoBlueprints",T("app.noBlueprints"),540,440,810,120,19,GlassUiPalette.Muted);
            ui.Button("ClosePicker",T("app.cancel"),510,836,900,48,()=>ShowPage(origin));
        }
        public BattleTuning rangeBattleTuning;
        public void ShowRangeSetup()
        {
            Begin(AppPage.RangeSetup);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.68f,.77f);
            Header(T("app.rangeTitle"),T("app.rangeSubtitle"));
            ui.Label("DrillHeading",T("app.drillMode"),48,205,1200,35,20);
            var modes=(RangeBotMode[])Enum.GetValues(typeof(RangeBotMode));
            for(int i=0;i<modes.Length;i++){
                var mode=modes[i];float x=48+i*462;bool active=RangePracticeSettings.Mode==mode;
                ui.Panel("DrillMode_"+mode,x,266,438,154);
                ui.Button("SelectDrill_"+mode,T("app.mode."+mode),x+20,286,398,46,()=>{RangePracticeSettings.Mode=mode;ShowRangeSetup();},active);
                ui.Label("DrillModeHint",T("app.modeHint."+mode),x+20,349,398,52,14,GlassUiPalette.Muted);
            }
            ui.Panel("TargetRosterGlass",48,450,1138,508);
            ui.Label("TargetRosterHeading",T("app.targetRoster"),78,478,1078,34,22);
            ui.Label("TargetRosterHint",T("app.targetRosterHint"),78,524,1078,42,14,GlassUiPalette.Muted);
            var roster=new RangeTargetRoster(ShipWorkspace.BlueprintDirectory);
            var defaultTarget=rangeBattleTuning?rangeBattleTuning.defaultTargetBlueprint:null;
            var targets=roster.AvailableTargets(defaultTarget);
            ui.Scroll("TargetRosterScroll",78,589,1078,286,out var content);content.sizeDelta=new Vector2(0,targets.Count*70);
            int row=0,total=0;
            foreach(var target in targets){
                string chosen=target.file;int count=target.count;total+=count;
                var line=MenuSurface.Rect(ui.Root,"RosterRow",0,0,1038,58);line.SetParent(content,false);line.anchorMin=new Vector2(0,1);line.anchorMax=Vector2.one;line.pivot=new Vector2(.5f,1);line.sizeDelta=new Vector2(0,58);line.anchoredPosition=new Vector2(0,-row*70);
                var name=ui.Label("RosterShipName",target.builtIn?T("app.builtInTarget"):target.blueprint.name,0,0,730,48,17);PlaceInRow(name.rectTransform,line,0,0,700,58);if(!target.builtIn)L10n.ApplyUserContentFont(name);
                var minus=ui.Button("RosterMinus_"+row,"−",0,0,52,48,()=>{roster.SetCount(chosen,Mathf.Max(0,count-1),defaultTarget);ShowRangeSetup();});PlaceInRow((RectTransform)minus.transform,line,750,0,52,48);minus.interactable=count>0;
                var counter=ui.Label("RosterCount",count.ToString(),0,0,72,48,18);counter.alignment=TextAlignmentOptions.Center;PlaceInRow(counter.rectTransform,line,810,0,72,48);
                var plus=ui.Button("RosterPlus_"+row,"+",0,0,52,48,()=>{roster.SetCount(chosen,count+1,defaultTarget);ShowRangeSetup();});PlaceInRow((RectTransform)plus.transform,line,890,0,52,48);plus.interactable=count<int.MaxValue;row++;
            }
            if(row==0)ui.Label("EmptyTargets",T("app.noTargetBlueprints"),80,640,1070,90,17,GlassUiPalette.Muted);
            content.sizeDelta=new Vector2(0,row*70);
            ui.Label("TotalTargets",T("app.targetTotal",total),78,903,1078,25,15,GlassUiPalette.Accent);
            ui.Panel("RangeShipGlass",1210,450,662,508);
            ui.Label("RangeShipHeading",T("app.deployingShip"),1242,480,596,24,13,GlassUiPalette.Accent);
            UserName("RangeShipName",selected.name,1242,526,596,70,27);
            ui.Label("RangeBudgetFree",T("app.rangeNoBudget"),1242,617,596,48,16,GlassUiPalette.Muted);
            ui.Button("RangeEdit",T("app.editShip"),1242,697,288,48,OpenEditor);
            ui.Button("RangeChangeShip",T("app.changeShip"),1546,697,294,48,ShowShipPicker);
            ui.Button("EnterRange",T("app.enterRange"),1242,796,598,64,EnterRange,true);
            ui.Label("RangeExitHint",T("app.rangeExitHint"),1242,886,598,42,14,GlassUiPalette.Muted);
        }
        public void EnterRange()
        {
            var report=DeploymentValidator.ValidateForRange(ShipWorkspace.Ship,tuning.database);
            if(!report.IsValid){ConfirmDialog(T("app.readinessFailed"),string.Join("\n",report.Errors.Take(4).Select(e=>T(e.key,e.args))),T("app.editShip"),OpenEditor,ShowRangeSetup);return;}
            FlightSession.Begin(ShipWorkspace.Ship,ShipWorkspace.Dirty);GameInput.Suspended=false;SceneLoad(FlightSession.RangeScene);
        }
        static void PlaceInRow(RectTransform rect,RectTransform parent,float x,float y,float w,float h)
        {
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);rect.localScale=Vector3.one;
        }
        public void ShowHelp()
        {
            Begin(AppPage.Help);Header(T("app.helpTitle"),T("app.helpSubtitle"));preview.SetFocus(.58f,.80f);
            ui.Panel("HelpGlass",48,224,1280,732,true);
            ui.Label("FlightHelpTitle",T("app.helpFlight"),82,256,1200,40,24);
            string key(GameAction action)=>GameInput.BindingLabel(action);
            ui.Label("FlightHelpBody",T("app.helpFlightBody",key(GameAction.Forward),key(GameAction.Backward),key(GameAction.Left),key(GameAction.Right),key(GameAction.Up),key(GameAction.Down),key(GameAction.RollLeft),key(GameAction.RollRight),key(GameAction.Boost),key(GameAction.TargetLock)),82,319,1200,176,18,GlassUiPalette.Muted);
            ui.Label("AssemblyHelpTitle",T("app.helpAssembly"),82,534,1200,40,24);
            ui.Label("AssemblyHelpBody",T("app.helpAssemblyBody"),82,596,1200,128,18,GlassUiPalette.Muted);
            ui.Label("CombatHelpTitle",T("app.helpCombat"),82,762,1200,40,24);
            ui.Label("CombatHelpBody",T("app.helpCombatBody"),82,824,1200,102,18,GlassUiPalette.Muted);
        }
    }
}
