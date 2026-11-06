// @role Open live range options and settings while suppressing player input.
// @calls RangeCombatWorld, RangeHUD, MenuSurface, SettingsScreen, FigmaVisuals

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Starframe.Combat;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.App
{
    /// <summary>Live drill controls reuse the menu surface without taking ownership of the HUD render target.</summary>
    public sealed class RangeAppNavigation:MonoBehaviour
    {
        RangeHUD hud;RangeCombatWorld world;MenuSurface overlay;SettingsScreen settings;
        bool previousSuspended;int openedFrame;
        public bool IsOpen=>overlay;
        public void Initialize(RangeHUD display,RangeCombatWorld combat)
        {
            hud=display;world=combat;hud.SettingsRequested+=OpenSettings;hud.PracticeOptionsRequested+=OpenPractice;
            L10n.Changed+=RefreshLocale;
        }
        void CreateOverlay()
        {
            if(overlay)return;
            previousSuspended=GameInput.Suspended;GameInput.Suspended=true;hud.InputOverlayOpen=true;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;openedFrame=Time.frameCount;
            overlay=new GameObject("RangeOptionsOverlay",typeof(RectTransform)).AddComponent<MenuSurface>();
            overlay.InitializeOverlay(hud.RootCanvas,hud.WorldCamera);
            overlay.OperationFailed+=error=>ShowStorageError();
        }
        public void OpenSettings()
        {
            if(IsOpen)return;CreateOverlay();settings=overlay.gameObject.AddComponent<SettingsScreen>();settings.Initialize(overlay,Close);
        }
        public void OpenPractice()
        {
            if(IsOpen)return;CreateOverlay();BuildPractice();
        }
        void BuildPractice()
        {
            overlay.Clear();
            overlay.Label("RangeEyebrow",L10n.T("app.rangeOptions"),180,149,1200,26,14,GlassUiPalette.Muted);
            overlay.Panel("PracticeOptionsGlass",140,196,1640,640,true);
            overlay.Label("PracticeTitle",L10n.T("app.rangeTitle"),180,228,1500,48,32);
            overlay.Label("PracticeHint",L10n.T("app.rangeLiveHint"),180,295,1500,66,17,GlassUiPalette.Muted);
            bool attack=world.BotMode==RangeBotMode.StationaryFire||world.BotMode==RangeBotMode.Combat;
            bool move=world.BotMode==RangeBotMode.FlyOnly||world.BotMode==RangeBotMode.Combat;
            void SetBehavior(bool nextAttack,bool nextMove)
            {
                world.SetBotMode(nextAttack?(nextMove?RangeBotMode.Combat:RangeBotMode.StationaryFire):(nextMove?RangeBotMode.FlyOnly:RangeBotMode.Stationary));BuildPractice();
            }
            void Behavior(string name,string title,string description,bool enabled,float x,Action change)
            {
                overlay.Panel(name+"Card",x,376,754,228);
                var heading=overlay.Label(name+"Title",title,x+28,400,500,42,28,GlassUiPalette.Accent);FigmaVisuals.SetFont(heading,true);
                overlay.Label(name+"Description",description,x+28,478,494,88,18,GlassUiPalette.Muted);
                var button=overlay.Button(name,"",x+610,438,90,44,change);
                button.GetComponent<UnityEngine.UI.Image>().color=new Color(52/255f,57/255f,77/255f);
                var thumb=MenuSurface.Rect(overlay.Root,name+"Thumb",x+(enabled?661:616),444,33,32).gameObject.AddComponent<UnityEngine.UI.Image>();
                thumb.color=enabled?GlassUiPalette.Accent:new Color(.56f,.56f,.56f);thumb.raycastTarget=false;
                overlay.Hint(button,title+": "+(enabled?(L10n.IsEnglish?"ON":"开启"):(L10n.IsEnglish?"OFF":"关闭")));
            }
            Behavior("LiveAttack",L10n.IsEnglish?"ATTACK":"攻击",L10n.IsEnglish?"Allow targets to fire. Movement is controlled separately.":"允许靶机开火。移动行为独立控制。",attack,180,()=>SetBehavior(!attack,move));
            Behavior("LiveMove",L10n.IsEnglish?"MOVE":"移动",L10n.IsEnglish?"Allow targets to fly and maneuver. Attack is controlled separately.":"允许靶机飞行和机动。攻击行为独立控制。",move,974,()=>SetBehavior(attack,!move));
            overlay.Label("LiveTargetCount",L10n.T("app.rangeLiveCount",world.Bots.Count),180,636,1450,32,18);
            overlay.Label("LiveResetHint",L10n.T("app.rangeResetHint"),180,685,1450,42,15,GlassUiPalette.Muted);
            overlay.Button("ResetRangeTargets",L10n.T("app.rangeReset"),180,750,500,50,()=>{world.ResetTargets();BuildPractice();});
            overlay.Button("CloseRangeOptions",L10n.T("settings.back"),1260,750,480,50,Close,true);
        }
        void ShowStorageError()
        {
            // Keep the player's current screen and draft intact when a preference cannot be saved.
            if(!overlay)return;
            overlay.Label("OptionsStorageError",L10n.T("app.storageError"),180,870,1500,60,16,UiTheme.Current.danger);
        }
        void RefreshLocale(){if(IsOpen&&!settings)BuildPractice();}
        public void Close()
        {
            if(!overlay)return;
            if(settings&&settings.IsOpen){settings.Close();return;}
            if(settings){settings.enabled=false;Destroy(settings);settings=null;}
            overlay.gameObject.SetActive(false);Destroy(overlay.gameObject);overlay=null;
            hud.InputOverlayOpen=false;GameInput.Suspended=previousSuspended;
        }
        void Update()
        {
            if(IsOpen&&!settings&&Time.frameCount>openedFrame&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)Close();
        }
        void OnDestroy()
        {
            L10n.Changed-=RefreshLocale;
            if(hud){hud.SettingsRequested-=OpenSettings;hud.PracticeOptionsRequested-=OpenPractice;}
            if(overlay){overlay.gameObject.SetActive(false);Destroy(overlay.gameObject);GameInput.Suspended=previousSuspended;}
        }
    }
}
