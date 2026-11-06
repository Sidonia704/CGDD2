// @role Present the Figma combat instruments with live per-mount readiness, heat, reloads, target states and peripheral ship schematics.
// @calls RangeCombatWorld, ShipFlightController, ShipWeapons, ShipStatusGraphic, CombatWeaponStatusGraphic, FlightInstrumentGraphic, GatlingHeatGraphic, SphereRadarGraphic, TargetAimGraphic, TargetSilhouetteOutline, ShipCountermeasureController, ReticleHitFeedback, FigmaVisuals, UiStyle, UiTheme, GameSettings

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;
using Starframe.Presentation;
namespace Starframe.Flight
{
    public sealed class RangeHUD:MonoBehaviour
    {
        ShipFlightController flight;Camera worldCamera,displayCamera;RenderTexture sceneTexture;
        Starframe.Combat.RangeCombatWorld combat;
        TMP_FontAsset font;Canvas canvas;GameObject modal;
        TextMeshProUGUI info,lockLabel,leadLabel,energyLabel,boostLabel;RawImage energyFill;double nextTelemetry;readonly List<RawImage> glass=new List<RawImage>();readonly List<Material> materials=new List<Material>();
        const double TelemetryInterval=1.0/60.0;
        readonly List<TextMeshProUGUI> targetLabels=new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> targetDistances=new List<TextMeshProUGUI>();
        readonly HashSet<RectTransform> scaledHudElements=new HashSet<RectTransform>();
        readonly List<string> leadNames=new List<string>(8);
        sealed class WeaponGroupHudEntry{public int id;public Image tile;public TextMeshProUGUI label;}
        readonly List<WeaponGroupHudEntry> weaponGroupHud=new List<WeaponGroupHudEntry>();
        Starframe.Combat.TargetAimGraphic targetAimGraphic;
        FlightAimGraphic flightAim;
        ShipStatusGraphic ownStatus,targetStatus;SphereRadarGraphic radar;
        TextMeshProUGUI ownCondition,targetIdentity,targetCondition,radarLabel,shieldLabel;
        RawImage shieldFill;
        TextMeshProUGUI missileLabel,flareLabel,threatLabel,cadenceLabel;
        UnityEngine.UI.Image missileLockTrack,missileLockFill;
        TextMeshProUGUI speedReadout,summaryReadout,diagnosticHint,alertReadout;
        RawImage telemetryPanel;RectTransform telemetryShadow;
        bool refinedFlightLayout,diagnosticsExpanded;
        FlightInstrumentGraphic instruments;TextMeshProUGUI instrumentShield,instrumentEnergy;
        GatlingHeatGraphic gatlingHeat;TextMeshProUGUI gatlingHeatValue,gatlingHeatState;
        CombatWeaponStatusGraphic weaponStatus;
        const float EnergyWidth=316,TelemetryWidth=360;
        static readonly Color HudInk=Color.white,HudMuted=new Color(.84f,.84f,.84f,.94f);
        bool inputOverlayOpen,missionPresentation;int ignoreEscapeThroughFrame=-1;
        public Canvas RootCanvas=>canvas;
        public Camera WorldCamera=>worldCamera;
        public bool TryGetAimCursorScreenPoint(out Vector2 point)
        {
            point=default;return flightAim&&flightAim.TryGetCursorScreenPoint(out point);
        }
        public bool TryGetForwardReticleScreenPoint(out Vector2 point)
        {
            point=default;return flightAim&&flightAim.TryGetForwardScreenPoint(out point);
        }
        public bool InputOverlayOpen{get=>inputOverlayOpen;set{inputOverlayOpen=value;ignoreEscapeThroughFrame=Time.frameCount+1;}}
        public bool ExitDialogOpen=>inputOverlayOpen||(modal&&modal.activeSelf);
        public event Action ExitConfirmed;
        public event Action SettingsRequested,PracticeOptionsRequested;
        public void ConfigureMissionPresentation()
        {
            missionPresentation=true;
            if(!canvas||!modal)return;
            var root=canvas.transform;
            var options=modal.transform.Find("RangeOptions");if(options)options.gameObject.SetActive(false);
            var optionsText=modal.transform.Find("RangeOptionsText");if(optionsText)optionsText.gameObject.SetActive(false);
            var title=modal.transform.Find("ExitTitle")?.GetComponent<TMP_Text>();if(title)L10n.Bind(title,"mission.arcade.exitTitle");
            var hint=modal.transform.Find("ExitHint")?.GetComponent<TMP_Text>();if(hint)L10n.Bind(hint,"mission.arcade.exitHint");
            var confirm=modal.transform.Find("ConfirmExitText")?.GetComponent<TMP_Text>();if(confirm)L10n.Bind(confirm,"mission.arcade.exitConfirm");
            var settings=modal.transform.Find("RangeSettings");if(settings)SetRect((RectTransform)settings,636,530,648,50);
            var settingsText=modal.transform.Find("RangeSettingsText");if(settingsText)SetRect((RectTransform)settingsText,636,537,648,36);
            if(!refinedFlightLayout){var heading=root.Find("Title")?.GetComponent<TMP_Text>();if(heading)L10n.Bind(heading,"mission.arcade.title");}
        }
        internal RectTransform CreateMissionGlass(RectTransform parent,string name,float x,float y,float width,float height)
        {
            Panel(parent,name,x,y,width,height);
            return parent.Find(name).GetComponent<RectTransform>();
        }
        public void NotifyWeaponHit(bool scenery=false){if(flightAim&&!ExitDialogOpen)flightAim.NotifyWeaponHit(scenery);}
        public void BindCombat(Starframe.Combat.RangeCombatWorld value)
        {
            combat=value;
            if(!combat||!canvas||!displayCamera||!worldCamera)return;
            var root=(RectTransform)canvas.transform;
            var silhouette=Rect(root,"LockedTargetSilhouette",0,0,1920,1080).gameObject.AddComponent<Starframe.Combat.TargetSilhouetteOutline>();
            silhouette.Initialize(worldCamera,combat.Targeting);silhouette.transform.SetSiblingIndex(root.Find("FlightView").GetSiblingIndex()+1);
            leadLabel=Text(root,"LeadLegend","",refinedFlightLayout?660:1410,refinedFlightLayout?116:393,refinedFlightLayout?600:478,refinedFlightLayout?24:42,refinedFlightLayout?13:14);leadLabel.alignment=refinedFlightLayout?TextAlignmentOptions.Center:TextAlignmentOptions.TopRight;leadLabel.textWrappingMode=TextWrappingModes.Normal;
            ownStatus.SetMonochrome(true);targetStatus.SetMonochrome(true);ownStatus.Bind(combat.Player.ship);radar.Initialize(combat,combat.Sensors!=null?combat.Sensors.MaximumRange:combat.Targeting.LockRange);
            targetStatus.SetRelativeObserver(combat.Player.ship.transform);
            var overlay=new GameObject("TargetAimCanvas",typeof(RectTransform),typeof(Canvas));overlay.layer=5;overlay.transform.SetParent(canvas.transform,false);
            var targetCanvas=overlay.GetComponent<Canvas>();targetCanvas.renderMode=RenderMode.ScreenSpaceCamera;targetCanvas.worldCamera=displayCamera;targetCanvas.planeDistance=1;
            var rect=(RectTransform)overlay.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var graphicObject=new GameObject("TargetAim",typeof(RectTransform),typeof(CanvasRenderer));graphicObject.layer=5;graphicObject.transform.SetParent(overlay.transform,false);
            var graphicRect=(RectTransform)graphicObject.transform;graphicRect.anchorMin=Vector2.zero;graphicRect.anchorMax=Vector2.one;graphicRect.offsetMin=graphicRect.offsetMax=Vector2.zero;
            targetAimGraphic=graphicObject.AddComponent<Starframe.Combat.TargetAimGraphic>();targetAimGraphic.Initialize(worldCamera,combat.Targeting,combat.Player.weapons);
            overlay.transform.SetSiblingIndex(modal.transform.GetSiblingIndex());modal.transform.SetAsLastSibling();
            BuildWeaponGroupHud(root,combat.Player.weapons);
            if(refinedFlightLayout){weaponStatus=Rect(root,"WeaponMountStatus",348.75f,414,282,243).gameObject.AddComponent<CombatWeaponStatusGraphic>();weaponStatus.Bind(combat.Player.ship,combat.Player.weapons,combat.Player.countermeasures);}
            float missileX=refinedFlightLayout?407:510,missileY=refinedFlightLayout?640:116;
            missileLabel=Text(root,"MissileStatus","",missileX,missileY,680,24,14);
            if(refinedFlightLayout)missileLabel.alignment=TextAlignmentOptions.Center;
            missileLockTrack=Rect(root,"MissileLockTrack",refinedFlightLayout?840:510,missileY+30,240,3).gameObject.AddComponent<UnityEngine.UI.Image>();
            missileLockTrack.raycastTarget=false;missileLockTrack.color=new Color(Starframe.Presentation.UiTheme.Current.amber.r,Starframe.Presentation.UiTheme.Current.amber.g,Starframe.Presentation.UiTheme.Current.amber.b,.18f);
            missileLockFill=Rect(root,"MissileLockFill",refinedFlightLayout?840:510,missileY+30,240,3).gameObject.AddComponent<UnityEngine.UI.Image>();
            missileLockFill.raycastTarget=false;missileLockFill.rectTransform.SetParent(missileLockTrack.rectTransform,false);SetProgress(missileLockFill.rectTransform,0);
            RefreshMissileHud(combat.Player.weapons);
            if(refinedFlightLayout){missileLabel.gameObject.SetActive(false);missileLockTrack.gameObject.SetActive(false);missileLockFill.gameObject.SetActive(false);}
            flareLabel=Text(root,"CountermeasureStatus","",refinedFlightLayout?348:510,refinedFlightLayout?660:166,refinedFlightLayout?282:900,26,14);flareLabel.color=HudMuted;if(refinedFlightLayout)flareLabel.gameObject.SetActive(false);
            threatLabel=Text(root,"ThreatStatus","",refinedFlightLayout?620:510,refinedFlightLayout?44:204,refinedFlightLayout?680:900,30,18);threatLabel.color=UiTheme.Current.danger;if(refinedFlightLayout)threatLabel.alignment=TextAlignmentOptions.Center;
            modal.transform.SetAsLastSibling();
            ApplyHudElementScale();
        }
        public void Initialize(Camera camera,ShipFlightController controller,FlightTuning tuning)
        {
            flight=controller;worldCamera=camera;worldCamera.cullingMask&=~(1<<5);
            refinedFlightLayout=Starframe.Presentation.UiTheme.Current.flightLayout;
            font=L10n.CurrentFont;
            L10n.Changed+=RefreshLocale;
            Starframe.App.GameSettings.Changed+=RefreshVisualPreferences;
            var presenter=new GameObject("Flight HUD Camera");presenter.transform.SetParent(transform,false);
            displayCamera=presenter.AddComponent<Camera>();displayCamera.orthographic=true;displayCamera.orthographicSize=5.4f;displayCamera.cullingMask=1<<5;displayCamera.depth=10;
            displayCamera.clearFlags=CameraClearFlags.SolidColor;displayCamera.backgroundColor=Color.black;displayCamera.nearClipPlane=.1f;displayCamera.farClipPlane=10;displayCamera.allowHDR=false;
            var data=presenter.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.renderPostProcessing=false;
            var go=new GameObject("FlightRangeCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            go.layer=5;go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=displayCamera;canvas.planeDistance=1;
            var scale=go.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;UiStyle.ApplyScaler(scale);
            if(!FindFirstObjectByType<EventSystem>()){var events=new GameObject("FlightEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));events.transform.SetParent(transform,false);events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();}
            var root=(RectTransform)go.transform;
            var background=Rect(root,"FlightView",0,0,1920,1080).gameObject.AddComponent<RawImage>();background.raycastTarget=false;glass.Add(background);
            flightAim=Rect(root,"FlightAim",0,0,1920,1080).gameObject.AddComponent<FlightAimGraphic>();flightAim.Initialize(worldCamera,flight);
            if(refinedFlightLayout)BuildRefinedFlightLayout(root);
            else BuildLegacyFlightLayout(root);
            if(!refinedFlightLayout)BuildGatlingHeatHud(root);
            int radarTop=refinedFlightLayout?24:28;float radarHeight=refinedFlightLayout?240:312;
            Panel(root,"RadarGlass",refinedFlightLayout?1602:1576,radarTop,refinedFlightLayout?282:312,radarHeight);
            var radarTitle=Text(root,"RadarTitle",refinedFlightLayout?"RADAR":"hud.radarLegacyTitle",refinedFlightLayout?1740:1596,radarTop,refinedFlightLayout?144:272,27,refinedFlightLayout?21:17);if(refinedFlightLayout)radarTitle.alignment=TextAlignmentOptions.Right;
            radar=Rect(root,"SphericalRadar",refinedFlightLayout?1602:1608,refinedFlightLayout?92:radarTop+45,refinedFlightLayout?282:248,refinedFlightLayout?143:225).gameObject.AddComponent<SphereRadarGraphic>();
            radarLabel=Text(root,"RadarContacts","hud.radarInitial",refinedFlightLayout?1602:1596,refinedFlightLayout?264:radarTop+280,refinedFlightLayout?282:272,22,13);if(refinedFlightLayout){radarLabel.color=HudMuted;radarLabel.alignment=TextAlignmentOptions.Right;HudBrackets(root,"RadarBrackets",1602,81.75f,282,175.5f);}
            if(refinedFlightLayout)
            {
                var controls=Text(root,"ControlsCombat","hud.combatHelp",420,1048,1080,22,13);controls.alignment=TextAlignmentOptions.Center;controls.color=HudMuted;controls.gameObject.SetActive(false);
            }
            else Text(root,"Controls","hud.allHelp",32,1030,1830,28,15);
            var group=Rect(root,"ExitModal",0,0,1920,1080);modal=group.gameObject;
            var dim=Rect(group,"ModalBlocker",0,0,1920,1080).gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,.45f);
            Panel(group,"ExitGlass",600,342,720,396);
            Text(group,"ExitTitle","hud.exitTitle",636,375,648,45,25);
            Text(group,"ExitHint","hud.exitHint",636,434,648,68,17);
            Button(group,"RangeOptions","app.rangeOptions",636,530,310,50,()=>PracticeOptionsRequested?.Invoke());
            Button(group,"RangeSettings","app.settings",974,530,310,50,()=>SettingsRequested?.Invoke());
            Button(group,"CancelExit","hud.continue",636,632,310,50,()=>CloseExit(),refinedFlightLayout);
            Button(group,"ConfirmExit","hud.returnAssembly",974,632,310,50,()=>{CloseExit();ExitConfirmed?.Invoke();});
            // These values are composed by telemetry each tick, including locale changes.
            // Their initial placeholders must not overwrite live values on a language switch.
            foreach(var label in new TMP_Text[]{ownCondition,targetIdentity,targetCondition,radarLabel,shieldLabel,energyLabel,lockLabel,summaryReadout})
                if(label)L10n.Unbind(label);
            modal.SetActive(false);Resize();Canvas.ForceUpdateCanvases();
            foreach(var child in go.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
            ApplyHudElementScale();
        }
        void BuildLegacyFlightLayout(RectTransform root)
        {
            Panel(root,"TelemetryGlass",32,32,450,348);
            Text(root,"Title","hud.rangeTitle",55,49,382,35,23);
            info=Text(root,"Telemetry","",55,94,396,275,15);info.textWrappingMode=TextWrappingModes.Normal;
            lockLabel=Text(root,"TargetLockStatus","hud.lockInitial",1410,353,478,30,17);lockLabel.alignment=TextAlignmentOptions.Right;
            Panel(root,"OwnStatusGlass",32,618,360,266);Text(root,"OwnStatusTitle","hud.ownTitle",54,634,316,28,18);
            ownStatus=Rect(root,"OwnShipSchematic",52,670,320,173).gameObject.AddComponent<ShipStatusGraphic>();
            ownCondition=Text(root,"OwnShipCondition","hud.coreOnline",54,852,316,22,14);
            Panel(root,"EnergyGlass",32,894,360,128);
            shieldLabel=Text(root,"ShieldValue","hud.shieldInitial",54,904,316,25,14);
            CreateShieldFill(root,54,932,EnergyWidth,10);
            energyLabel=Text(root,"EnergyValue","hud.energyInitial",54,944,316,25,14);
            CreateEnergyFill(root,54,976,EnergyWidth,14);
            boostLabel=Text(root,"BoostStatus","hud.boost",54,996,316,20,13);boostLabel.color=new Color(.52f,.88f,1,.95f);boostLabel.gameObject.SetActive(false);
            BuildTargetMonitor(root,1528,726,360,296,1550,742,1548,809,1550,990);
        }
        void BuildRefinedFlightLayout(RectTransform root)
        {
            telemetryShadow=Panel(root,"TelemetryGlass",32,300,360,180);
            telemetryPanel=root.Find("TelemetryGlass")?.GetComponent<RawImage>();
            diagnosticHint=Text(root,"DiagnosticHint","hud.diagnosticsOpen",32,300,310,24,12);diagnosticHint.gameObject.SetActive(false);
            info=Text(root,"Telemetry","",32,332,360,188,12);info.textWrappingMode=TextWrappingModes.Normal;info.gameObject.SetActive(false);
            summaryReadout=Text(root,"FlightSummary","",32,300,316,45,14);summaryReadout.gameObject.SetActive(false);
            alertReadout=Text(root,"FlightAlert","",720,95,480,26,16);alertReadout.alignment=TextAlignmentOptions.Center;alertReadout.gameObject.SetActive(false);
            lockLabel=Text(root,"TargetLockStatus","hud.lockHint",720,68,480,26,15);lockLabel.color=HudMuted;lockLabel.alignment=TextAlignmentOptions.Center;lockLabel.gameObject.SetActive(false);
            Panel(root,"OwnStatusGlass",1309.5f,421.5f,260,210);
            Text(root,"OwnStatusTitle",L10n.IsEnglish?"SHIP STATUS":"舰船状态",1309.5f,421.5f,260,30,21);
            ownCondition=Text(root,"OwnShipCondition","",1311,447,260,23,16.5f);
            ownStatus=Rect(root,"OwnShipSchematic",1329.75f,489,222,131.25f).gameObject.AddComponent<ShipStatusGraphic>();
            HudBrackets(root,"OwnShipBrackets",1314.75f,473.25f,252,158.25f);
            BuildTargetMonitor(root,45,30.75f,282,233,45,30.75f,60.75f,93.75f,45,268,14,146.25f,21,29);
            var targetTitle=root.Find("TargetStatusTitle")?.GetComponent<TMP_Text>();if(targetTitle){L10n.Unbind(targetTitle);targetTitle.text=L10n.IsEnglish?"TARGET STATUS":"目标状态";}
            HudBrackets(root,"TargetShipBrackets",45.75f,78,282,185.25f);
            var targetRect=targetStatus.rectTransform;SetRect(targetRect,60.75f,93.75f,247.5f,146.25f);
            targetCondition.gameObject.SetActive(false);
            // Retain telemetry components for the legacy path and diagnostics; the graphical instruments carry the flight view.
            shieldLabel=Text(root,"ShieldValue","",0,0,1,1,14);shieldLabel.gameObject.SetActive(false);CreateShieldFill(root,0,0,1,1);root.Find("ShieldTrack").gameObject.SetActive(false);
            energyLabel=Text(root,"EnergyValue","",0,0,1,1,14);energyLabel.gameObject.SetActive(false);CreateEnergyFill(root,0,0,1,1);root.Find("EnergyTrack").gameObject.SetActive(false);
            boostLabel=Text(root,"BoostStatus","",0,0,1,1,12);boostLabel.gameObject.SetActive(false);
            instruments=Rect(root,"FlightInstruments",0,0,1920,1080).gameObject.AddComponent<FlightInstrumentGraphic>();instruments.raycastTarget=false;
            speedReadout=Text(root,"SpeedReadout","0",645,663,64,35,27);speedReadout.alignment=TextAlignmentOptions.Center;
            var unit=Text(root,"SpeedUnit","m/s",645,699,64,19,13.5f);unit.alignment=TextAlignmentOptions.Center;
            instrumentShield=Text(root,"ShieldGaugeValue","",1160,663,64,27,18);instrumentShield.alignment=TextAlignmentOptions.Center;
            instrumentEnergy=Text(root,"EnergyGaugeValue","",1218.75f,663,64,27,18);instrumentEnergy.alignment=TextAlignmentOptions.Center;
            string[] titles={"FLARE","MISSILE","LASER","AMMO","GROUPS"};float[] titleX={351.75f,409.5f,464.25f,519,577.5f};
            for(int i=0;i<titles.Length;i++)Text(root,"WeaponColumnTitle"+i,titles[i],titleX[i],392.25f,58,18,13.5f);
            BuildHudIcons(root);
            SetDiagnosticsExpanded(false);
        }
        void HudBrackets(RectTransform root,string name,float x,float y,float width,float height)
        {
            Color tint=new Color32(185,255,3,255);const float arm=15.75f,stroke=2.25f;
            for(int sx=0;sx<2;sx++)for(int sy=0;sy<2;sy++)
            {
                float cx=x+sx*(width-stroke),cy=y+sy*(height-stroke);
                var vertical=Rect(root,name+sx+sy+"V",cx,y+sy*(height-arm),stroke,arm).gameObject.AddComponent<Image>();vertical.raycastTarget=false;vertical.color=tint;
                var horizontal=Rect(root,name+sx+sy+"H",x+sx*(width-arm),cy,arm,stroke).gameObject.AddComponent<Image>();horizontal.raycastTarget=false;horizontal.color=tint;
            }
        }
        void HudAsset(RectTransform root,string asset,float x,float y,float width,float height,float angle=0,float boundsWidth=0,float boundsHeight=0,bool mirror=false)
        {
            if(boundsWidth>0)x+=(boundsWidth-width)*.5f;if(boundsHeight>0)y+=(boundsHeight-height)*.5f;
            var image=Rect(root,"HudIcon"+asset+x, x*.75f,y*.75f,width*.75f,height*.75f).gameObject.AddComponent<RawImage>();
            image.texture=Resources.Load<Texture2D>("UI/Figma/Hud"+asset);image.raycastTarget=false;image.color=Color.white;image.rectTransform.localEulerAngles=new Vector3(0,0,-angle);
            if(mirror)image.uvRect=new UnityEngine.Rect(1,0,-1,1);
        }
        void BuildHudIcons(RectTransform root)
        {
            for(int i=0;i<3;i++)HudAsset(root,"Rectangle133",479,559+i*13,28,10,-20.64f,29.728f,19.23f);
            HudAsset(root,"Rectangle99",564,548,14,61);HudAsset(root,"Polygon9",560,589,4,17);HudAsset(root,"Polygon10",578,589,4,17);
            for(int i=0;i<3;i++){HudAsset(root,"Polygon8",701+i*13,563,10,16);HudOutlineBox(root,"AmmoIcon"+i,(701+i*13)*.75f,581*.75f,7.5f,16.5f,Color.white,2.25f);}
            HudAsset(root,"Line98",637.93f,574.62f,30.967f,4,-130.39f,20.066f,23.586f);HudAsset(root,"Line99",640.9f,560,11.794f,4,-83.08f,1.42f,11.709f);
            HudAsset(root,"Line101",619,579.51f,11.794f,4,-177.69f,11.785f,.475f);HudAsset(root,"Line100",644.39f,572.12f,11.794f,4,-28.28f,10.387f,5.588f);HudAsset(root,"Line102",628.97f,584.39f,11.794f,4,127.5f,7.18f,9.357f);
            for(int i=0;i<3;i++){HudAsset(root,"Rectangle166",872+i*12,508,12,15);HudAsset(root,"Rectangle167",872+i*12,523,12,15,0,0,0,true);}
            HudAsset(root,"Rectangle85",1576,532,10,34,90,34,10);HudAsset(root,"Rectangle87",1575,512.38f,9.259f,33.041f,-150,24.539f,33.244f);HudAsset(root,"Rectangle88",1585.49f,512,10.016f,33.478f,-30,25.413f,34);
        }
        void HudOutlineBox(RectTransform root,string name,float x,float y,float width,float height,Color tint,float stroke)
        {
            for(int i=0;i<4;i++){var image=Rect(root,name+i,x+(i==1?width-stroke:0),y+(i==3?height-stroke:0),i<2?stroke:width,i<2?height:stroke).gameObject.AddComponent<Image>();image.color=tint;image.raycastTarget=false;}
        }
        void BuildGatlingHeatHud(RectTransform root)
        {
            gatlingHeat=Rect(root,"GatlingHeatGauge",refinedFlightLayout?1296:408,refinedFlightLayout?340:932,
                refinedFlightLayout?64:150,refinedFlightLayout?350:64).gameObject.AddComponent<GatlingHeatGraphic>();
            gatlingHeat.raycastTarget=false;gatlingHeat.SetHorizontal(!refinedFlightLayout);
            gatlingHeatState=Text(root,"GatlingHeatState","",refinedFlightLayout?1284:408,refinedFlightLayout?364:932,
                refinedFlightLayout?88:150,18,11);gatlingHeatState.alignment=TextAlignmentOptions.Center;
            gatlingHeatValue=Text(root,"GatlingHeatValue","",refinedFlightLayout?1296:408,refinedFlightLayout?665:992,
                refinedFlightLayout?64:150,28,17);gatlingHeatValue.alignment=TextAlignmentOptions.Center;
            gatlingHeat.gameObject.SetActive(false);gatlingHeatState.gameObject.SetActive(false);gatlingHeatValue.gameObject.SetActive(false);
        }
        void RefreshGatlingHeatHud(Starframe.Combat.ShipWeapons weapons)
        {
            if(!gatlingHeat||weapons==null)return;
            var telemetry=weapons.GatlingHeat;
            bool visible=telemetry.Count>0;
            gatlingHeat.gameObject.SetActive(visible);gatlingHeatState.gameObject.SetActive(visible);gatlingHeatValue.gameObject.SetActive(visible);
            if(!visible)return;
            // Aggregation and the unlock condition belong to the actual mount states.
            // Switching groups or releasing fire must not clear cooling overheated guns.
            gatlingHeat.SetTelemetry(telemetry.DisplayHeatFraction,telemetry.AnyOverheated);
            Color tint=telemetry.AnyOverheated?GatlingHeatGraphic.OverheatColor:UiTheme.Current.combatInstrument;
            gatlingHeatState.color=gatlingHeatValue.color=tint;
            gatlingHeatState.text=telemetry.AnyOverheated?(L10n.IsEnglish?"OVERHEAT":"过热"):(L10n.IsEnglish?"HEAT":"热量");
            gatlingHeatValue.text=$"{100*telemetry.DisplayHeatFraction:0}%";
        }

        void CreateShieldFill(RectTransform root,float x,float y,float width,float height)
        {
            var track=Rect(root,"ShieldTrack",x,y,width,height).gameObject.AddComponent<Image>();track.color=refinedFlightLayout?new Color(HudInk.r,HudInk.g,HudInk.b,.16f):new Color(.035f,.12f,.18f,.9f);track.raycastTarget=false;
            shieldFill=Rect(root,"ShieldFill",x,y,width,height).gameObject.AddComponent<RawImage>();shieldFill.texture=Texture2D.whiteTexture;shieldFill.color=refinedFlightLayout?HudInk:new Color(.4f,.72f,.8f,.95f);shieldFill.raycastTarget=false;
            shieldFill.rectTransform.SetParent(track.rectTransform,false);SetProgress(shieldFill.rectTransform,0);
        }
        void CreateEnergyFill(RectTransform root,float x,float y,float width,float height)
        {
            var track=Rect(root,"EnergyTrack",x,y,width,height).gameObject.AddComponent<Image>();track.color=refinedFlightLayout?new Color(HudInk.r,HudInk.g,HudInk.b,.16f):new Color(.035f,.12f,.18f,.9f);track.raycastTarget=false;
            energyFill=Rect(root,"EnergyFill",x,y,width,height).gameObject.AddComponent<RawImage>();energyFill.texture=Texture2D.whiteTexture;energyFill.color=refinedFlightLayout?HudInk:new Color(.28f,.82f,1,.95f);energyFill.raycastTarget=false;
            energyFill.rectTransform.SetParent(track.rectTransform,false);SetProgress(energyFill.rectTransform,0);
        }
        void BuildTargetMonitor(RectTransform root,float px,float py,float pw,float ph,float tx,float ty,float sx,float sy,float cx,float cy,float identitySize=15,float schematicHeight=164,float titleSize=18,float identityOffset=35)
        {
            Panel(root,"TargetStatusGlass",px,py,pw,ph);Text(root,"TargetStatusTitle","hud.targetTitle",tx,ty,178,28,titleSize);
            targetIdentity=Text(root,"TargetIdentity","hud.noTarget",tx,ty+identityOffset,316,18,identitySize);targetIdentity.overflowMode=TextOverflowModes.Ellipsis;
            targetStatus=Rect(root,"TargetShipSchematic",sx,sy,320,schematicHeight).gameObject.AddComponent<ShipStatusGraphic>();
            targetCondition=Text(root,"TargetShipCondition","hud.targetHint",cx,cy,316,22,14);
        }
        void BuildWeaponGroupHud(RectTransform root,Starframe.Combat.ShipWeapons weapons)
        {
            foreach(var entry in weaponGroupHud)if(entry.tile)Destroy(entry.tile.gameObject);
            weaponGroupHud.Clear();
            if(weapons==null||weapons.Groups==null||weapons.Groups.Count==0)return;
            if(refinedFlightLayout)
            {
                Panel(root,"WeaponGroupHudGlass",575.25f,414,54,222);
                var ordered=new List<Starframe.Construction.WeaponGroupRecord>(weapons.Groups);ordered.Sort((a,b)=>a.id.CompareTo(b.id));
                float rowHeight=Mathf.Min(57,222f/Mathf.Max(1,ordered.Count));int row=0;
                foreach(var group in ordered)
                {
                    if(group.id<0||group.id>=8)continue;
                    float y=414+row++*rowHeight;
                    var tile=Rect(root,"WeaponGroupHud_"+group.id,575.25f,y,54,rowHeight-4).gameObject.AddComponent<Image>();tile.raycastTarget=false;
                    var stripe=new GameObject("ActiveLine",typeof(RectTransform),typeof(Image));stripe.layer=5;stripe.transform.SetParent(tile.transform,false);var stripeRect=(RectTransform)stripe.transform;stripeRect.anchorMin=Vector2.zero;stripeRect.anchorMax=new Vector2(1,0);stripeRect.offsetMin=Vector2.zero;stripeRect.offsetMax=new Vector2(0,2);stripe.GetComponent<Image>().raycastTarget=false;
                    var label=Text(root,"WeaponGroupHudLabel_"+group.id,"",576,y+3,53,rowHeight-8,Mathf.Min(13.5f,(rowHeight-10)*.4f));label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Overflow;
                    weaponGroupHud.Add(new WeaponGroupHudEntry{id=group.id,tile=tile,label=label});
                }
                cadenceLabel=Text(root,"DirectFireCadence","",348.75f,667,282,20,12);cadenceLabel.color=HudMuted;
                RefreshWeaponGroupHud(weapons);return;
            }
            const float tileWidth=102,tileHeight=44,gap=8;
            var groups=new List<Starframe.Construction.WeaponGroupRecord>(weapons.Groups);
            groups.Sort((a,b)=>a.id.CompareTo(b.id));
            int slots=4;foreach(var group in groups)if(group.id>=0&&group.id<8)slots=Mathf.Max(slots,group.id+1);
            float panelWidth=slots*(tileWidth+gap)+24,panelX=(1920-panelWidth)*.5f,panelY=refinedFlightLayout?948:476;
            Panel(root,"WeaponGroupHudGlass",panelX,panelY,panelWidth,86);
            Text(root,"WeaponGroupHudTitle","hud.weaponGroupsTitle",panelX+16,panelY+9,panelWidth*.5f,18,12).color=HudMuted;
            var hint=Text(root,"WeaponGroupHudHint","hud.weaponGroupsKeyHint",panelX+panelWidth*.5f,panelY+9,panelWidth*.5f-16,18,12);
            hint.alignment=TextAlignmentOptions.Right;hint.color=HudMuted;
            cadenceLabel=Text(root,"DirectFireCadence","",panelX,panelY-25,panelWidth,20,13);cadenceLabel.alignment=TextAlignmentOptions.Center;cadenceLabel.color=HudMuted;
            for(int i=0;i<groups.Count&&i<8;i++)
            {
                if(groups[i].id<0||groups[i].id>=8)continue;
                float x=panelX+16+groups[i].id*(tileWidth+gap),y=panelY+32;
                var tile=Rect(root,"WeaponGroupHud_"+groups[i].id,x,y,tileWidth,tileHeight).gameObject.AddComponent<Image>();
                tile.raycastTarget=false;
                var activeLine=new GameObject("ActiveLine",typeof(RectTransform),typeof(Image));activeLine.transform.SetParent(tile.transform,false);var lineRect=(RectTransform)activeLine.transform;lineRect.anchorMin=new Vector2(0,0);lineRect.anchorMax=new Vector2(1,0);lineRect.offsetMin=Vector2.zero;lineRect.offsetMax=new Vector2(0,2);activeLine.GetComponent<Image>().raycastTarget=false;
                var label=Text(root,"WeaponGroupHudLabel_"+groups[i].id,"",x+5,y+4,tileWidth-10,tileHeight-8,12);
                label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.Normal;
                weaponGroupHud.Add(new WeaponGroupHudEntry{id=groups[i].id,tile=tile,label=label});
            }
            RefreshWeaponGroupHud(weapons);
        }
        void RefreshWeaponGroupHud(Starframe.Combat.ShipWeapons weapons)
        {
            if(weapons==null)return;
            if(cadenceLabel)cadenceLabel.text=Starframe.App.GameInput.BindingLabel(Starframe.App.GameAction.WeaponCadence)+"  ·  "+(L10n.IsEnglish?(weapons.AlternatingDirectFire?"ALTERNATING FIRE":"SALVO FIRE"):(weapons.AlternatingDirectFire?"交替射击":"齐射"));
            Color enabled=new Color(HudInk.r,HudInk.g,HudInk.b,.18f);
            Color disabled=new Color(HudInk.r,HudInk.g,HudInk.b,.07f);
            for(int i=0;i<weaponGroupHud.Count;i++)
            {
                var entry=weaponGroupHud[i];if(!entry.tile||!entry.label)continue;
                bool active=weapons.IsGroupEnabled(entry.id);int count=weapons.GetGroupGunCount(entry.id,true);
                entry.tile.color=Color.clear;
                var line=entry.tile.transform.Find("ActiveLine")?.GetComponent<Image>();if(line)line.color=active?UiTheme.Current.combatInstrument:disabled;
                entry.label.text=refinedFlightLayout?(entry.id+1)+"  "+(active?"ON":"OFF")+"\n"+count+" WPN":"<b>"+(entry.id+1)+"  "+L10n.T(active?"hud.weaponGroupOn":"hud.weaponGroupOff")+"</b>\n<size=92%>"+L10n.T("hud.weaponGroupGuns",count)+"</size>";
                entry.label.color=active?HudInk:new Color(1,1,1,.35f);
            }
        }
        void RefreshMissileHud(Starframe.Combat.ShipWeapons weapons)
        {
            if(refinedFlightLayout){if(weaponStatus)weaponStatus.Refresh();return;}
            if(!missileLabel||!missileLockTrack||!missileLockFill)return;
            bool visible=weapons!=null&&weapons.HasMissileWeapons;
            missileLabel.gameObject.SetActive(visible);missileLockTrack.gameObject.SetActive(visible);missileLockFill.gameObject.SetActive(visible);
            if(!visible)return;
            float progress=Mathf.Clamp01(weapons.MissileLockProgress),reload=weapons.MissileReloadRemaining;
            Color tint=progress>=.999f?UiTheme.Current.combatInstrument:Starframe.Presentation.UiTheme.Current.amber;
            missileLabel.color=tint;missileLockFill.color=tint;
            string text=!combat.Targeting||!combat.Targeting.HasTarget?L10n.T("hud.missileNoTarget"):
                progress<.999f?L10n.T("hud.missileLocking",progress*100):L10n.T("hud.missileReady");
            text+="  ·  "+L10n.T("hud.missileCount",weapons.ReadyMissileCount,weapons.MissileWeaponCount);
            if(reload>.01f)text+="  ·  "+L10n.T("hud.missileReload",reload);
            missileLabel.text=text;SetProgress(missileLockFill.rectTransform,progress);
        }
        void SetDiagnosticsExpanded(bool expanded)
        {
            diagnosticsExpanded=expanded;
            if(refinedFlightLayout){if(info){SetRect(info.rectTransform,32,332,360,260);info.gameObject.SetActive(expanded);}if(diagnosticHint)diagnosticHint.gameObject.SetActive(expanded);return;}
            float height=expanded?424:184;
            SetPanelHeight(telemetryPanel,32,height);SetRect(telemetryShadow,35,36,TelemetryWidth,height);
            SetRect(info.rectTransform,54,204,316,188);
            SetRect(alertReadout.rectTransform,54,expanded?396:188,316,26);
            info.gameObject.SetActive(expanded);
            L10n.Bind(diagnosticHint,expanded?"hud.diagnosticsClose":"hud.diagnosticsOpen");
        }
        void SetPanelHeight(RawImage image,float y,float height)
        {
            if(!image)return;
            var rect=image.rectTransform;rect.anchorMin=new Vector2(32/1920f,(1080f-y-height)/1080f);rect.anchorMax=new Vector2((32+TelemetryWidth)/1920f,(1080f-y)/1080f);rect.offsetMin=rect.offsetMax=Vector2.zero;
            var uv=new UnityEngine.Rect(32/1920f,(1080f-y-height)/1080f,TelemetryWidth/1920f,height/1080f);image.uvRect=uv;
            if(image.material)image.material.SetVector("_PanelUVRect",new Vector4(uv.x,uv.y,uv.width,uv.height));
            if(scaledHudElements.Contains(rect))ScaleHudAnchors(rect);
        }
        static void SetProgress(RectTransform fill,float value)
        {
            // Ratio-based children follow their actual track on every canvas aspect and scale.
            fill.anchorMin=Vector2.zero;fill.anchorMax=new Vector2(Mathf.Clamp01(value),1);
            fill.offsetMin=fill.offsetMax=Vector2.zero;
        }
        static void SetRect(RectTransform rect,float x,float y,float width,float height)
        {
            rect.anchorMin=new Vector2(x/1920f,(1080f-y-height)/1080f);rect.anchorMax=new Vector2((x+width)/1920f,(1080f-y)/1080f);rect.offsetMin=rect.offsetMax=Vector2.zero;
            var hud=rect.GetComponentInParent<RangeHUD>();if(hud&&hud.scaledHudElements.Contains(rect))ScaleHudAnchors(rect);
        }
        public void OpenExit(){modal.SetActive(true);if(EventSystem.current){var resume=modal.transform.Find("CancelExit");if(resume)EventSystem.current.SetSelectedGameObject(resume.gameObject);}}
        public void CloseExit(){modal.SetActive(false);if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);}
        void Update()
        {
            // Actor snapshots are runtime-only and may be absent while surviving component references reload.
            if(!flight||!canvas||!worldCamera||!info)return;
            var p=flight.Performance;
            if(p==null||(combat&&(combat.Player==null||!combat.Player.ship)))return;
            Resize();
            if(!inputOverlayOpen&&Time.frameCount>ignoreEscapeThroughFrame&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){if(ExitDialogOpen)CloseExit();else OpenExit();}
            if(refinedFlightLayout&&!ExitDialogOpen&&Keyboard.current!=null&&Keyboard.current.f1Key.wasPressedThisFrame)SetDiagnosticsExpanded(!diagnosticsExpanded);
            double telemetryNow=Time.unscaledTimeAsDouble;
            if(telemetryNow+0.000001<nextTelemetry)return;
            nextTelemetry=nextTelemetry<=0?telemetryNow+TelemetryInterval:nextTelemetry+TelemetryInterval;
            if(nextTelemetry<=telemetryNow)nextTelemetry=telemetryNow+TelemetryInterval;
            var a=p.accelerationPositive/9.81f;var b=p.accelerationNegative/9.81f;
            string performanceDetails=(!refinedFlightLayout||diagnosticsExpanded)?L10n.T("hud.performance",p.totalMass,a.z,b.z,a.x,b.x,a.y,b.y,p.angularRate,p.pitchAcceleration,p.rollSpeed):"";
            if(!refinedFlightLayout)info.text=L10n.T("hud.speed",flight.CurrentSpeed,p.maxSpeed,performanceDetails);
            if(speedReadout)speedReadout.text=refinedFlightLayout?$"{flight.CurrentSpeed:0}":"";
            if(speedReadout)speedReadout.color=UiTheme.Current.combatInstrument;
            if(summaryReadout)summaryReadout.text=L10n.IsEnglish?$"Mass  {p.totalMass/1000:0.0} t":$"质量  {p.totalMass/1000:0.0} t";
            float maxEnergy=Mathf.Max(0,flight.MaxEnergy),energy=Mathf.Clamp(flight.Energy,0,maxEnergy),ratio=maxEnergy>0?energy/maxEnergy:0;
            if(energyLabel)energyLabel.text=maxEnergy>0?L10n.T("hud.energy",ratio,energy,maxEnergy):L10n.T("hud.noPower");
            if(energyFill)SetProgress(energyFill.rectTransform,ratio);
            if(boostLabel)boostLabel.gameObject.SetActive(flight.IsBoosting);
            if(refinedFlightLayout){info.text="";if(alertReadout){alertReadout.text="";alertReadout.gameObject.SetActive(false);}}
            if(combat){var player=combat.Player;int present=0,live=0;foreach(var part in player.ship.Parts)if(part.present)present++;foreach(var bot in combat.Bots)if(bot.ship.IsAlive)live++;
                RefreshWeaponGroupHud(player.weapons);RefreshMissileHud(player.weapons);RefreshGatlingHeatHud(player.weapons);
                var flares=player.countermeasures;
                if(flareLabel&&flares)flareLabel.text=flares.LauncherCount==0?L10n.T("hud.flaresMissing",Starframe.App.GameInput.BindingLabel(Starframe.App.GameAction.Countermeasure)):
                    L10n.T("hud.flaresStatus",Starframe.App.GameInput.BindingLabel(Starframe.App.GameAction.Countermeasure),flares.ReadyCount,flares.LauncherCount,flares.NextReadySeconds);
                if(threatLabel)
                {
                    bool incoming=combat.Projectiles.TryGetIncomingThreat(player.ship,out float missileDistance,out _);
                    int locks=combat.Sensors!=null?combat.Sensors.CountLocksOn(player.ship):0;
                    threatLabel.text=incoming?L10n.T("hud.missileIncoming",missileDistance):locks>0?L10n.T("hud.enemyLocked",locks):"";
                    threatLabel.color=incoming?UiTheme.Current.danger:UiTheme.Current.amber;
                }
                float capacity=player.ship.Shields.Capacity,charge=player.ship.Shields.Charge;
                shieldLabel.text=capacity>0?L10n.T("hud.shield",charge,capacity):L10n.T("hud.noShield");
                SetProgress(shieldFill.rectTransform,capacity>0?charge/capacity:0);
                if(instruments){instruments.SetTelemetry(flight.CurrentSpeed,p.maxSpeed,ratio,capacity>0?charge/capacity:0,flight.IsBoosting);instrumentShield.text=capacity>0?$"{100*charge/capacity:0}%":"—";instrumentEnergy.text=$"{100*ratio:0}%";instrumentShield.color=instrumentEnergy.color=UiTheme.Current.combatInstrument;}
                var targeting=combat.Targeting;bool locked=targeting&&targeting.HasTarget;
                var target=locked?targeting.LockedTarget:null;
                if(lockLabel){lockLabel.text=locked?L10n.T("hud.unlock",targeting.TargetDistance):(L10n.IsEnglish?"F  Lock target ahead":"F  锁定前方目标");lockLabel.color=locked?UiTheme.Current.danger:HudMuted;}
                ownStatus.Bind(player.ship);ownCondition.text=refinedFlightLayout?(L10n.IsEnglish?"PARTS: ":"部件：")+present+"/"+player.ship.Parts.Count:Condition(player.ship);ownCondition.color=player.ship.IsAlive?(refinedFlightLayout?HudInk:new Color(.48f,.85f,1)):(refinedFlightLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.25f,.2f));
                targetStatus.Bind(target);
                targetIdentity.text=target?(refinedFlightLayout?TargetName(target):L10n.T("hud.targetIdentity",TargetName(target),targeting.TargetDistance)):L10n.T("hud.noTarget");
                L10n.ApplyUserContentFont(targetIdentity);
                targetCondition.text=target?Condition(target):L10n.T("hud.targetHint");
                int detected=targeting?targeting.DetectedTargets.Count:0;
                float sensorRange=combat.Sensors!=null?combat.Sensors.MaximumRange:targeting?targeting.LockRange:0;
                radarLabel.text=L10n.IsEnglish?$"{sensorRange/1000:0.#} km  ·  {detected} contacts":$"{sensorRange/1000:0.#} km  ·  目标 {detected}";
                if(leadLabel){var leads=combat.Targeting?combat.Targeting.LeadPoints:null;leadNames.Clear();if(leads!=null&&(leads.Count>1||diagnosticsExpanded))foreach(var lead in leads)if(lead.weapon){int i=leadNames.Count;leadNames.Add($"<color=#FF3D2E>{i+1}</color> · {lead.weapon.projectileSpeed:0} m/s");}leadLabel.text=string.Join("   ",leadNames);}
                string combatDetails=(!refinedFlightLayout||diagnosticsExpanded)?L10n.T("hud.combatDetails",present,player.ship.Parts.Count,player.weapons.ActiveGuns,player.weapons.BlockedGuns,player.ship.DamageDealt,player.ship.DamageTaken,live,combat.Bots.Count,combat.BotKills):"";
                string alert=missionPresentation?(!player.ship.IsAlive?L10n.T("mission.arcade.shipLost"):""):
                    !player.ship.IsAlive?L10n.T("hud.respawn",combat.PlayerRespawnRemaining):live<combat.Bots.Count?L10n.T("hud.rebuilding"):"";
                if(!refinedFlightLayout)
                {
                    info.text+=combatDetails;
                    if(alert.Length>0)info.text+="\n"+alert;
                }
                else
                {
                    summaryReadout.text=L10n.IsEnglish?$"Mass  {p.totalMass/1000:0.0} t\nWeapons  {player.weapons.ActiveGuns} ready · {player.weapons.BlockedGuns} blocked":$"质量  {p.totalMass/1000:0.0} t\n武器  {player.weapons.ActiveGuns} 就绪 · {player.weapons.BlockedGuns} 遮挡";
                    if(diagnosticsExpanded)info.text=performanceDetails+combatDetails;
                    if(alertReadout&&alert.Length>0){alertReadout.text=alert;alertReadout.color=!player.ship.IsAlive?Starframe.Presentation.UiTheme.Current.danger:Starframe.Presentation.UiTheme.Current.amber;alertReadout.gameObject.SetActive(true);}
                }
            }
        }
        static string BlueprintCostText(float cost)
            =>(L10n.IsEnglish?"COST ":"造价 ")+Mathf.Max(0,cost).ToString("#,0",System.Globalization.CultureInfo.InvariantCulture);
        string TargetName(FlightShipRuntime ship){foreach(var actor in combat.Actors)if(actor.ship==ship)return actor.blueprintName??L10n.T("hud.enemyShip");return L10n.T("hud.enemyShip");}
        string Condition(FlightShipRuntime ship)
        {
            int count=0,damaged=0;foreach(var p in ship.Parts)if(p.present){count++;if(p.hp<p.maxHp-.001f)damaged++;}
            Color damageColor=damaged>0?(refinedFlightLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.38f,.33f)):HudMuted;
            string damageHex=ColorUtility.ToHtmlStringRGB(damageColor);
            return L10n.T("hud.condition",L10n.T(ship.IsAlive?"hud.coreOnline":"hud.coreDestroyed"),count,ship.Parts.Count,damaged,damageHex);
        }
        void RefreshLocale(){font=L10n.CurrentFont;nextTelemetry=0;}
        void RefreshVisualPreferences()
        {
            if(canvas)UiStyle.ApplyScaler(canvas.GetComponent<CanvasScaler>());
            foreach(var image in glass)if(image&&image.material&&image.material.shader.name=="Starframe/UI/BackdropBlur")GlassUiPalette.Apply(image.material,image.name=="ExitGlass"?GlassUiPalette.Dialog:GlassUiPalette.Glass);
            nextTelemetry=0;
        }
        void LateUpdate()
        {
            if(!combat||combat.Player==null||!combat.Player.ship||!canvas||!modal||!worldCamera)return;
            while(targetLabels.Count<combat.Bots.Count){
                int i=targetLabels.Count;var label=Text((RectTransform)canvas.transform,"BotMarker"+i,"",0,0,190,42,refinedFlightLayout?14.25f:18);label.alignment=TextAlignmentOptions.Bottom;label.fontStyle=FontStyles.Bold;
                label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.5f,.5f);label.rectTransform.pivot=new Vector2(.5f,0);label.rectTransform.sizeDelta=new Vector2(190,42);label.rectTransform.localScale=Vector3.one;label.transform.SetSiblingIndex(modal.transform.GetSiblingIndex());targetLabels.Add(label);
                var distance=Text((RectTransform)canvas.transform,"BotDistance"+i,"",0,0,140,22,14.25f);distance.alignment=TextAlignmentOptions.Top;distance.rectTransform.anchorMin=distance.rectTransform.anchorMax=new Vector2(.5f,.5f);distance.rectTransform.pivot=new Vector2(.5f,1);distance.rectTransform.sizeDelta=new Vector2(140,22);distance.transform.SetSiblingIndex(modal.transform.GetSiblingIndex());targetDistances.Add(distance);
            }
            for(int i=0;i<targetLabels.Count;i++){
                var label=targetLabels[i];var distance=targetDistances[i];if(i>=combat.Bots.Count){label.gameObject.SetActive(false);distance.gameObject.SetActive(false);continue;}
                var bot=combat.Bots[i];var point=worldCamera.WorldToScreenPoint(refinedFlightLayout?bot.ship.CorePosition:bot.body.position+Vector3.up*(bot.ship.HullRadius+1));
                bool main=combat.Targeting.HasTarget&&combat.Targeting.LockedTarget==bot.ship;
                bool area=combat.Player.weapons.IsVlsLockedTarget(bot.ship);
                bool visible=bot.ship.IsAlive&&(main||area)&&point.z>0&&point.x>0&&point.x<Screen.width&&point.y>0&&point.y<Screen.height;label.gameObject.SetActive(visible);label.color=new Color(1,.24f,.18f,.9f);
                distance.gameObject.SetActive(visible&&refinedFlightLayout);
                if(visible)
                {
                    label.text=BlueprintCostText(bot.ship.BlueprintCost)+"\n"+bot.blueprintName;L10n.ApplyUserContentFont(label);label.color=new Color32(255,46,34,255);
                    if(RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,point,displayCamera,out var local))
                    {
                        float screenRadius=Mathf.Abs(worldCamera.WorldToScreenPoint(bot.ship.CorePosition+worldCamera.transform.right*combat.Targeting.LockedTargetRadius).x-point.x)/Mathf.Max(.01f,canvas.scaleFactor);
                        float frameRadius=main?Mathf.Max(19.875f,Mathf.Clamp(screenRadius,18/Mathf.Max(.01f,canvas.scaleFactor),76/Mathf.Max(.01f,canvas.scaleFactor))):22/Mathf.Max(.01f,canvas.scaleFactor);
                        label.rectTransform.anchoredPosition=local+(refinedFlightLayout?new Vector2(-18,frameRadius+12):Vector2.zero);
                        distance.rectTransform.anchoredPosition=local+new Vector2(-18,-frameRadius-12);distance.text=Vector3.Distance(combat.Player.ship.CorePosition,bot.ship.CorePosition).ToString("0")+"M";distance.color=label.color;
                    }
                }
            }
        }
        void ApplyHudElementScale()
        {
            if(!canvas)return;
            if(refinedFlightLayout)return;
            foreach(Transform child in canvas.transform)
            {
                if(child.name=="FlightView"||child.name=="ExitModal"||child.name=="TargetAimCanvas"||child.name.StartsWith("BotMarker")||child.GetComponent<FlightAimGraphic>()||child.GetComponent<Starframe.Combat.TargetSilhouetteOutline>())continue;
                var rect=child as RectTransform;if(!rect||!scaledHudElements.Add(rect))continue;
                rect.localScale=Vector3.one*.95f;ScaleHudAnchors(rect);
            }
        }
        static void ScaleHudAnchors(RectTransform rect)
        {
            // Apply the same screen-centred scale to positions and sizes, keeping
            // separate instrument graphics and their text readouts aligned.
            Vector2 centre=(rect.anchorMin+rect.anchorMax)*.5f;
            Vector2 shift=(centre-new Vector2(.5f,.5f))*(-.05f);
            rect.anchorMin+=shift;rect.anchorMax+=shift;
        }
        void Resize()
        {
            int w=Mathf.Max(1,Screen.width),h=Mathf.Max(1,Screen.height);
            if(sceneTexture&&sceneTexture.width==w&&sceneTexture.height==h)return;
            if(sceneTexture){worldCamera.targetTexture=null;sceneTexture.Release();Destroy(sceneTexture);}
            sceneTexture=new RenderTexture(w,h,24,RenderTextureFormat.ARGBHalf){name="RangeView",useMipMap=true,autoGenerateMips=true,filterMode=FilterMode.Trilinear};sceneTexture.Create();
            worldCamera.targetTexture=sceneTexture;worldCamera.aspect=(float)w/h;foreach(var image in glass)image.texture=sceneTexture;
        }
        static RectTransform Rect(RectTransform parent,string name,float x,float y,float w,float h)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;var r=(RectTransform)go.transform;r.SetParent(parent,false);
            r.anchorMin=new Vector2(x/1920,(1080-y-h)/1080);r.anchorMax=new Vector2((x+w)/1920,(1080-y)/1080);r.offsetMin=r.offsetMax=Vector2.zero;return r;
        }
        TextMeshProUGUI Text(RectTransform parent,string name,string value,float x,float y,float w,float h,float size)
        {
            var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.color=HudInk;
            if(L10n.IsKey(value))L10n.Bind(t,value);else{t.text=value;L10n.ApplyFont(t);}
            t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Ellipsis;UiStyle.ApplyText(t,size);
            if(refinedFlightLayout)FigmaVisuals.SetFont(t,true);
            if(name=="ExitHint"||name.StartsWith("Controls",StringComparison.Ordinal))t.textWrappingMode=TextWrappingModes.Normal;
            return t;
        }
        RectTransform Panel(RectTransform parent,string name,float x,float y,float w,float h)
        {
            bool monitor=!refinedFlightLayout&&(name=="OwnStatusGlass"||name=="TargetStatusGlass");
            bool floating=name!="ExitGlass"&&!monitor;
            if(floating){var empty=Rect(parent,name,x,y,w,h).gameObject.AddComponent<RawImage>();empty.color=Color.clear;empty.raycastTarget=false;if(name=="OwnStatusGlass"||name=="TargetStatusGlass")glass.Add(empty);return empty.rectTransform;}
            var shadow=Rect(parent,name+"Shadow",x+3,y+4,w,h).gameObject.AddComponent<Image>();shadow.color=new Color(0,0,0,.08f);shadow.raycastTarget=false;
            var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<RawImage>();image.raycastTarget=false;
            var material=new Material(Shader.Find("Starframe/UI/BackdropBlur"));GlassUiPalette.Apply(material,name=="ExitGlass"?GlassUiPalette.Dialog:GlassUiPalette.Glass);
            material.SetVector("_PanelUVRect",new Vector4(x/1920,(1080-y-h)/1080,w/1920,h/1080));
            image.uvRect=new UnityEngine.Rect(x/1920,(1080-y-h)/1080,w/1920,h/1080);image.texture=sceneTexture;image.material=material;materials.Add(material);glass.Add(image);return shadow.rectTransform;
        }
        void Button(RectTransform parent,string name,string value,float x,float y,float w,float h,Action action,bool primary=false)
        {
            var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.color=primary?GlassUiPalette.Action:GlassUiPalette.Button;
            var button=image.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.colors=UiStyle.ControlColors();button.navigation=new Navigation{mode=Navigation.Mode.Automatic};button.onClick.AddListener(()=>{Starframe.App.GameAudio.Click();action();});
            var text=Text(parent,name+"Text",value,x,y+7,w,h-14,17);text.alignment=TextAlignmentOptions.Center;
            if(primary&&refinedFlightLayout)text.color=GlassUiPalette.ActionInk;
        }
        void OnDestroy()
        {
            if(worldCamera)worldCamera.targetTexture=null;if(sceneTexture){sceneTexture.Release();Destroy(sceneTexture);}
            foreach(var m in materials)Destroy(m);
            L10n.Changed-=RefreshLocale;
            Starframe.App.GameSettings.Changed-=RefreshVisualPreferences;

        }
    }
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FlightAimGraphic:UnityEngine.UI.Graphic
    {
        Camera viewCamera;ShipFlightController flight;Canvas ownerCanvas;
        Vector2 actualPoint,desiredPoint,displayedOffset;bool positioned,refinedReticle;
        readonly Starframe.Presentation.ReticleHitFeedback hitFeedback=new Starframe.Presentation.ReticleHitFeedback();
        bool sceneryFeedback;
        double lastCombatHit=double.NegativeInfinity;
        public bool NotifyWeaponHit(bool scenery=false)
        {
            var theme=Starframe.Presentation.UiTheme.Current;
            if(!theme.hitFeedbackEnabled)return false;
            double now=Time.unscaledTimeAsDouble;
            if(scenery&&now-lastCombatHit<theme.hitFeedbackDuration)return false;
            bool accepted=hitFeedback.TryTrigger(now,theme.hitFeedbackMaxRate);
            if(!scenery){lastCombatHit=now;sceneryFeedback=false;SetVerticesDirty();}
            if(accepted){sceneryFeedback=scenery;SetVerticesDirty();}return accepted;
        }
        public void Initialize(Camera view,ShipFlightController ship){viewCamera=view;flight=ship;ownerCanvas=GetComponentInParent<Canvas>();raycastTarget=false;color=Color.white;refinedReticle=Starframe.Presentation.UiTheme.Current.aimReticle;}
        public bool TryGetCursorScreenPoint(out Vector2 point)
            => TryGetScreenPoint(desiredPoint,out point);
        public bool TryGetForwardScreenPoint(out Vector2 point)
            => TryGetScreenPoint(actualPoint,out point);
        bool TryGetScreenPoint(Vector2 local,out Vector2 point)
        {
            point=default;if(!positioned||!ownerCanvas||!isActiveAndEnabled)return false;
            var uiCamera=ownerCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:ownerCanvas.worldCamera;
            point=RectTransformUtility.WorldToScreenPoint(uiCamera,rectTransform.TransformPoint(local));
            return true;
        }
        void LateUpdate()
        {
            if(!flight||!viewCamera||!ownerCanvas)return;
            var uiCamera=ownerCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:ownerCanvas.worldCamera;
            // Project direction vectors, avoiding parallax from the elevated third-person camera.
            var forward=Starframe.Combat.AimProjection.ScreenPoint(viewCamera,flight.RenderForward);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,forward,uiCamera,out var a);
            var r=rectTransform.rect;a=Clamp(a,r);
            if(!positioned){actualPoint=desiredPoint=r.center;positioned=true;}
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f),scale=1/Mathf.Max(.01f,ownerCanvas.scaleFactor);
            actualPoint=a;
            // Smooth the relative offset, so camera/ship motion moves both markers together.
            displayedOffset=Vector2.MoveTowards(displayedOffset,Vector2.Lerp(displayedOffset,flight.AimOffsetPixels*scale,1-Mathf.Exp(-flight.Tuning.reticleSharpness*2*dt)),flight.Tuning.reticleMaxSpeed*1.5f*scale*dt);
            desiredPoint=Clamp(actualPoint+displayedOffset,r);
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(!flight||!viewCamera||!ownerCanvas)return;
            Vector2 a=actualPoint,b=desiredPoint;
            float s=1/Mathf.Max(.01f,ownerCanvas.scaleFactor);
            var theme=Starframe.Presentation.UiTheme.Current;
            hitFeedback.Sample(Time.unscaledTimeAsDouble,theme.hitFeedbackDuration,out float hitStrength,out float extension);
            if(sceneryFeedback)hitStrength*=theme.sceneryHitFeedbackStrength;
            if(!theme.hitFeedbackEnabled){hitFeedback.Reset();hitStrength=extension=0;}
            if(refinedReticle)
            {
                // Projection and cursor positions above are shared with the legacy graphic; only the drawing changes.
                var accent=theme.combatInstrument;accent.a=.98f;var cursor=new Color(1,1,1,.9f);
                if(theme.hitFeedbackEnabled)DrawReactiveCross(vh,a,s,theme,hitStrength,accent);
                else
                {
                    DrawOpenCross(vh,a,6*s,15*s,4*s,accent);
                    Rect(vh,a,new Vector2(4,4)*s,accent);
                }
                DrawHitMarker(vh,a,s,theme,hitStrength,extension);
                bool showCursor=(b-a).sqrMagnitude>=64*s*s;
                if(showCursor)
                {
                    if((b-a).magnitude>28*s){var direction=(b-a).normalized;Line(vh,a+direction*24*s,b-direction*15*s,1*s,new Color(theme.text.r,theme.text.g,theme.text.b,.20f));}
                    float gap=7,edge=11,thickness=2*s;
                    Line(vh,b+new Vector2(-edge,-edge)*s,b+new Vector2(-gap,-edge)*s,thickness,cursor);
                    Line(vh,b+new Vector2(-edge,-edge)*s,b+new Vector2(-edge,-gap)*s,thickness,cursor);
                    Line(vh,b+new Vector2(edge,-edge)*s,b+new Vector2(gap,-edge)*s,thickness,cursor);
                    Line(vh,b+new Vector2(edge,-edge)*s,b+new Vector2(edge,-gap)*s,thickness,cursor);
                    Line(vh,b+new Vector2(-edge,edge)*s,b+new Vector2(-gap,edge)*s,thickness,cursor);
                    Line(vh,b+new Vector2(-edge,edge)*s,b+new Vector2(-edge,gap)*s,thickness,cursor);
                    Line(vh,b+new Vector2(edge,edge)*s,b+new Vector2(gap,edge)*s,thickness,cursor);
                    Line(vh,b+new Vector2(edge,edge)*s,b+new Vector2(edge,gap)*s,thickness,cursor);
                }
                return;
            }
            Line(vh,a,b,2.5f*s);
            if(theme.hitFeedbackEnabled)
            {
                DrawReactiveCross(vh,a,s,theme,hitStrength,Color.white);
                DrawHitMarker(vh,a,s,theme,hitStrength,extension);
            }
            else {Line(vh,a+Vector2.left*12*s,a+Vector2.right*12*s,4*s);Line(vh,a+Vector2.down*12*s,a+Vector2.up*12*s,4*s);}
            var lowerLeft=b+new Vector2(-8,-8)*s;var upperLeft=b+new Vector2(-8,8)*s;
            var upperRight=b+new Vector2(8,8)*s;var lowerRight=b+new Vector2(8,-8)*s;
            Line(vh,lowerLeft,upperLeft,3.5f*s);Line(vh,upperLeft,upperRight,3.5f*s);
            Line(vh,upperRight,lowerRight,3.5f*s);Line(vh,lowerRight,lowerLeft,3.5f*s);
        }
        void DrawReactiveCross(VertexHelper vh,Vector2 center,float scale,Starframe.Presentation.UiTheme theme,float strength,Color tint)
        {
            float idle=Mathf.Max(0,theme.reticleIdleGap);
            float motion=Mathf.Clamp01(Starframe.App.GameSettings.Current.hudMotion);
            float gap=Mathf.Lerp(idle,Mathf.Clamp(theme.reticleHitGap,0,idle),strength*motion)*scale;
            float outer=gap+Mathf.Max(1,theme.reticleArmLength)*scale;
            tint=Color.Lerp(tint,Color.white,strength*.65f);
            DrawOpenCross(vh,center,gap,outer,3*scale,tint);
            Rect(vh,center,Vector2.one*3*scale,tint);
        }
        void DrawOpenCross(VertexHelper vh,Vector2 center,float gap,float outer,float width,Color tint)
        {
            Line(vh,center+Vector2.left*outer,center+Vector2.left*gap,width,tint);
            Line(vh,center+Vector2.right*gap,center+Vector2.right*outer,width,tint);
            Line(vh,center+Vector2.down*outer,center+Vector2.down*gap,width,tint);
            Line(vh,center+Vector2.up*gap,center+Vector2.up*outer,width,tint);
        }
        void DrawHitMarker(VertexHelper vh,Vector2 center,float scale,Starframe.Presentation.UiTheme theme,float strength,float extension)
        {
            if(strength<=.001f)return;
            float motion=Mathf.Clamp01(Starframe.App.GameSettings.Current.hudMotion);
            float length=Mathf.Lerp(2,Mathf.Max(2,theme.hitMarkerLength),Mathf.Lerp(1,extension,motion))*scale*(sceneryFeedback ? .65f : 1);
            Color tint=sceneryFeedback?Color.white:Color.Lerp(theme.hitMarkerColor,Color.white,.22f*strength);tint.a*=strength;
            Color glow=tint;glow.a*=.18f;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)
            {
                var direction=new Vector2(x,y).normalized;
                var start=center+direction*15*scale;var end=start+direction*length;
                Line(vh,start,end,6*scale,glow);Line(vh,start,end,3*scale,tint);
            }
        }
        static Vector2 Clamp(Vector2 p,UnityEngine.Rect r)=>new Vector2(Mathf.Clamp(p.x,r.xMin+14,r.xMax-14),Mathf.Clamp(p.y,r.yMin+14,r.yMax-14));
        void Line(VertexHelper vh,Vector2 a,Vector2 b,float width)=>Line(vh,a,b,width,Color.white);
        void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
        {
            if((a-b).sqrMagnitude<.000001f)return;
            Stroke(vh,a,b,width,tint);
        }
        void Rect(VertexHelper vh,Vector2 center,Vector2 size,Color tint)
        {
            int i=vh.currentVertCount;Vector2 half=size*.5f;
            vh.AddVert(center+new Vector2(-half.x,-half.y),tint,Vector2.zero);vh.AddVert(center+new Vector2(-half.x,half.y),tint,Vector2.zero);
            vh.AddVert(center+new Vector2(half.x,half.y),tint,Vector2.zero);vh.AddVert(center+new Vector2(half.x,-half.y),tint,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        void Stroke(VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
        {
            if((a-b).sqrMagnitude<.000001f)return;var d=b-a;var n=new Vector2(-d.y,d.x).normalized*width*.5f;int i=vh.currentVertCount;
            vh.AddVert(a-n,tint,Vector2.zero);vh.AddVert(a+n,tint,Vector2.zero);vh.AddVert(b+n,tint,Vector2.zero);vh.AddVert(b-n,tint,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}
