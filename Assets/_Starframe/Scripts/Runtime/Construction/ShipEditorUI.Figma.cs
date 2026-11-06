// @role Render the Figma assembly workbench, native catalogue and integrated weapon groups.
// @calls ShipEditorController, AssemblyModuleCatalogue, AssemblyWeaponGroups, ShipPerformance, FigmaVisuals, MenuShipMetrics, MissionCatalog, MissionDefinition

using System;
using TMPro;
using Starframe.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Starframe.Construction
{
    public sealed partial class ShipEditorUI
    {
        static readonly Color FPanel=new Color32(27,31,45,255),FLime=new Color32(185,255,3,255);
        RectTransform figmaRoot,figmaRight,figmaStatsBody,figmaPerformanceBody,figmaModuleBody,figmaDescription;
        Image figmaStatsPanel,figmaPerformancePanel,figmaModulePanel;
        TextMeshProUGUI figmaCost,figmaParts,figmaStats,figmaPerformance,figmaDescriptionText,figmaHelp,figmaGroupsLabel;
        TextMeshProUGUI figmaStatsToggle,figmaPerformanceToggle,figmaModuleToggle;
        bool figmaStatsExpanded=true,figmaPerformanceExpanded=true,figmaModuleExpanded=true;
        AssemblyModuleCatalogue moduleCatalogue;
        AssemblyWeaponGroups weaponOverlay;
        UnityEngine.UI.Button mirrorTool;
        [SerializeField] Starframe.App.MissionCatalog assemblyMissions;
        public bool IsCatalogueOpen=>moduleCatalogue&&moduleCatalogue.IsOpen;
        public bool IsWeaponGroupsOpen=>weaponOverlay&&weaponOverlay.IsOpen;
        public bool IsSpecialEditorMode=>IsCatalogueOpen||IsWeaponGroupsOpen;
        public bool HasEditorFocus=>EditingName;
        public RectTransform CanvasRoot=>figmaRoot;
        public Camera CanvasCamera=>presentationCamera;

        public void Initialize(ShipEditorController editor,Camera camera)
        {
            controller=editor;worldCamera=camera;ObservePerformanceAssembly(editor.Assembly);assemblyLayout=true;font=L10n.CurrentFont;
            L10n.Changed+=RefreshLocalizedContent;Starframe.App.GameSettings.Changed+=OnUiSettingsChanged;
            var go=new GameObject("ConstructionCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            go.layer=5;go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();
            if(!presentationCamera)throw new InvalidOperationException("Presentation camera is not assigned.");
            presentationCamera.enabled=true;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=presentationCamera;canvas.planeDistance=1;
            worldCamera.cullingMask&=~(1<<5);
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            if(!FindFirstObjectByType<EventSystem>()){
                var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));events.transform.SetParent(transform,false);events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            figmaRoot=(RectTransform)go.transform;
            background=MakeRect(figmaRoot,"WorldViewport",0,0,1920,1080).gameObject.AddComponent<RawImage>();background.raycastTarget=false;
            var header=FullGroup(figmaRoot,"Header");
            FBox(header,"ShipIdentityPanel",31.5f,23.25f,465.75f,102);
            FText(header,"ShipNameCaption",Copy("舰船名称","SHIP NAME"),48,32.25f,260,27,20);
            blueprintIdentityText=FText(header,"BlueprintIdentity",BlueprintName,48,65,278,46,36,FLime,true);blueprintIdentityText.overflowMode=TextOverflowModes.Ellipsis;
            FText(header,"CostCaption",Copy("造价:","COST:"),329.25f,45.75f,77,30,20);
            figmaCost=FText(header,"CostValue","0",405,45.75f,85,30,20,FLime,true);
            FText(header,"PartsCaption",Copy("部件:","PARTS:"),329.25f,87.75f,80,30,20);
            figmaParts=FText(header,"PartsValue","0",409.5f,87.75f,80,30,20,FLime,true);
            figmaStatsPanel=FBox(header,"ShipStatisticsPanel",31.5f,141,465.75f,117);
            figmaStatsBody=FullGroup(header,"StatisticsBody");
            figmaStats=FText(figmaStatsBody,"ShipStatistics","",48,159,412,91,18);figmaStats.textWrappingMode=TextWrappingModes.NoWrap;figmaStats.lineSpacing=L10n.IsEnglish?6:3;
            FButton(header,"ToggleStatistics","−",468,142,25,22,()=>{figmaStatsExpanded=!figmaStatsExpanded;LayoutFigmaPanels();},18,Color.white,Color.clear);figmaStatsToggle=header.Find("ToggleStatistics/Label").GetComponent<TextMeshProUGUI>();
            placeTool=FButton(header,"PlaceTool","P",33.75f,267.75f,29.25f,27.75f,()=>{if(controller.DeleteMode)controller.ToggleDelete();},18.75f);
            deleteTool=FButton(header,"DeleteTool","X",71.25f,267.75f,29.25f,27.75f,()=>{if(!controller.DeleteMode)controller.ToggleDelete();},18.75f);
            mirrorTool=FButton(header,"MirrorTool","M",108.75f,267.75f,29.25f,27.75f,controller.ToggleMirror,18.75f);
            SetMirror(controller.MirrorMode);
            placeToolText=placeTool.GetComponentInChildren<TextMeshProUGUI>();deleteToolText=deleteTool.GetComponentInChildren<TextMeshProUGUI>();
            figmaRight=FullGroup(figmaRoot,"Toolbox");
            figmaPerformancePanel=FBox(figmaRight,"PerformancePanel",1525.5f,18.75f,369,615.75f);
            FText(figmaRight,"PerformanceHeading",Copy("舰船性能","SHIP PERFORMANCE"),1542,44.25f,330,38,22,Color.white,true);
            FButton(figmaRight,"TogglePerformance","−",1863,24,25,25,()=>{figmaPerformanceExpanded=!figmaPerformanceExpanded;LayoutFigmaPanels();},20,Color.white,Color.clear);figmaPerformanceToggle=figmaRight.Find("TogglePerformance/Label").GetComponent<TextMeshProUGUI>();
            figmaPerformanceBody=FullGroup(figmaRight,"PerformanceBody");
            var performanceScroll=FScroll(figmaPerformanceBody,"PerformanceScroll",1542,90,345,520,out var performanceContent);
            figmaPerformance=FLocalText(performanceContent,"PerformanceValues","",0,0,345,510,15);figmaPerformance.textWrappingMode=TextWrappingModes.Normal;figmaPerformance.overflowMode=TextOverflowModes.Overflow;figmaPerformance.lineSpacing=1;
            figmaDescription=FullGroup(figmaRight,"ModuleDescription");FBox(figmaDescription,"DescriptionPanel",1525.5f,111,369,521.25f);
            FText(figmaDescription,"DescriptionHeading",Copy("组件说明","MODULE DESCRIPTION"),1542,126,339,38,22,Color.white,true);FBox(figmaDescription,"DescriptionDivider",1540.5f,165,348,1.5f,new Color(1,1,1,.6f),false);
            figmaDescriptionText=FText(figmaDescription,"DescriptionText","",1543.5f,186,330,400,18);figmaDescriptionText.textWrappingMode=TextWrappingModes.Normal;
            figmaModulePanel=FBox(figmaRight,"ModuleDetailsPanel",1525.5f,643.5f,369,320.25f);
            FText(figmaRight,"ModuleDetailsHeading",Copy("组件详情","MODULE DETAILS"),1539.75f,661.5f,330,38,22,Color.white,true);FBox(figmaRight,"ModuleDivider",1539.75f,704.25f,348,1.5f,new Color(1,1,1,.6f),false);
            FButton(figmaRight,"ToggleModuleDetails","−",1866,647,25,22,()=>{figmaModuleExpanded=!figmaModuleExpanded;LayoutFigmaPanels();},18,Color.white,Color.clear);figmaModuleToggle=figmaRight.Find("ToggleModuleDetails/Label").GetComponent<TextMeshProUGUI>();
            figmaModuleBody=FullGroup(figmaRight,"ModuleDetailsBody");
            moduleNameText=FText(figmaModuleBody,"ModuleName","",1544.25f,729,325,31,22,FLime,true);
            moduleInfoScroll=FScroll(figmaModuleBody,"ModuleInfoScroll",1542,771,340,174,out var moduleContent);
            moduleStatsText=FLocalText(moduleContent,"ModuleStatistics","",0,0,330,174,19.5f);moduleStatsText.textWrappingMode=TextWrappingModes.Normal;moduleStatsText.overflowMode=TextOverflowModes.Overflow;
            FButton(figmaRight,"SaveBlueprint",Copy("保存","SAVE"),1525.5f,1009,177.75f,48,OpenSaveDialog,22,FLime);
            FButton(figmaRight,"LoadBlueprint",Copy("蓝图库","BLUEPRINTS"),1719,1009,177.75f,48,OpenLoadDialog,22);
            var footer=FullGroup(figmaRoot,"ViewportHelp");
            FButton(footer,"EnterFlightRange",Copy("试飞","TEST FLIGHT"),37.5f,1013,180.75f,44,controller.EnterFlightRange,22,Color.white,new Color32(215,80,34,255));
            FBox(footer,"WorkspaceStatusPanel",237,1013,612.75f,44);
            statusText=FText(footer,"Status","",253,1013,450,44,18,Color.white,true);statusText.alignment=TextAlignmentOptions.Left;statusText.overflowMode=TextOverflowModes.Ellipsis;
            rotationReadoutText=FText(footer,"RotationReadout","ROTATE 0 · R",711,1013,126.75f,44,16,new Color(1,1,1,.71f));rotationReadoutText.alignment=TextAlignmentOptions.Right;
            FButton(footer,"AssemblyWeaponGroups",Copy("武器分组","WEAPON GROUPS"),860.25f,1013,239.25f,44,ToggleWeaponGroups,20,FLime);figmaGroupsLabel=footer.Find("AssemblyWeaponGroups/Label").GetComponent<TextMeshProUGUI>();
            figmaHelp=FText(footer,"BuildHelp","",37.5f,960.75f,1000,44,16.5f);figmaHelp.textWrappingMode=TextWrappingModes.Normal;
            // Existing dialog and placement APIs retain their hidden backing fields.
            var hidden=FullGroup(figmaRoot,"EditorData");hidden.gameObject.SetActive(false);
            countText=FText(hidden,"Count","",0,0,10,10,12);assemblyCostText=FText(hidden,"AssemblyCost","",0,0,10,10,12);
            modeText=FText(hidden,"Mode","",0,0,10,10,12);headerMode=FText(hidden,"HeaderMode","",0,0,10,10,12);shapeText=FText(hidden,"Shape","",0,0,10,10,12);coordinateText=FText(hidden,"Coordinates","",0,0,10,10,12);rotationText=FText(hidden,"Rotation","",0,0,10,10,12);idText=FText(hidden,"ID","",0,0,10,10,12);
            BuildDialogs(figmaRoot);
            moduleCatalogue=gameObject.AddComponent<AssemblyModuleCatalogue>();moduleCatalogue.Initialize(controller,this);
            weaponOverlay=gameObject.AddComponent<AssemblyWeaponGroups>();weaponOverlay.Initialize(controller,this);
            initialized=true;RefreshFigmaCopy();LayoutFigmaPanels();ResizeTarget();Canvas.ForceUpdateCanvases();
            foreach(var child in go.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
        }

        public void ToggleCatalogue()
        {
            if(IsDialogOpen)return;if(IsWeaponGroupsOpen)weaponOverlay.Close();moduleCatalogue.SetOpen(!IsCatalogueOpen);
            if(IsCatalogueOpen){figmaStatsExpanded=false;figmaPerformanceExpanded=false;}LayoutFigmaPanels();RefreshFigmaCopy();
        }
        public void ToggleWeaponGroups()
        {
            if(IsDialogOpen)return;if(IsCatalogueOpen)moduleCatalogue.SetOpen(false);
            if(IsWeaponGroupsOpen)weaponOverlay.Close();else weaponOverlay.Open();LayoutFigmaPanels();RefreshFigmaCopy();
        }
        public bool ConsumeEditorEscape()
        {
            if(IsCatalogueOpen){ToggleCatalogue();return true;}
            if(IsWeaponGroupsOpen){if(!weaponOverlay.CancelDrag())ToggleWeaponGroups();return true;}return false;
        }
        public bool WorkspaceContains(Vector2 screen)
        {
            if(IsDialogOpen||!figmaRoot)return false;
            if(!RectTransformUtility.RectangleContainsScreenPoint(figmaRoot,screen,presentationCamera))return false;
            if(EventSystem.current){
                var results=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=screen},results);
                foreach(var result in results)if(result.module is GraphicRaycaster)return false;
            }
            return true;
        }
        void LayoutFigmaPanels()
        {
            if(!figmaRoot)return;
            figmaStatsBody.gameObject.SetActive(figmaStatsExpanded);SetFRect(figmaStatsPanel.rectTransform,31.5f,141,465.75f,figmaStatsExpanded?117:24);figmaStatsToggle.text=figmaStatsExpanded?"−":"+";
            figmaRight.gameObject.SetActive(!IsWeaponGroupsOpen);
            SetFRect(figmaPerformancePanel.rectTransform,1525.5f,18.75f,369,figmaPerformanceExpanded?615.75f:73.5f);figmaPerformanceBody.gameObject.SetActive(figmaPerformanceExpanded);figmaPerformanceToggle.text=figmaPerformanceExpanded?"−":"+";
            figmaDescription.gameObject.SetActive(IsCatalogueOpen&&!figmaPerformanceExpanded);
            SetFRect(figmaModulePanel.rectTransform,1525.5f,643.5f,369,figmaModuleExpanded?320.25f:69);figmaModuleBody.gameObject.SetActive(figmaModuleExpanded);figmaModuleToggle.text=figmaModuleExpanded?"−":"+";
        }
        void RefreshFigmaMode(bool delete)
        {
            if(placeTool)((Image)placeTool.targetGraphic).color=delete?new Color32(26,39,67,255):new Color32(54,70,77,255);
            if(deleteTool)((Image)deleteTool.targetGraphic).color=delete?new Color32(215,80,34,255):new Color32(26,39,67,255);
            if(placeToolText)placeToolText.text="P";if(deleteToolText)deleteToolText.text="X";
        }
        public void SetMirror(bool enabled)
        {
            if(mirrorTool)((UnityEngine.UI.Image)mirrorTool.targetGraphic).color=enabled?new Color32(118,177,49,255):new Color32(26,39,67,255);
        }
        void RefreshFigmaModule()
        {
            if(!figmaRoot||!moduleStatsText)return;var d=GetDefinition(localizedShape);
            moduleNameText.text=ShipEditorController.ShapeName(localizedShape).ToUpperInvariant();moduleNameText.color=FLime;FigmaVisuals.SetFont(moduleNameText,true);
            bool thruster=BlockModules.IsThruster(localizedShape);
            bool powerModule=d&&d.generationWatts>0&&d.storageJoules>0;
            UiStyle.ApplyText(moduleStatsText,thruster?15.5f:powerModule?16:19.5f);FigmaVisuals.SetFont(moduleStatsText,true);
            if(d){
                var mission=CurrentAssemblyMission();float moduleCost=mission?mission.CostFor(d):d.cost;
                var effective=flightTuning?flightTuning.Get(localizedShape):d.Stats;
                var rows=new System.Collections.Generic.List<string>{Copy($"造价   {moduleCost:0}",$"COST   {moduleCost:0}"),Copy($"耐久   {d.maxHp:0}",$"HP     {d.maxHp:0}"),Copy($"质量   {effective.massKg:0} KG",$"MASS   {effective.massKg:0} KG"),Copy($"格数   {BlockModules.LocalCells(localizedShape).Length}",$"CELLS  {BlockModules.LocalCells(localizedShape).Length}"),Copy($"恢复   {d.recoverySeconds:0.#} S",$"REGEN  {d.recoverySeconds:0.#} S")};
                if(thruster&&flightTuning){
                    rows.Add(Copy($"推力   {effective.thrustNewtons/1000:0.#} KN",$"THRUST {effective.thrustNewtons/1000:0.#} KN"));
                    rows.Add(Copy($"持续   {flightTuning.PropulsionPowerDemandWatts:0} W（整舰）",$"SUSTAINED {flightTuning.PropulsionPowerDemandWatts:0} W (SHIP)"));
                    rows.Add(Copy($"加力   {flightTuning.BoostPowerDemandWatts:0} W（整舰）",$"BOOST {flightTuning.BoostPowerDemandWatts:0} W (SHIP)"));
                }
                if(d.generationWatts>0)rows.Add(Copy($"发电   {d.generationWatts:0} W",$"OUTPUT {d.generationWatts:0} W"));
                if(d.storageJoules>0)rows.Add(Copy($"储电   {d.storageJoules:0} J",$"CAPACITY {d.storageJoules:0} J"));
                moduleStatsText.text=string.Join("\n",rows);
                if(d.weapon)moduleStatsText.text+=Copy($"\n射程   {d.weapon.range:0} M\n射速   {d.weapon.shotsPerSecond:0.#}/S",$"RANGE  {d.weapon.range:0} M\nRATE   {d.weapon.shotsPerSecond:0.#}/S");
                figmaDescriptionText.text=ShipEditorController.ShapeName(localizedShape)+"\n\n"+BuildRefinedModuleSummary(d,effective,effective.massKg,d.maxHp);
            }
            moduleStatsText.lineSpacing=thruster||powerModule?0:5;RefreshModuleInfoHeight();
            if(moduleCatalogue)moduleCatalogue.Highlight(localizedShape);
        }
        void RefreshFigmaStats()
        {
            if(!figmaRoot||cachedShipPerformance==null)return;var p=cachedShipPerformance;string green(string v)=>"<color=#B9FF03>"+v+"</color>";
            var metrics=Starframe.App.MenuShipMetrics.Calculate(controller.Assembly.Export(BlueprintName),flightTuning,CurrentAssemblyMission());
            figmaCost.text=metrics.Cost.ToString("0");figmaParts.text=controller.Assembly.Count.ToString();
            float mobility=metrics.Mobility,firepower=metrics.Firepower;
            figmaStats.text=Copy($"部件: {green(controller.Assembly.Count.ToString())}     耐久: {green(p.totalHp.ToString("0"))}\n质量: {green((p.totalMass/1000).ToString("0.0")+"t")}    机动: {green(mobility.ToString("0"))}\n造价: {green(metrics.Cost.ToString("0"))}     火力: {green(firepower.ToString("0"))}",$"PARTS: {green(controller.Assembly.Count.ToString())}    HEALTH: {green(p.totalHp.ToString("0"))}\nMASS: {green((p.totalMass/1000).ToString("0.0")+"t")}    MOBILITY: {green(mobility.ToString("0"))}\nCOST: {green(metrics.Cost.ToString("0"))}    FIREPOWER: {green(firepower.ToString("0"))}");
            var ap=p.accelerationPositive/9.80665f;var an=p.accelerationNegative/9.80665f;var rates=WeakerAxes(p.angularRatePositive,p.angularRateNegative);var angular=WeakerAxes(p.angularAccelerationPositive,p.angularAccelerationNegative);
            figmaPerformance.text=$"MASS {green((p.totalMass/1000).ToString("0.0")+"t")} · HP {green(p.totalHp.ToString("N0"))}\nOUTPUT {green(cachedGeneration.ToString("N0")+" W")} · CAPACITY {green(cachedEnergyCapacity.ToString("N0")+" J")}\n\nPOWER CONSUMPTION\n\n<size=90%>IDLE {green("0 W")} · SUSTAINED {green((cachedWeaponDemand+cachedPropulsionDemand).ToString("N0")+" W")}\nPEAK {green((cachedWeaponDemand+cachedShieldDemand+(flightTuning?flightTuning.BoostPowerDemandWatts:0)).ToString("N0")+" W")}</size>\n\nSPEED\n\n<size=90%>CAP {green(p.maxSpeed.ToString("0")+" m/s")} · BOOST x {green(flightTuning.boostMultiplier.ToString("0.#"))}</size>\n\nDIRECTION · ACCEL / BRAKE (g)\n\n<size=90%>FWD {green($"{ap.z:0.00}/{an.z:0.00}")} AFT {green($"{an.z:0.00}/{ap.z:0.00}")}\nLEFT {green($"{an.x:0.00}/{ap.x:0.00}")} RIGHT {green($"{ap.x:0.00}/{an.x:0.00}")}\nUP {green($"{ap.y:0.00}/{an.y:0.00}")} · DOWN {green($"{an.y:0.00}/{ap.y:0.00}")}</size>\n\nROTATION · RATE / ACCELERATION\n\n<size=90%>PITCH {green($"{rates.x:0.0} °/s · {angular.x:0.0} °/s²")}\nYAW {green($"{rates.y:0.0} °/s · {angular.y:0.0} °/s²")}\nROLL {green($"{rates.z:0.0} °/s · {angular.z:0.0} °/s²")}\nFULL-SPEED STOP F/A {green(PerformanceDistance(p.maxSpeedStoppingDistancePositive.z)+"/"+PerformanceDistance(p.maxSpeedStoppingDistanceNegative.z)+" m")}\nTURN RADIUS P/Y {green(PerformanceDistance(p.turningRadiusAtMaxSpeed.x)+"/"+PerformanceDistance(p.turningRadiusAtMaxSpeed.y)+" m")}</size>";
            if(!L10n.IsEnglish)figmaPerformance.text=Starframe.App.MenuShipMetrics.Detail(metrics);
            var content=figmaPerformance.rectTransform.parent as RectTransform;float h=Mathf.Max(510,figmaPerformance.GetPreferredValues(figmaPerformance.text,345,float.PositiveInfinity).y+8);content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,h);figmaPerformance.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,h);
        }
        void RefreshFigmaCopy()
        {
            if(!figmaRoot)return;
            figmaGroupsLabel.text=IsWeaponGroupsOpen?Copy("返回组装","BACK TO ASSEMBLY"):Copy("武器分组","WEAPON GROUPS");
            figmaHelp.text=IsCatalogueOpen?Copy("<color=#B9FF03>SHIFT</color> 移动视角 · <color=#B9FF03>SPACE</color> 返回组装 · 点击选择组件","<color=#B9FF03>SHIFT</color> Move camera · <color=#B9FF03>SPACE</color> back to assembly · Click select module"):
                IsWeaponGroupsOpen?Copy("<color=#B9FF03>SHIFT</color> 移动视角 · <color=#B9FF03>左键</color> 选择并拖动到分组\n<color=#B9FF03>双击</color> 选择同类武器 · <color=#B9FF03>ESC</color> 取消连线", "<color=#B9FF03>SHIFT</color> Move camera · <color=#B9FF03>LEFT CLICK</color> Select and drag to group\n<color=#B9FF03>DOUBLE CLICK</color> Select all same type · <color=#B9FF03>ESC</color> Cancel link"):
                Copy("<color=#B9FF03>SHIFT</color> 移动视角 · <color=#B9FF03>X</color> 删除 · <color=#B9FF03>R</color> 旋转 · <color=#B9FF03>M</color> 镜像\n<color=#B9FF03>SPACE</color> 组件目录 · <color=#B9FF03>ALT</color> 复制组件", "<color=#B9FF03>SHIFT</color> Move camera · <color=#B9FF03>X</color> Delete mode · <color=#B9FF03>R</color> Rotate · <color=#B9FF03>M</color> Mirror\n<color=#B9FF03>SPACE</color> select module · <color=#B9FF03>ALT</color> Copy module");
            FigmaVisuals.SetFont(blueprintIdentityText,true);FigmaVisuals.SetFont(figmaHelp);UiStyle.ApplyText(figmaHelp,L10n.IsEnglish?16.5f:14.5f);UiStyle.ApplyText(figmaStats,18);figmaStats.lineSpacing=L10n.IsEnglish?6:3;RefreshFigmaMode(controller.DeleteMode);RefreshFigmaModule();RefreshFigmaStats();
        }
        Starframe.App.MissionDefinition CurrentAssemblyMission()
        {
            if(!assemblyMissions){var loaded=Resources.FindObjectsOfTypeAll<Starframe.App.MissionCatalog>();if(loaded.Length>0)assemblyMissions=loaded[0];}
            if(!assemblyMissions)return null;var mission=assemblyMissions.Find(Starframe.App.AppNavigation.SelectedMissionId);
            return mission?mission:assemblyMissions.missions!=null&&assemblyMissions.missions.Count>0?assemblyMissions.missions[0]:null;
        }
        internal Image FBox(RectTransform parent,string name,float x,float y,float w,float h,Color? tint=null,bool raycast=true)
        {var i=MakeRect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();i.color=tint??FPanel;i.raycastTarget=raycast;return i;}
        internal TextMeshProUGUI FText(RectTransform parent,string name,string value,float x,float y,float w,float h,float size,Color? tint=null,bool bold=false)
        {var t=Label(parent,name,value,x,y,w,h,size,tint??Color.white);t.alignment=TextAlignmentOptions.TopLeft;t.overflowMode=TextOverflowModes.Overflow;FigmaVisuals.SetFont(t,bold);return t;}
        TextMeshProUGUI FLocalText(RectTransform parent,string name,string value,float x,float y,float w,float h,float size)
        {var t=LocalLabel(parent,name,value,x,y,w,h,size,Color.white,TextAlignmentOptions.TopLeft);FigmaVisuals.SetFont(t);return t;}
        internal Button FButton(RectTransform parent,string name,string value,float x,float y,float w,float h,Action action,float size=19.5f,Color? textColor=null,Color? backgroundColor=null)
        {
            var rect=MakeRect(parent,name,x,y,w,h);var image=rect.gameObject.AddComponent<Image>();image.color=backgroundColor??FPanel;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.navigation=new Navigation{mode=Navigation.Mode.None};button.colors=UiStyle.ControlColors();button.onClick.AddListener(()=>{Starframe.App.GameAudio.Click();action?.Invoke();});
            float pad=w<40?2:8;
            var label=FLocalText(rect,"Label",value,pad,2,w-pad*2,h-4,size);label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=new Vector2(pad,2);label.rectTransform.offsetMax=new Vector2(-pad,-2);label.color=textColor??Color.white;label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;FigmaVisuals.SetFont(label,true);return button;
        }
        ScrollRect FScroll(RectTransform parent,string name,float x,float y,float w,float h,out RectTransform content)
        {
            var rect=MakeRect(parent,name,x,y,w,h);var scroll=rect.gameObject.AddComponent<ScrollRect>();
            var viewport=LocalRect(rect,"Viewport",0,0,w,h);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=viewport.offsetMax=Vector2.zero;viewport.gameObject.AddComponent<RectMask2D>();var image=viewport.gameObject.AddComponent<Image>();image.color=Color.clear;image.canvasRenderer.cullTransparentMesh=false;
            content=LocalRect(viewport,"Content",0,0,w,h);content.anchorMin=content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,1);content.anchoredPosition=Vector2.zero;
            scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=28;return scroll;
        }
        static void SetFRect(RectTransform rect,float x,float y,float w,float h)
        {rect.anchorMin=new Vector2(x/1920,(1080-y-h)/1080);rect.anchorMax=new Vector2((x+w)/1920,(1080-y)/1080);rect.offsetMin=rect.offsetMax=Vector2.zero;}
    }
}
