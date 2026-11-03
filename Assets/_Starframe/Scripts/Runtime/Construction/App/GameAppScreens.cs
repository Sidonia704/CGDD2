// @role Implement deployment, blueprint selection and range-setup screens of the app.
// @calls DeploymentValidator, ShipWorkspace, RangeTargetRoster, RangePracticeSettings, RangeLoadingOverlay

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
        float rangeRosterPosition=1;
        public void ShowDeployment()
        {
            if(!mission){ShowHome();return;}
            Begin(AppPage.Deployment);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.52f,.32f);
            Header(T("app.confirmDeployment"),T("app.deploymentSubtitle"));
            ui.Label("SelectedOperation",T(mission.titleKey),48,254,1080,48,30);
            ui.Label("OperationRegion",T(mission.regionKey),48,312,900,28,15,GlassUiPalette.Muted);
            ui.Label("PreviewHint",T("app.orbitHint"),140,882,840,30,13,GlassUiPalette.Muted).alignment=TextAlignmentOptions.Center;
            ui.Panel("DeploymentGlass",1190,240,682,776);
            ui.Label("SelectedVesselLabel",T("app.deployingShip"),1224,268,610,24,13,GlassUiPalette.Muted);
            UserName("DeployShipName",selected.name,1224,306,610,58,29);
            var difficultyNames=new System.Collections.Generic.List<string>{T("app.difficulty.Relaxed"),T("app.difficulty.Standard"),T("app.difficulty.Expert")};
            ui.Label("DifficultyLabel",T("app.ui.difficulty"),1224,382,240,28,15,GlassUiPalette.Muted);
            ui.Dropdown("Difficulty",difficultyNames,(int)AppNavigation.Difficulty,1484,372,354,48,index=>{AppNavigation.Difficulty=(MissionDifficulty)index;ShowDeployment();});
            CurrentDeployment=DeploymentValidator.Validate(selected,tuning.database,mission,AppNavigation.Difficulty);
            var report=CurrentDeployment;
            ui.Label("BudgetValue",T("app.budgetValue",report.Cost.ToString("N0"),report.Budget.ToString("N0")),1224,456,614,42,25,report.Cost>report.Budget?UiTheme.Current.danger:GlassUiPalette.Ink);
            var track=MenuSurface.Rect(ui.Root,"BudgetTrack",1224,514,614,5).gameObject.AddComponent<UnityEngine.UI.Image>();track.color=new Color(1,1,1,.10f);track.raycastTarget=false;
            var fill=MenuSurface.Rect(ui.Root,"BudgetFill",1224,514,614*Mathf.Clamp01(report.Budget>0?report.Cost/report.Budget:1),5).gameObject.AddComponent<UnityEngine.UI.Image>();fill.color=report.Cost>report.Budget?UiTheme.Current.danger:GlassUiPalette.Accent;fill.raycastTarget=false;
            ui.Label("BudgetRemaining",T("app.budgetRemaining",Mathf.Max(0,report.Budget-report.Cost).ToString("N0")),1224,535,614,28,15,GlassUiPalette.Muted);
            ui.Label("FlightReadiness",report.IsValid?T("app.readinessPassed"):T("app.readinessFailed"),1224,594,614,34,22,report.IsValid?UiTheme.Current.success:UiTheme.Current.danger);
            var issues=report.Errors.Concat(report.Warnings).ToArray();
            ui.Scroll("DeploymentIssuesScroll",1224,645,614,146,out var issueContent);
            string issueText=issues.Length>0?string.Join("\n\n",issues.Select(issue=>"• "+T(issue.key,issue.args))):T("app.readinessBody");
            var issueLabel=ui.Label("DeploymentIssues",issueText,0,0,1,1,17,report.Errors.Count>0?UiTheme.Current.danger:GlassUiPalette.Muted);
            float issueHeight=Mathf.Max(144,issueLabel.GetPreferredValues(issueText,576,10000).y+16);
            PlaceInRow(issueLabel.rectTransform,issueContent,0,0,576,issueHeight);issueContent.sizeDelta=new Vector2(0,issueHeight);
            ui.Button("EditBeforeLaunch",T("app.editShip"),1224,815,295,50,OpenEditor);
            ui.Button("ChangeBeforeLaunch",T("app.changeShip"),1535,815,303,50,ShowShipPicker);
            var launch=ui.Button("ConfirmLaunch",T("app.confirmLaunch"),1224,889,614,60,()=>ConfirmLaunch(),true);launch.interactable=report.IsValid;
            if(!report.IsValid)ui.Hint(launch,T("app.readinessFailed")+"\n"+string.Join("\n",report.Errors.Take(3).Select(issue=>T(issue.key,issue.args))));
            ui.Label("LaunchHint",T(HasOperationScene(mission)?"app.launchHint":"app.ui.missionPendingHint"),1224,964,614,42,14,GlassUiPalette.Muted);
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
        static void SceneLoad(string name){RangeLoadingOverlay.LoadScene(name);}
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
        // Compatibility entry point: callers now enter a dedicated library page, not a modal.
        public void ShowShipPicker()
        {
            libraryReturn=page==AppPage.BlueprintLibrary?AppPage.Home:page;
            ShowBlueprintLibrary();
        }
        public void ShowBlueprintLibrary()
        {
            Begin(AppPage.BlueprintLibrary);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.57f,.71f);
            libraryScreen=gameObject.AddComponent<BlueprintLibraryScreen>();
            libraryScreen.Initialize(ui,preview,tuning.database,CloseBlueprintLibrary);
        }
        void CloseBlueprintLibrary()
        {
            if(AppNavigation.BlueprintsReturnToEditor){
                AppNavigation.BlueprintsReturnToEditor=false;AppNavigation.ReturnPage=AppNavigation.EditorResumePage;AppNavigation.OpenEditor();
            }else ShowPage(libraryReturn);
        }
        public BattleTuning rangeBattleTuning;
        public void ShowRangeSetup()
        {
            Begin(AppPage.RangeSetup);selected=ShipWorkspace.Ship;preview.Show(selected);preview.SetFocus(.68f,.77f);
            Header(T("app.rangeTitle"),T("app.rangeSubtitle"));
            ui.Label("DrillHeading",T("app.drillMode"),48,235,1200,35,20);
            var modes=(RangeBotMode[])Enum.GetValues(typeof(RangeBotMode));
            for(int i=0;i<modes.Length;i++){
                var mode=modes[i];float x=48+i*462;bool active=RangePracticeSettings.Mode==mode;
                ui.Panel("DrillMode_"+mode,x,286,438,140);
                ui.Button("SelectDrill_"+mode,T("app.mode."+mode),x+20,302,398,48,()=>{RangePracticeSettings.Mode=mode;ShowRangeSetup();},active);
                ui.Label("DrillModeHint",T("app.modeHint."+mode),x+20,363,398,50,15,GlassUiPalette.Muted);
            }
            ui.Panel("TargetRosterGlass",48,450,1138,508);
            ui.Label("TargetRosterHeading",T("app.targetRoster"),78,478,1078,34,22);
            ui.Label("TargetRosterHint",T("app.targetRosterHint"),78,524,1078,42,14,GlassUiPalette.Muted);
            var roster=new RangeTargetRoster(ShipWorkspace.BlueprintDirectory);
            var defaultTarget=rangeBattleTuning?rangeBattleTuning.defaultTargetBlueprint:null;
            var targets=roster.AvailableTargets(defaultTarget);
            var rosterScroll=ui.Scroll("TargetRosterScroll",78,589,1078,286,out var content);content.sizeDelta=new Vector2(0,targets.Count*70);
            void RefreshRoster(){rangeRosterPosition=rosterScroll.verticalNormalizedPosition;ShowRangeSetup();}
            int row=0,total=0;
            foreach(var target in targets){
                string chosen=target.file;int count=target.count;total+=count;
                var line=MenuSurface.Rect(ui.Root,"RosterRow",0,0,1038,58);line.SetParent(content,false);line.anchorMin=new Vector2(0,1);line.anchorMax=Vector2.one;line.pivot=new Vector2(.5f,1);line.sizeDelta=new Vector2(0,58);line.anchoredPosition=new Vector2(0,-row*70);
                var name=ui.Label("RosterShipName",target.builtIn?T("app.builtInTarget"):target.blueprint.name,0,0,730,48,17);PlaceInRow(name.rectTransform,line,0,0,700,58);if(!target.builtIn)L10n.ApplyUserContentFont(name);
                var minus=ui.Button("RosterMinus_"+row,"−",0,0,52,48,()=>{roster.SetCount(chosen,Mathf.Max(0,count-1),defaultTarget);RefreshRoster();});PlaceInRow((RectTransform)minus.transform,line,750,0,52,48);minus.interactable=count>0;
                var counter=ui.Label("RosterCount",count.ToString(),0,0,72,48,18);counter.alignment=TextAlignmentOptions.Center;PlaceInRow(counter.rectTransform,line,810,0,72,48);
                var plus=ui.Button("RosterPlus_"+row,"+",0,0,52,48,()=>{roster.SetCount(chosen,count+1,defaultTarget);RefreshRoster();});PlaceInRow((RectTransform)plus.transform,line,890,0,52,48);plus.interactable=count<int.MaxValue;row++;
            }
            if(row==0)ui.Label("EmptyTargets",T("app.noTargetBlueprints"),80,640,1070,90,17,GlassUiPalette.Muted);
            content.sizeDelta=new Vector2(0,row*70);
            Canvas.ForceUpdateCanvases();rosterScroll.verticalNormalizedPosition=rangeRosterPosition;
            ui.Label("TotalTargets",T("app.targetTotal",total),78,903,1078,25,15,GlassUiPalette.Accent);
            ui.Panel("RangeShipGlass",1210,450,662,508);
            ui.Label("RangeShipHeading",T("app.deployingShip"),1242,480,596,24,13,GlassUiPalette.Accent);
            UserName("RangeShipName",selected.name,1242,526,596,70,27);
            ui.Label("RangeBudgetFree",T("app.rangeNoBudget"),1242,604,596,24,15,GlassUiPalette.Muted);
            float generation=0,storage=0,weaponPower=0;
            foreach(var part in selected.parts){
                var module=tuning.database.Get(part.shape);if(!module)continue;
                generation+=module.generationWatts;storage+=module.storageJoules;
                if(module.weapon)weaponPower+=Mathf.Max(0,module.weapon.sustainedPowerWatts);
            }
            ui.Label("RangePowerSummary",T("app.rangePowerSummary",generation,storage,weaponPower,tuning.boostPowerWatts),1242,636,596,48,14,GlassUiPalette.Muted);
            ui.Button("RangeEdit",T("app.editShip"),1242,697,288,48,OpenEditor);
            ui.Button("RangeChangeShip",T("app.changeShip"),1546,697,294,48,ShowShipPicker);
            ui.Button("EnterRange",T("app.enterRange"),1242,796,598,64,EnterRange,true);
            ui.Label("RangeExitHint",T("app.rangeExitHint"),1242,886,598,42,14,GlassUiPalette.Muted);
        }
        public void EnterRange()
        {
            if(dialogOpen)return;
            var report=DeploymentValidator.ValidateForRange(ShipWorkspace.Ship,tuning.database);
            if(!report.IsValid){ConfirmDialog(T("app.readinessFailed"),string.Join("\n",report.Errors.Take(4).Select(e=>T(e.key,e.args))),T("app.editShip"),OpenEditor,ShowRangeSetup);return;}
            if(report.Warnings.Count>0){
                ConfirmDialog(T("app.rangeWarnings"),string.Join("\n",report.Warnings.Select(issue=>"• "+T(issue.key,issue.args))),T("app.continueRange"),LaunchRange,OpenEditor);
                var labels=ui.Root.GetComponentsInChildren<TextMeshProUGUI>();
                L10n.Bind(labels.First(label=>label.name=="ConfirmationCancelLabel"),"app.editShip");
                var body=labels.First(label=>label.name=="ConfirmationBody");
                body.enableAutoSizing=true;body.fontSizeMin=12;body.fontSizeMax=17;
                return;
            }
            LaunchRange();
        }
        void LaunchRange()
        {
            MissionSession.Clear();
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
            ui.Label("FlightHelpBody",T("app.helpFlightBody",key(GameAction.Forward),key(GameAction.Backward),key(GameAction.Left),key(GameAction.Right),key(GameAction.Up),key(GameAction.Down),key(GameAction.RollLeft),key(GameAction.RollRight),key(GameAction.Boost),key(GameAction.TargetLock),key(GameAction.Countermeasure)),82,319,1200,176,18,GlassUiPalette.Muted);
            ui.Label("AssemblyHelpTitle",T("app.helpAssembly"),82,534,1200,40,24);
            ui.Label("AssemblyHelpBody",T("app.helpAssemblyBody"),82,596,1200,128,18,GlassUiPalette.Muted);
            ui.Label("CombatHelpTitle",T("app.helpCombat"),82,762,1200,40,24);
            ui.Label("CombatHelpBody",T("app.helpCombatBody"),82,824,1200,102,18,GlassUiPalette.Muted);
        }
    }
}
