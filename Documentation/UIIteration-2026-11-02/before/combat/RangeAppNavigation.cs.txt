// @role Open live range options and settings while suppressing player input.
// @calls RangeCombatWorld, RangeHUD, MenuSurface, SettingsScreen

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
            overlay.Label("RangeEyebrow",L10n.T("app.rangeOptions"),172,149,1200,26,14,GlassUiPalette.Accent);
            overlay.Panel("PracticeOptionsGlass",140,196,1640,640,true);
            overlay.Label("PracticeTitle",L10n.T("app.rangeTitle"),180,228,1500,48,32);
            overlay.Label("PracticeHint",L10n.T("app.rangeLiveHint"),180,295,1500,66,17,GlassUiPalette.Muted);
            for(int i=0;i<4;i++)
            {
                var mode=(RangeBotMode)i;float x=180+i*394;
                overlay.Button("LiveMode_"+mode,L10n.T("app.mode."+mode),x,398,368,58,()=>{world.SetBotMode(mode);BuildPractice();},world.BotMode==mode);
                overlay.Label("LiveModeDescription_"+mode,L10n.T("app.modeHint."+mode),x+12,478,344,126,16,GlassUiPalette.Muted);
            }
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
