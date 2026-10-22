using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;
namespace Starframe.Flight
{
    public sealed class RangeHUD:MonoBehaviour
    {
        ShipFlightController flight;Camera worldCamera,displayCamera;RenderTexture sceneTexture;
        Starframe.Combat.RangeCombatWorld combat;
        TMP_FontAsset font,fallback;Canvas canvas;GameObject modal;
        TextMeshProUGUI info,lockLabel,leadLabel,energyLabel,boostLabel;RawImage energyFill;float nextTelemetry;readonly List<RawImage> glass=new List<RawImage>();readonly List<Material> materials=new List<Material>();
        readonly List<TextMeshProUGUI> targetLabels=new List<TextMeshProUGUI>();
        Starframe.Combat.TargetAimGraphic targetAimGraphic;
        ShipStatusGraphic ownStatus,targetStatus;SphereRadarGraphic radar;
        TextMeshProUGUI ownCondition,targetIdentity,targetCondition,radarLabel,shieldLabel;
        RawImage shieldFill;
        TextMeshProUGUI speedReadout,summaryReadout,diagnosticHint,alertReadout;
        RawImage telemetryPanel;RectTransform telemetryShadow;
        bool refinedFlightLayout,diagnosticsExpanded;
        const float EnergyWidth=316;
        public bool ExitDialogOpen=>modal&&modal.activeSelf;
        public event Action ExitConfirmed;
        public void BindCombat(Starframe.Combat.RangeCombatWorld value)
        {
            combat=value;
            if(!combat||!canvas||!displayCamera||!worldCamera)return;
            var root=(RectTransform)canvas.transform;
            leadLabel=Text(root,"LeadLegend","",refinedFlightLayout?510:1410,refinedFlightLayout?80:393,refinedFlightLayout?960:478,refinedFlightLayout?28:42,refinedFlightLayout?12:14);leadLabel.alignment=refinedFlightLayout?TextAlignmentOptions.TopLeft:TextAlignmentOptions.TopRight;leadLabel.textWrappingMode=TextWrappingModes.Normal;
            ownStatus.Bind(combat.Player.ship);radar.Initialize(combat,combat.Targeting.LockRange);
            targetStatus.SetRelativeObserver(combat.Player.ship.transform);
            var overlay=new GameObject("TargetAimCanvas",typeof(RectTransform),typeof(Canvas));overlay.layer=5;overlay.transform.SetParent(canvas.transform,false);
            var targetCanvas=overlay.GetComponent<Canvas>();targetCanvas.renderMode=RenderMode.ScreenSpaceCamera;targetCanvas.worldCamera=displayCamera;targetCanvas.planeDistance=1;
            var rect=(RectTransform)overlay.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var graphicObject=new GameObject("TargetAim",typeof(RectTransform),typeof(CanvasRenderer));graphicObject.layer=5;graphicObject.transform.SetParent(overlay.transform,false);
            var graphicRect=(RectTransform)graphicObject.transform;graphicRect.anchorMin=Vector2.zero;graphicRect.anchorMax=Vector2.one;graphicRect.offsetMin=graphicRect.offsetMax=Vector2.zero;
            targetAimGraphic=graphicObject.AddComponent<Starframe.Combat.TargetAimGraphic>();targetAimGraphic.Initialize(worldCamera,combat.Targeting);
            overlay.transform.SetSiblingIndex(modal.transform.GetSiblingIndex());modal.transform.SetAsLastSibling();
        }
        public void Initialize(Camera camera,ShipFlightController controller,FlightTuning tuning)
        {
            flight=controller;worldCamera=camera;worldCamera.cullingMask&=~(1<<5);
            refinedFlightLayout=Starframe.Presentation.UiTheme.Current.flightLayout;
            font=Instantiate(TMP_Settings.defaultFontAsset);fallback=TMP_FontAsset.CreateFontAsset("Microsoft YaHei","Regular",48);
            font.fallbackFontAssetTable=new List<TMP_FontAsset>{fallback};
            var presenter=new GameObject("Flight HUD Camera");presenter.transform.SetParent(transform,false);
            displayCamera=presenter.AddComponent<Camera>();displayCamera.orthographic=true;displayCamera.orthographicSize=5.4f;displayCamera.cullingMask=1<<5;displayCamera.depth=10;
            displayCamera.clearFlags=CameraClearFlags.SolidColor;displayCamera.backgroundColor=Color.black;displayCamera.nearClipPlane=.1f;displayCamera.farClipPlane=10;displayCamera.allowHDR=false;
            var data=presenter.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.renderPostProcessing=false;
            var go=new GameObject("FlightRangeCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            go.layer=5;go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=displayCamera;canvas.planeDistance=1;
            var scale=go.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;
            if(!FindFirstObjectByType<EventSystem>()){var events=new GameObject("FlightEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));events.transform.SetParent(transform,false);events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();}
            var root=(RectTransform)go.transform;
            var background=Rect(root,"FlightView",0,0,1920,1080).gameObject.AddComponent<RawImage>();background.raycastTarget=false;glass.Add(background);
            var aim=Rect(root,"FlightAim",0,0,1920,1080).gameObject.AddComponent<FlightAimGraphic>();aim.Initialize(worldCamera,flight);
            if(refinedFlightLayout)BuildRefinedFlightLayout(root);
            else BuildLegacyFlightLayout(root);
            int radarTop=refinedFlightLayout?32:28;float radarHeight=refinedFlightLayout?240:312;
            Panel(root,"RadarGlass",1576,radarTop,312,radarHeight);Text(root,"RadarTitle",refinedFlightLayout?"战术雷达":"战术雷达 · 平面投影",1596,radarTop+14,272,25,refinedFlightLayout?16:17);
            radar=Rect(root,"SphericalRadar",1608,radarTop+(refinedFlightLayout?41:45),248,refinedFlightLayout?164:225).gameObject.AddComponent<SphereRadarGraphic>();
            radarLabel=Text(root,"RadarContacts","3 km · 敌机 0",1596,radarTop+(refinedFlightLayout?211:280),272,22,13);
            if(refinedFlightLayout)
            {
                Text(root,"ControlsMovement","W/S 前后 · A/D 左右 · Space/Ctrl 上下 · Q/E 滚转 · Shift 加速 · 鼠标转向",410,1002,1100,20,13).alignment=TextAlignmentOptions.Center;
                Text(root,"ControlsCombat","左键开火 · F 锁定/解除 · 滚轮镜头 · Esc 退出 · F1 性能参数",410,1027,1100,20,13).alignment=TextAlignmentOptions.Center;
            }
            else Text(root,"Controls","W/S 前后 · A/D 左右 · Space/Ctrl 上下 · Q/E 滚转 · Shift 加速 · 鼠标 偏移转向 · 左键 开火 · F 锁定/解除 · 滚轮 镜头 · Esc 退出",32,1030,1830,28,15);
            var group=Rect(root,"ExitModal",0,0,1920,1080);modal=group.gameObject;
            var dim=Rect(group,"ModalBlocker",0,0,1920,1080).gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,.45f);
            Panel(group,"ExitGlass",680,410,560,260);
            Text(group,"ExitTitle","退出靶场？",712,443,460,45,25);
            Text(group,"ExitHint","返回装配编辑器，保留进入时的蓝图。",712,511,496,34,17);
            Button(group,"CancelExit","继续飞行",716,590,210,44,()=>CloseExit(),refinedFlightLayout);
            Button(group,"ConfirmExit","返回装配",994,590,210,44,()=>{CloseExit();ExitConfirmed?.Invoke();});
            modal.SetActive(false);Resize();Canvas.ForceUpdateCanvases();
            foreach(var child in go.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
        }
        void BuildLegacyFlightLayout(RectTransform root)
        {
            Panel(root,"TelemetryGlass",32,32,450,348);
            Text(root,"Title","性能靶场",55,49,382,35,23);
            info=Text(root,"Telemetry","",55,94,396,275,15);info.textWrappingMode=TextWrappingModes.Normal;
            lockLabel=Text(root,"TargetLockStatus","F 目标锁定",1410,353,478,30,17);lockLabel.alignment=TextAlignmentOptions.Right;
            Panel(root,"OwnStatusGlass",32,618,360,266);Text(root,"OwnStatusTitle","自身状态",54,634,316,28,18);
            ownStatus=Rect(root,"OwnShipSchematic",52,670,320,173).gameObject.AddComponent<ShipStatusGraphic>();
            ownCondition=Text(root,"OwnShipCondition","核心正常",54,852,316,22,14);
            Panel(root,"EnergyGlass",32,894,360,128);
            shieldLabel=Text(root,"ShieldValue","护盾  0 / 0",54,904,316,25,14);
            CreateShieldFill(root,54,932,EnergyWidth,10);
            energyLabel=Text(root,"EnergyValue","能量  100% · 500 / 500",54,944,316,25,14);
            CreateEnergyFill(root,54,976,EnergyWidth,14);
            boostLabel=Text(root,"BoostStatus","SHIFT  加速",54,996,316,20,13);boostLabel.color=new Color(.52f,.88f,1,.95f);boostLabel.gameObject.SetActive(false);
            BuildTargetMonitor(root,1528,726,360,296,1550,742,1548,809,1550,990);
        }
        void BuildRefinedFlightLayout(RectTransform root)
        {
            telemetryShadow=Panel(root,"TelemetryGlass",32,32,450,310);
            telemetryPanel=root.Find("TelemetryGlass")?.GetComponent<RawImage>();
            Text(root,"Title","飞行系统",54,48,190,24,15).color=GlassUiPalette.Accent;
            diagnosticHint=Text(root,"DiagnosticHint","F1  性能参数",284,48,178,24,12);diagnosticHint.color=GlassUiPalette.Muted;diagnosticHint.alignment=TextAlignmentOptions.Right;
            speedReadout=Text(root,"SpeedReadout","0.0 m/s",54,77,406,54,34);
            summaryReadout=Text(root,"FlightSummary","质量 — kg\n武器 —\n靶场 3 km · 未锁定",54,139,406,57,13);summaryReadout.textWrappingMode=TextWrappingModes.Normal;summaryReadout.color=GlassUiPalette.Muted;
            info=Text(root,"Telemetry","",54,205,406,126,11);info.textWrappingMode=TextWrappingModes.Normal;info.color=GlassUiPalette.Muted;info.lineSpacing=-1;
            alertReadout=Text(root,"FlightAlert","",54,207,406,22,12);alertReadout.gameObject.SetActive(false);
            lockLabel=Text(root,"TargetLockStatus","F  锁定前方目标",510,48,400,28,16);lockLabel.color=GlassUiPalette.Accent;
            Panel(root,"OwnStatusGlass",32,678,360,238);Text(root,"OwnStatusTitle","自身状态",54,694,316,25,17);
            ownStatus=Rect(root,"OwnShipSchematic",52,730,320,124).gameObject.AddComponent<ShipStatusGraphic>();
            ownCondition=Text(root,"OwnShipCondition","核心正常",54,876,316,22,13);
            Panel(root,"EnergyGlass",32,934,360,114);
            shieldLabel=Text(root,"ShieldValue","护盾  0 / 0",54,942,316,20,13);
            CreateShieldFill(root,54,965,EnergyWidth,8);
            energyLabel=Text(root,"EnergyValue","能量  100%  ·  500 / 500",54,979,316,20,13);
            CreateEnergyFill(root,54,1002,EnergyWidth,10);
            boostLabel=Text(root,"BoostStatus","SHIFT  加速",54,1020,316,18,12);boostLabel.color=GlassUiPalette.Accent;boostLabel.gameObject.SetActive(false);
            BuildTargetMonitor(root,1528,678,360,238,1550,694,1548,742,1550,876,12,124,17,27);
            SetDiagnosticsExpanded(false);
        }
        void CreateShieldFill(RectTransform root,float x,float y,float width,float height)
        {
            var track=Rect(root,"ShieldTrack",x,y,width,height).gameObject.AddComponent<Image>();track.color=refinedFlightLayout?new Color(GlassUiPalette.Accent.r,GlassUiPalette.Accent.g,GlassUiPalette.Accent.b,.16f):new Color(.035f,.12f,.18f,.9f);track.raycastTarget=false;
            shieldFill=Rect(root,"ShieldFill",x,y,width,height).gameObject.AddComponent<RawImage>();shieldFill.texture=Texture2D.whiteTexture;shieldFill.color=refinedFlightLayout?GlassUiPalette.Accent:new Color(.4f,.72f,.8f,.95f);shieldFill.raycastTarget=false;
            shieldFill.rectTransform.anchorMin=shieldFill.rectTransform.anchorMax=new Vector2(x/1920f,(1080f-y-height*.5f)/1080f);shieldFill.rectTransform.pivot=new Vector2(0,.5f);shieldFill.rectTransform.anchoredPosition=Vector2.zero;
        }
        void CreateEnergyFill(RectTransform root,float x,float y,float width,float height)
        {
            var track=Rect(root,"EnergyTrack",x,y,width,height).gameObject.AddComponent<Image>();track.color=refinedFlightLayout?new Color(GlassUiPalette.Accent.r,GlassUiPalette.Accent.g,GlassUiPalette.Accent.b,.16f):new Color(.035f,.12f,.18f,.9f);track.raycastTarget=false;
            energyFill=Rect(root,"EnergyFill",x,y,width,height).gameObject.AddComponent<RawImage>();energyFill.texture=Texture2D.whiteTexture;energyFill.color=refinedFlightLayout?GlassUiPalette.Accent:new Color(.28f,.82f,1,.95f);energyFill.raycastTarget=false;
            energyFill.rectTransform.anchorMin=energyFill.rectTransform.anchorMax=new Vector2(x/1920f,(1080f-y-height*.5f)/1080f);energyFill.rectTransform.pivot=new Vector2(0,.5f);energyFill.rectTransform.anchoredPosition=Vector2.zero;energyFill.rectTransform.sizeDelta=new Vector2(width,height);
        }
        void BuildTargetMonitor(RectTransform root,float px,float py,float pw,float ph,float tx,float ty,float sx,float sy,float cx,float cy,float identitySize=15,float schematicHeight=164,float titleSize=18,float identityOffset=35)
        {
            Panel(root,"TargetStatusGlass",px,py,pw,ph);Text(root,"TargetStatusTitle","锁定目标",tx,ty,316,28,titleSize);
            targetIdentity=Text(root,"TargetIdentity","未锁定目标",tx,ty+identityOffset,316,18,identitySize);targetIdentity.overflowMode=TextOverflowModes.Ellipsis;
            targetStatus=Rect(root,"TargetShipSchematic",sx,sy,320,schematicHeight).gameObject.AddComponent<ShipStatusGraphic>();
            targetCondition=Text(root,"TargetShipCondition","F 锁定前方敌机",cx,cy,316,22,14);
        }
        void SetDiagnosticsExpanded(bool expanded)
        {
            diagnosticsExpanded=expanded;
            float height=expanded?418:220;
            SetPanelHeight(telemetryPanel,32,height);SetRect(telemetryShadow,35,36,450,height);
            SetRect(info.rectTransform,54,205,406,expanded?190:100);
            SetRect(alertReadout.rectTransform,54,expanded?410:207,406,22);
            info.gameObject.SetActive(expanded);
            diagnosticHint.text=expanded?"F1  收起参数":"F1  性能参数";
        }
        void SetPanelHeight(RawImage image,float y,float height)
        {
            if(!image)return;
            var rect=image.rectTransform;rect.anchorMin=new Vector2(32/1920f,(1080f-y-height)/1080f);rect.anchorMax=new Vector2((32+450)/1920f,(1080f-y)/1080f);rect.offsetMin=rect.offsetMax=Vector2.zero;
            var uv=new UnityEngine.Rect(32/1920f,(1080f-y-height)/1080f,450/1920f,height/1080f);image.uvRect=uv;
            if(image.material)image.material.SetVector("_PanelUVRect",new Vector4(uv.x,uv.y,uv.width,uv.height));
        }
        static void SetRect(RectTransform rect,float x,float y,float width,float height)
        {
            rect.anchorMin=new Vector2(x/1920f,(1080f-y-height)/1080f);rect.anchorMax=new Vector2((x+width)/1920f,(1080f-y)/1080f);rect.offsetMin=rect.offsetMax=Vector2.zero;
        }
        public void OpenExit(){modal.SetActive(true);}
        public void CloseExit(){modal.SetActive(false);if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);}
        void Update()
        {
            if(!flight)return;Resize();
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){if(ExitDialogOpen)CloseExit();else OpenExit();}
            if(refinedFlightLayout&&!ExitDialogOpen&&Keyboard.current!=null&&Keyboard.current.f1Key.wasPressedThisFrame)SetDiagnosticsExpanded(!diagnosticsExpanded);
            if(Time.unscaledTime<nextTelemetry)return;nextTelemetry=Time.unscaledTime+.12f;
            var p=flight.Performance;var a=p.accelerationPositive/9.81f;var b=p.accelerationNegative/9.81f;
            string performanceDetails=$"质量  {p.totalMass:0} kg\n推进 g：前 {a.z:0.00}   后 {b.z:0.00}\n             右 {a.x:0.00}   左 {b.x:0.00}\n             上 {a.y:0.00}   下 {b.y:0.00}\n俯仰/偏航  {p.angularRate:0}°/s   {p.pitchAcceleration:0}°/s²\n滚转  {p.rollSpeed:0}°/s";
            if(!refinedFlightLayout)info.text=$"速度  {flight.CurrentSpeed:0.0} / {p.maxSpeed:0} m/s\n{performanceDetails}";
            if(speedReadout)speedReadout.text=refinedFlightLayout?$"{flight.CurrentSpeed:0.0} <size=18>m/s</size>  <color=#{ColorUtility.ToHtmlStringRGB(GlassUiPalette.Muted)}>/ {p.maxSpeed:0}</color>":"";
            if(summaryReadout)summaryReadout.text=$"质量  {p.totalMass:0} kg\n武器  等待战斗\n靶场  3 km · 未锁定";
            float maxEnergy=Mathf.Max(0,flight.MaxEnergy),energy=Mathf.Clamp(flight.Energy,0,maxEnergy),ratio=maxEnergy>0?energy/maxEnergy:0;
            if(energyLabel)energyLabel.text=maxEnergy>0?(refinedFlightLayout?$"能量  {ratio:P0}  ·  {energy:0} / {maxEnergy:0}":$"能量  {ratio:P0} · {energy:0} / {maxEnergy:0}"):"无电源 · 安装电源后可加速/开火";
            if(energyFill)energyFill.rectTransform.sizeDelta=new Vector2(EnergyWidth*ratio,refinedFlightLayout?10:14);
            if(boostLabel)boostLabel.gameObject.SetActive(flight.IsBoosting);
            if(refinedFlightLayout){info.text="";if(alertReadout){alertReadout.text="";alertReadout.gameObject.SetActive(false);}}
            if(combat){var player=combat.Player;int present=0,live=0;foreach(var part in player.ship.Parts)if(part.present)present++;foreach(var bot in combat.Bots)if(bot.ship.IsAlive)live++;
                float capacity=player.ship.Shields.Capacity,charge=player.ship.Shields.Charge;
                shieldLabel.text=capacity>0?$"护盾  {charge:0} / {capacity:0}":"无护盾 · 安装护盾发生器";
                shieldFill.rectTransform.sizeDelta=new Vector2(EnergyWidth*(capacity>0?charge/capacity:0),refinedFlightLayout?8:10);
                var targeting=combat.Targeting;bool locked=targeting&&targeting.HasTarget;
                if(lockLabel)lockLabel.text=locked?$"F 解除锁定 · {targeting.TargetDistance:0} m":"F 锁定前方目标 · 3 km";
                ownStatus.Bind(player.ship);ownCondition.text=Condition(player.ship);ownCondition.color=player.ship.IsAlive?(refinedFlightLayout?GlassUiPalette.Accent:new Color(.48f,.85f,1)):(refinedFlightLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.25f,.2f));
                var target=locked?targeting.LockedTarget:null;targetStatus.Bind(target);
                targetIdentity.text=target?$"{TargetName(target)}  ·  {targeting.TargetDistance:0} m":"未锁定目标";
                targetCondition.text=target?Condition(target):"F 锁定前方敌机";
                radarLabel.text=$"3 km · 敌机 {live}  |  {(locked?"◇ 已锁定":"未锁定")}";
                if(leadLabel){var leads=combat.Targeting?combat.Targeting.LeadPoints:null;var labels=new List<string>();if(leads!=null)foreach(var lead in leads)if(lead.weapon){int i=labels.Count;string tint=i%4==0?"4DE0FF":i%4==1?"FFC257":i%4==2?"C2A1FF":"78FFB8";labels.Add($"<color=#{tint}>{(i%2==0?"◇":"□")}</color> {lead.weapon.projectileSpeed:0} m/s");}leadLabel.text=string.Join("   ",labels);}
                string combatDetails=$"\n部件 {present}/{player.ship.Parts.Count}   武器 {player.weapons.ActiveGuns} / 遮挡 {player.weapons.BlockedGuns}\n伤害  造成 {player.ship.DamageDealt:0} / 受到 {player.ship.DamageTaken:0}\n靶机 {live}/{combat.Bots.Count} · 已击毁 {combat.BotKills}";
                string alert=!player.ship.IsAlive?$"核心已毁 · {combat.PlayerRespawnRemaining:0.0}s 后重生":live<combat.Bots.Count?"靶机正在重建":"";
                if(!refinedFlightLayout)
                {
                    info.text+=combatDetails;
                    if(!player.ship.IsAlive)info.text+=$"\n核心已毁 · {combat.PlayerRespawnRemaining:0.0}s 后重生";
                    else if(live<combat.Bots.Count)info.text+="\n被击毁的靶机正在等待重生";
                }
                else
                {
                    summaryReadout.text=$"质量  {p.totalMass:0} kg\n武器  {player.weapons.ActiveGuns} 就绪 · 遮挡 {player.weapons.BlockedGuns}\n靶场  3 km · {(locked?$"锁定 {targeting.TargetDistance:0} m":"未锁定")}";
                    if(diagnosticsExpanded)info.text=performanceDetails+combatDetails;
                    if(alertReadout&&alert.Length>0){alertReadout.text=alert;alertReadout.color=!player.ship.IsAlive?Starframe.Presentation.UiTheme.Current.danger:Starframe.Presentation.UiTheme.Current.amber;alertReadout.gameObject.SetActive(true);}
                }
            }
        }
        string TargetName(FlightShipRuntime ship){foreach(var actor in combat.Actors)if(actor.ship==ship)return actor.blueprintName??"敌方飞船";return "敌方飞船";}
        string Condition(FlightShipRuntime ship)
        {
            int count=0,damaged=0;foreach(var p in ship.Parts)if(p.present){count++;if(p.hp<p.maxHp-.001f)damaged++;}
            Color damageColor=damaged>0?(refinedFlightLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.38f,.33f)):GlassUiPalette.Muted;
            string damageHex=ColorUtility.ToHtmlStringRGB(damageColor);
            return $"{(ship.IsAlive?"核心在线":"核心已毁")} · 部件 {count}/{ship.Parts.Count} · <color=#{damageHex}>受损 {damaged}</color>";
        }
        void LateUpdate()
        {
            if(!combat)return;
            while(targetLabels.Count<combat.Bots.Count){
                int i=targetLabels.Count;var label=Text((RectTransform)canvas.transform,"BotMarker"+i,"",0,0,360,28,18);label.alignment=TextAlignmentOptions.Center;label.fontStyle=FontStyles.Bold;
                label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.5f,.5f);label.rectTransform.sizeDelta=new Vector2(360,28);label.transform.SetSiblingIndex(modal.transform.GetSiblingIndex());targetLabels.Add(label);
            }
            for(int i=0;i<targetLabels.Count;i++){
                var label=targetLabels[i];if(i>=combat.Bots.Count){label.gameObject.SetActive(false);continue;}
                var bot=combat.Bots[i];var point=worldCamera.WorldToScreenPoint(bot.body.position+Vector3.up*(bot.ship.HullRadius+1));
                bool visible=bot.ship.IsAlive&&point.z>0&&point.x>0&&point.x<Screen.width&&point.y>0&&point.y<Screen.height;label.gameObject.SetActive(visible);
                if(visible){label.text=bot.blueprintName+" · "+(i+1);if(RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,point,displayCamera,out var local))label.rectTransform.anchoredPosition=local;}
            }
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
            var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=GlassUiPalette.Ink;
            t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Overflow;return t;
        }
        RectTransform Panel(RectTransform parent,string name,float x,float y,float w,float h)
        {
            var shadow=Rect(parent,name+"Shadow",x+3,y+4,w,h).gameObject.AddComponent<Image>();shadow.color=new Color(0,0,0,.08f);shadow.raycastTarget=false;
            var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<RawImage>();image.raycastTarget=false;
            var material=new Material(Shader.Find("Starframe/UI/BackdropBlur"));GlassUiPalette.Apply(material,name=="ExitGlass"?GlassUiPalette.Dialog:GlassUiPalette.Glass);
            material.SetVector("_PanelUVRect",new Vector4(x/1920,(1080-y-h)/1080,w/1920,h/1080));
            image.uvRect=new UnityEngine.Rect(x/1920,(1080-y-h)/1080,w/1920,h/1080);image.material=material;materials.Add(material);glass.Add(image);return shadow.rectTransform;
        }
        void Button(RectTransform parent,string name,string value,float x,float y,float w,float h,Action action,bool primary=false)
        {
            var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.color=primary?Starframe.Presentation.UiTheme.Current.accent:GlassUiPalette.Button;
            var button=image.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.navigation=new Navigation{mode=Navigation.Mode.None};button.onClick.AddListener(()=>action());
            var text=Text(parent,name+"Text",value,x,y+7,w,h-14,17);text.alignment=TextAlignmentOptions.Center;
            if(primary&&refinedFlightLayout)text.color=new Color(.045f,.075f,.08f,1);
        }
        void OnDestroy()
        {
            if(worldCamera)worldCamera.targetTexture=null;if(sceneTexture){sceneTexture.Release();Destroy(sceneTexture);}
            foreach(var m in materials)Destroy(m);if(fallback)Destroy(fallback);
            if(font){font.atlasTextures=Array.Empty<Texture2D>();font.material=null;Destroy(font);}
        }
    }
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FlightAimGraphic:UnityEngine.UI.Graphic
    {
        Camera viewCamera;ShipFlightController flight;Canvas ownerCanvas;
        Vector2 actualPoint,desiredPoint,displayedOffset;bool positioned,refinedReticle;
        public void Initialize(Camera view,ShipFlightController ship){viewCamera=view;flight=ship;ownerCanvas=GetComponentInParent<Canvas>();raycastTarget=false;color=Color.white;refinedReticle=Starframe.Presentation.UiTheme.Current.aimReticle;}
        void LateUpdate()
        {
            if(!flight||!viewCamera||!ownerCanvas)return;
            var uiCamera=ownerCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:ownerCanvas.worldCamera;
            // Project direction vectors, avoiding parallax from the elevated third-person camera.
            var forward=Starframe.Combat.AimProjection.ScreenPoint(viewCamera,flight.Forward);
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
            if(refinedReticle)
            {
                // Projection and cursor positions above are shared with the legacy graphic; only the drawing changes.
                var theme=Starframe.Presentation.UiTheme.Current;var accent=new Color(theme.accent.r,theme.accent.g,theme.accent.b,.94f);var cursor=new Color(theme.text.r,theme.text.g,theme.text.b,.84f);
                Line(vh,a+Vector2.left*15*s,a+Vector2.left*6*s,4*s,accent);
                Line(vh,a+Vector2.right*6*s,a+Vector2.right*15*s,4*s,accent);
                Line(vh,a+Vector2.down*15*s,a+Vector2.down*6*s,4*s,accent);
                Line(vh,a+Vector2.up*6*s,a+Vector2.up*15*s,4*s,accent);
                Rect(vh,a+new Vector2(-2,-2)*s,new Vector2(4,4)*s,accent);
                bool showCursor=(b-a).sqrMagnitude>=64*s*s;
                if(showCursor)
                {
                    Line(vh,a,b,2*s,new Color(theme.accent.r,theme.accent.g,theme.accent.b,.24f));
                    float gap=9,edge=13,thickness=3*s;
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
            Line(vh,a+Vector2.left*12*s,a+Vector2.right*12*s,4*s);Line(vh,a+Vector2.down*12*s,a+Vector2.up*12*s,4*s);
            var lowerLeft=b+new Vector2(-8,-8)*s;var upperLeft=b+new Vector2(-8,8)*s;
            var upperRight=b+new Vector2(8,8)*s;var lowerRight=b+new Vector2(8,-8)*s;
            Line(vh,lowerLeft,upperLeft,3.5f*s);Line(vh,upperLeft,upperRight,3.5f*s);
            Line(vh,upperRight,lowerRight,3.5f*s);Line(vh,lowerRight,lowerLeft,3.5f*s);
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
