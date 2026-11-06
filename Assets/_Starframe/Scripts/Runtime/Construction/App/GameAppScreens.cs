// @role Implement deployment, blueprint selection and range-setup screens of the app.
// @calls DeploymentValidator, ShipWorkspace, RangeTargetRoster, RangePracticeSettings, RangeLoadingOverlay, MenuShipMetrics, FigmaVisuals

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
        string rangeTargetFile,rangePickerFile;int rangePickerPage;
        static string MenuCopy(string zh,string en)=>L10n.IsEnglish?en:zh;
        UnityEngine.UI.Button MenuButton(string name,string text,float x,float y,float width,float height,Action action,float size=22,bool primary=false,Color? ink=null)
        {
            var button=ui.Button(name,text,x,y,width,height,action,primary);var label=button.GetComponentInChildren<TextMeshProUGUI>();UiStyle.ApplyText(label,size);label.color=ink??(primary?Color.white:GlassUiPalette.Accent);FigmaVisuals.SetFont(label,true);if(primary)MenuActionShape.Apply(button);return button;
        }
        void FigmaMenuHeader(string title,bool back=true)
        {
            if(!string.IsNullOrEmpty(title)){var heading=ui.Label("PageTitle",title,37,29,1430,108,48,Color.white);FigmaVisuals.SetFont(heading,true);heading.textWrappingMode=TextWrappingModes.NoWrap;heading.overflowMode=TextOverflowModes.Overflow;}
            var help=MenuButton("Help","?",1748,37,56,56,ShowHelp,45);ui.Hint(help,T("app.helpTitle"));
            var settings=MenuButton("Settings","",1817,37,56,56,ShowSettings,39);ui.Hint(settings,T("app.settings"));
            var gear=MenuSurface.Rect(ui.Root,"SettingsIcon",1828,48,34,34).gameObject.AddComponent<UnityEngine.UI.RawImage>();gear.texture=Resources.Load<Texture2D>("UI/Figma/MenuSettings");gear.color=new Color32(205,253,81,255);gear.raycastTarget=false;
            var maskShader=Shader.Find("Starframe/UI/FigmaAlphaMask");if(maskShader){gear.material=new Material(maskShader);var resource=gear.gameObject.AddComponent<MenuIconMaterialOwner>();resource.value=gear.material;}
            if(back)MenuButton("Back",MenuCopy("返回","BACK"),50,1006,114,44,ShowHome,20,false,Color.white);
        }
        public void ShowDeployment()
        {
            if(!mission){ShowHome();return;}
            Begin(AppPage.Deployment);selected=ShipWorkspace.Ship;preview.Show(selected);preview.ResetView();preview.SetFocus(.51f,.43f);preview.FitViewport(.66f,.66f);
            FigmaMenuHeader(MenuCopy("部署","DEPLOYMENT"));
            var badge=MenuButton("MissionLevel",MenuCopy("关卡 1","LEVEL 1"),37,153,152,44,()=>{},22,false,Color.black);badge.GetComponent<UnityEngine.UI.Image>().color=GlassUiPalette.Accent;
            var operation=ui.Label("SelectedOperation",T(mission.titleKey),218,153,620,76,24,Color.white);operation.textWrappingMode=TextWrappingModes.Normal;
            ui.Panel("DeploymentGlass",1419,374,459,121);
            ui.Label("SelectedVesselLabel",T("app.deployingShip"),1442,388,330,35,22,Color.white);
            var name=ui.Label("DeployShipName",selected.name,1442,435,410,46,34,GlassUiPalette.Accent);L10n.ApplyUserContentFont(name);
            CurrentDeployment=DeploymentValidator.Validate(selected,tuning.database,mission,AppNavigation.Difficulty);var report=CurrentDeployment;
            var readiness=ui.Label("FlightReadiness",report.IsValid?MenuCopy("舰船就绪","Ship ready"):MenuCopy("检查舰船","Check ship"),1717,386,146,30,19,report.IsValid?GlassUiPalette.Accent:UiTheme.Current.danger);readiness.alignment=TextAlignmentOptions.MidlineRight;
            ui.Panel("DifficultyGlass",1419,507,459,158);ui.Label("DifficultyLabel",MenuCopy("难度","DIFFICULTY"),1437,521,415,36,22);
            string[] difficultyNames={MenuCopy("休闲","Relaxed"),MenuCopy("标准","Standard"),MenuCopy("专家","Expert"),MenuCopy("地狱","Hell")};
            for(int i=0;i<4;i++){int step=i;MenuButton("DifficultyStep_"+i,difficultyNames[i],1429+i*111,574,108,35,()=>{AppNavigation.Difficulty=(MissionDifficulty)step;ShowDeployment();},18,false,Color.white);}
            var difficulty=ui.Slider("Difficulty",0,3,(int)AppNavigation.Difficulty,1443,625,410,26,value=>{AppNavigation.Difficulty=(MissionDifficulty)Mathf.RoundToInt(value);ShowDeployment();});difficulty.wholeNumbers=true;
            difficulty.handleRect.GetComponent<UnityEngine.UI.Image>().color=GlassUiPalette.Accent;
            difficulty.fillRect.GetComponent<UnityEngine.UI.Image>().color=new Color(.75f,.75f,.75f);
            ui.Panel("BudgetGlass",1419,677,459,121);ui.Label("BudgetHeading",MenuCopy("预算","BUDGET"),1437,691,175,38,22,GlassUiPalette.Accent);
            var budget=ui.Label("BudgetValue","<color=#B9FF03>"+report.Cost.ToString("N0")+"</color>/"+report.Budget.ToString("N0"),1605,691,250,38,24,report.Cost>report.Budget?UiTheme.Current.danger:Color.white);budget.alignment=TextAlignmentOptions.MidlineRight;
            var track=MenuSurface.Rect(ui.Root,"BudgetTrack",1442,756,410,22).gameObject.AddComponent<UnityEngine.UI.Image>();track.color=new Color(.82f,.82f,.82f);track.raycastTarget=false;
            var fill=MenuSurface.Rect(ui.Root,"BudgetFill",1442,756,410*Mathf.Clamp01(report.Budget>0?report.Cost/report.Budget:1),22).gameObject.AddComponent<UnityEngine.UI.Image>();fill.color=report.Cost>report.Budget?UiTheme.Current.danger:GlassUiPalette.Accent;fill.raycastTarget=false;
            MenuButton("EditBeforeLaunch",MenuCopy("编辑舰船","EDIT SHIP"),1419,810,224,64,OpenEditor,24,false,Color.white);
            MenuButton("ChangeBeforeLaunch",MenuCopy("更换舰船","CHANGE SHIP"),1655,810,223,64,ShowShipPicker,24,false,Color.white);
            var launch=MenuButton("ConfirmLaunch",MenuCopy("开始","START"),1550,952,320,82,()=>ConfirmLaunch(),32,true);launch.interactable=report.IsValid;
            var issues=report.Errors.Concat(report.Warnings).ToArray();if(issues.Length>0){string issueText=string.Join("\n",issues.Select(issue=>"• "+T(issue.key,issue.args)));ui.Hint(readiness,issueText);ui.Hint(launch,issueText);ui.Label("DeploymentIssues",issueText,825,927,562,100,18,report.Errors.Count>0?UiTheme.Current.danger:Color.white);}
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
            libraryScreen.Initialize(ui,preview,tuning.database,CloseBlueprintLibrary,mission);
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
            Begin(AppPage.RangeSetup);selected=ShipWorkspace.Ship;preview.Show(selected);preview.ResetView();preview.SetFocus(.51f,.63f);preview.FitViewport(.66f,.66f);
            FigmaMenuHeader(MenuCopy("训练场","TRAINING RANGE"));
            var roster=new RangeTargetRoster(ShipWorkspace.BlueprintDirectory);var defaultTarget=rangeBattleTuning?rangeBattleTuning.defaultTargetBlueprint:null;
            var targets=roster.AvailableTargets(defaultTarget);
            var target=targets.FirstOrDefault(t=>t.file==rangeTargetFile)??targets.FirstOrDefault(t=>t.count>0)??targets.FirstOrDefault();
            if(target!=null)
            {
                rangeTargetFile=target.file;
                ui.Panel("RangeTargetCard",56,218,257,211);var picture=MenuSurface.Rect(ui.Root,"RangeTargetThumbnail",56,225,257,166).gameObject.AddComponent<UnityEngine.UI.RawImage>();picture.texture=preview.Thumbnail(target.blueprint);picture.raycastTarget=false;
                var name=ui.Label("RangeTargetName",target.blueprint.name,63,397,132,29,22,GlassUiPalette.Accent);L10n.ApplyUserContentFont(name);name.textWrappingMode=TextWrappingModes.NoWrap;name.alignment=TextAlignmentOptions.MidlineLeft;
                var cost=ui.Label("RangeTargetCost",BlueprintCostDisplay.Label(target.blueprint,tuning.database,mission),198,399,111,24,14,GlassUiPalette.Accent);cost.alignment=TextAlignmentOptions.MidlineRight;cost.textWrappingMode=TextWrappingModes.NoWrap;cost.enableAutoSizing=true;cost.fontSizeMin=11;cost.fontSizeMax=14;
                ui.Label("RangeCountLabel",MenuCopy("数量","Count"),172,356,66,30,18,Color.white);
                var minus=MenuButton("RosterMinus","−",236,352,23,33,()=>{roster.SetCount(target.file,Mathf.Max(0,target.count-1),defaultTarget);ShowRangeSetup();},25);minus.interactable=target.count>0;
                ui.Label("RosterCount",target.count.ToString(),264,356,24,30,21,GlassUiPalette.Accent).alignment=TextAlignmentOptions.Center;
                var plus=MenuButton("RosterPlus","+",288,352,22,33,()=>{roster.SetCount(target.file,target.count+1,defaultTarget);ShowRangeSetup();},25);plus.interactable=target.count<int.MaxValue;
            }
            var targetBar=MenuSurface.Rect(ui.Root,"TargetBar",56,176,256,44).gameObject.AddComponent<UnityEngine.UI.Image>();targetBar.color=new Color(53/255f,58/255f,74/255f);targetBar.raycastTarget=false;
            ui.Label("TargetLabel",MenuCopy("靶机：","TARGET:"),64,183,235,35,22);
            var select=MenuButton("SelectTarget",MenuCopy("选择靶机","SELECT TARGET"),56,448,257,52,ShowRangeTargetPicker,22,false,Color.black);select.GetComponent<UnityEngine.UI.Image>().color=GlassUiPalette.Accent;select.interactable=targets.Count>0;
            ui.Panel("TargetBehavior",56,521,257,179);ui.Label("TargetBehaviorTitle",MenuCopy("靶机行为","TARGET BEHAVIOR"),73,531,232,35,20,Color.white);
            bool attack=RangePracticeSettings.Mode==RangeBotMode.StationaryFire||RangePracticeSettings.Mode==RangeBotMode.Combat;
            bool move=RangePracticeSettings.Mode==RangeBotMode.FlyOnly||RangePracticeSettings.Mode==RangeBotMode.Combat;
            void SetMode(bool a,bool m){RangePracticeSettings.Mode=a?(m?RangeBotMode.Combat:RangeBotMode.StationaryFire):(m?RangeBotMode.FlyOnly:RangeBotMode.Stationary);ShowRangeSetup();}
            RangeBehaviorToggle("RangeAttack",MenuCopy("攻击","ATTACK"),attack,572,()=>SetMode(!attack,move));
            RangeBehaviorToggle("RangeMove",MenuCopy("移动","MOVE"),move,635,()=>SetMode(attack,!move));
            ui.Label("TotalTargets",MenuCopy("已配置靶机：","CONFIGURED TARGETS: ")+targets.Sum(t=>t.count),56,719,470,30,18,GlassUiPalette.Accent);
            MenuButton("RangeEdit",MenuCopy("编辑舰船","EDIT SHIP"),56,767,257,46,OpenEditor,22);
            MenuButton("RangeChangeShip",MenuCopy("更换舰船","CHANGE SHIP"),56,826,257,46,ShowShipPicker,22);
            MenuButton("EnterRange",MenuCopy("进入","ENTER"),1550,952,320,82,EnterRange,32,true);
        }
        void RangeBehaviorToggle(string name,string text,bool enabled,float y,Action action)
        {
            ui.Label(name+"Label",text,80,y+4,130,32,20,GlassUiPalette.Accent).alignment=TextAlignmentOptions.MidlineLeft;
            var button=MenuButton(name,"",238,y,66,36,action,1);button.GetComponent<UnityEngine.UI.Image>().color=new Color(52/255f,57/255f,77/255f);
            var knob=MenuSurface.Rect(ui.Root,name+"Thumb",enabled?275:244,y+6,23,24).gameObject.AddComponent<UnityEngine.UI.Image>();knob.color=enabled?GlassUiPalette.Accent:new Color(.56f,.56f,.56f);knob.raycastTarget=false;
        }
        void ShowRangeTargetPicker()
        {
            var roster=new RangeTargetRoster(ShipWorkspace.BlueprintDirectory);var defaultTarget=rangeBattleTuning?rangeBattleTuning.defaultTargetBlueprint:null;var targets=roster.AvailableTargets(defaultTarget);
            rangePickerFile=rangePickerFile??rangeTargetFile;int pages=Mathf.Max(1,(targets.Count+11)/12);rangePickerPage=Mathf.Clamp(rangePickerPage,0,pages-1);
            // Replacing this modal rebuilds the setup behind it and removes every previous blocker.
            ShowRangeSetup();dialogOpen=true;dialogCancel=()=>{rangePickerFile=null;ShowRangeSetup();};preview.AllowOrbit=false;
            var modal=MenuSurface.Rect(ui.Root,"RangeTargetPicker",0,0,1920,1080);var blocker=modal.gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.49f);blocker.raycastTarget=true;
            void Attach(Component component){component.transform.SetParent(modal,false);}
            var panel=MenuSurface.Rect(ui.Root,"TargetPickerBackground",306,133,1228,839).gameObject.AddComponent<UnityEngine.UI.Image>();panel.color=FigmaVisuals.Background;panel.raycastTarget=true;Attach(panel);
            for(int i=rangePickerPage*12;i<Mathf.Min(targets.Count,(rangePickerPage+1)*12);i++)
            {
                var target=targets[i];int slot=i%12;float x=321+(slot%4)*303,y=155+(slot/4)*247;
                var card=MenuButton("TargetPick_"+i,"",x,y,266,213,()=>{rangePickerFile=target.file;ShowRangeTargetPicker();},20);Attach(card);
                var picture=MenuSurface.Rect(ui.Root,"TargetThumbnail_"+i,x,y,266,174).gameObject.AddComponent<UnityEngine.UI.RawImage>();picture.texture=preview.Thumbnail(target.blueprint);picture.raycastTarget=false;Attach(picture);
                var name=ui.Label("TargetName_"+i,target.blueprint.name,x+6,y+174,137,39,20,GlassUiPalette.Accent);L10n.ApplyUserContentFont(name);name.alignment=TextAlignmentOptions.MidlineLeft;name.textWrappingMode=TextWrappingModes.NoWrap;name.enableAutoSizing=true;name.fontSizeMin=15;name.fontSizeMax=20;Attach(name);
                var cost=ui.Label("TargetCost_"+i,BlueprintCostDisplay.Label(target.blueprint,tuning.database,mission),x+147,y+174,115,39,14,GlassUiPalette.Accent);cost.alignment=TextAlignmentOptions.MidlineRight;cost.textWrappingMode=TextWrappingModes.NoWrap;cost.enableAutoSizing=true;cost.fontSizeMin=11;cost.fontSizeMax=14;Attach(cost);
                if(target.file==rangePickerFile){var outline=card.gameObject.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=GlassUiPalette.Accent;outline.effectDistance=new Vector2(3,3);}
            }
            var last=MenuButton("TargetPreviousPage",MenuCopy("上一页","PREVIOUS"),321,905,148,45,()=>{rangePickerPage--;ShowRangeTargetPicker();},19,false,Color.white);last.interactable=rangePickerPage>0;Attach(last);
            var next=MenuButton("TargetNextPage",MenuCopy("下一页","NEXT PAGE"),485,905,148,45,()=>{rangePickerPage++;ShowRangeTargetPicker();},19,false,Color.white);next.interactable=rangePickerPage<pages-1;Attach(next);
            Attach(ui.Label("TargetPage",(rangePickerPage+1)+"/"+pages,655,914,120,30,20));
            Attach(MenuButton("TargetPickerCancel",MenuCopy("取消","CANCEL"),1050,888,140,57,()=>{rangePickerFile=null;ShowRangeSetup();},22,false,Color.white));
            Attach(MenuButton("TargetPickerConfirm",MenuCopy("确认","CONFIRM"),1343,888,167,57,()=>{if(!string.IsNullOrEmpty(rangePickerFile)){int count=Mathf.Max(1,roster.GetCount(rangeTargetFile));roster.SelectOnly(rangePickerFile,count,defaultTarget);rangeTargetFile=rangePickerFile;}rangePickerFile=null;ShowRangeSetup();},24));
            UiModalScope.Protect(ui.Root,modal);
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
