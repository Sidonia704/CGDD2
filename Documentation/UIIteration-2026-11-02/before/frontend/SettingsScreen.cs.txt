// @role Edit and apply the settings draft with conflict and storage feedback.
// @calls GameSettings, GameInput, GameAudio

using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Starframe.Presentation;
using Starframe.Flight;

namespace Starframe.App
{
    /// <summary>Staged preferences, input capture, and a timed display-change recovery.</summary>
    public sealed class SettingsScreen:MonoBehaviour
    {
        enum Category { Display, Graphics, Audio, Controls, Accessibility, Language }
        enum Dialog { None, Capture, Conflict, WeaponConflict, Display, Discard }
        struct DisplayResolution { public int width,height,hz;public DisplayResolution(int w,int h,int rate){width=w;height=h;hz=rate;} }
        MenuSurface surface;Action onClose;
        GameSettingsData baseline,draft,beforeDisplay;
        Category category;Dialog dialog;
        RectTransform content,modal;
        RectTransform captureCancelRect;
        TextMeshProUGUI status,captureHint,displayCountdown;
        UnityEngine.UI.Button applyButton,revertButton;
        GameAction captureAction,conflictAction;string pendingPath,captureError;
        float displayDeadline;int captureFrame;
        bool open,previousSuspended,rebuildRequested;
        public bool IsOpen=>open;
        public GameSettingsData Draft=>draft;
        bool Dirty=>baseline!=null&&draft!=null&&JsonUtility.ToJson(baseline)!=JsonUtility.ToJson(draft);

        public void Initialize(MenuSurface target,Action closed){surface=target;onClose=closed;Open();}
        public void Open()
        {
            if(open)return;
            baseline=GameSettings.Current.Clone();draft=baseline.Clone();category=Category.Display;dialog=Dialog.None;
            previousSuspended=GameInput.Suspended;GameInput.Suspended=true;open=true;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            L10n.Changed+=LocaleChanged;Build();
        }
        void LocaleChanged(){if(open)rebuildRequested=true;}
        void Build()
        {
            if(!surface||!open)return;
            surface.Clear();modal=null;content=null;
            surface.Label("SettingsEyebrow",L10n.T("settings.eyebrow"),48,33,1200,24,13,GlassUiPalette.Accent);
            surface.Label("SettingsTitle",L10n.T("settings.title"),48,67,1000,48,32);
            surface.Label("SettingsSubtitle",L10n.T("settings.subtitle"),48,123,1600,25,14,GlassUiPalette.Muted);
            surface.Panel("SettingsNavigation",48,174,284,742);
            surface.Panel("SettingsContent",356,174,1516,742,true);
            string[] categories={"Display","Graphics","Audio","Controls","Accessibility","Language"};
            for(int i=0;i<categories.Length;i++)
            {
                var value=(Category)i;
                surface.Button("SettingsTab"+value,L10n.T("settings.category."+value),68,198+i*68,244,52,()=>{category=value;Build();},category==value);
            }
            surface.Label("SettingsTabNote",L10n.T("settings.stagedNote"),72,714,232,158,14,GlassUiPalette.Muted);
            surface.Label("SettingsSectionTitle",L10n.T("settings.category."+category),396,204,1100,34,23);
            surface.Label("SettingsSectionDescription",L10n.T("settings.description."+category),396,247,1416,46,15,GlassUiPalette.Muted);
            surface.Scroll("SettingsScroll",396,308,1436,580,out content);
            var layout=content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            layout.spacing=8;layout.padding=new RectOffset(0,8,0,16);
            var fitter=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            switch(category)
            {
                case Category.Display:BuildDisplay();break;
                case Category.Graphics:BuildGraphics();break;
                case Category.Audio:BuildAudio();break;
                case Category.Controls:BuildControls();break;
                case Category.Accessibility:BuildAccessibility();break;
                case Category.Language:BuildLanguage();break;
            }
            surface.Panel("SettingsFooter",48,940,1824,96);
            surface.Button("SettingsBack",L10n.T("settings.back"),68,963,210,50,RequestClose);
            surface.Button("SettingsDefaults",L10n.T("settings.defaults"),300,963,226,50,RestoreDefaults);
            status=surface.Label("SettingsStatus",L10n.T(Dirty?"settings.unsaved":"settings.applied"),556,972,790,31,15,GlassUiPalette.Muted);
            revertButton=surface.Button("SettingsRevert",L10n.T("settings.revert"),1378,963,206,50,Revert);
            applyButton=surface.Button("SettingsApply",L10n.T("settings.apply"),1606,963,246,50,Apply,true);
            MarkChanged(false);BuildDialog();
            Canvas.ForceUpdateCanvases();UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }
        RectTransform Row(string key,string hint=null,float height=88)
        {
            var go=new GameObject("Setting_"+key,typeof(RectTransform),typeof(UnityEngine.UI.LayoutElement));go.layer=5;
            var rect=(RectTransform)go.transform;rect.SetParent(content,false);
            var element=go.GetComponent<UnityEngine.UI.LayoutElement>();element.minHeight=element.preferredHeight=height;
            Local(surface.Label(key+"Label",L10n.T("settings."+key),0,0,1,1,17),rect,0,8,790,28);
            if(hint!=null)Local(surface.Label(key+"Hint",L10n.T("settings."+hint),0,0,1,1,14,GlassUiPalette.Muted),rect,0,41,790,height-44);
            return rect;
        }
        static void Local(Component child,RectTransform parent,float x,float y,float w,float h)
        {
            var rect=(RectTransform)child.transform;rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);
            rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);
        }
        void Dropdown(RectTransform row,string name,List<string> labels,int selected,Action<int> set)
        {
            var dropdown=surface.Dropdown(name,labels,selected,0,0,1,1,value=>{set(value);MarkChanged();});
            Local(dropdown,row,930,12,410,46);
            L10n.ApplyUserContentFont(dropdown.captionText);L10n.ApplyUserContentFont(dropdown.itemText);
        }
        void ToggleRow(string name,bool value,Action<bool> set,string hint=null)
        {
            var row=Row(name,hint);
            Local(surface.Toggle(name+"Toggle",L10n.T("settings.enabled"),value,0,0,1,1,next=>{set(next);MarkChanged();}),row,1140,12,200,46);
        }
        void SliderRow(string name,float min,float max,float value,Action<float> set,Func<float,string> format,string hint=null,bool enabled=true)
        {
            var row=Row(name,hint);
            var number=surface.Label(name+"Value",format(value),0,0,1,1,16);Local(number,row,1242,20,98,30);number.alignment=TextAlignmentOptions.Right;
            var slider=surface.Slider(name+"Slider",min,max,value,0,0,1,1,next=>{set(next);number.text=format(next);MarkChanged();});
            slider.interactable=enabled;Local(slider,row,930,17,280,38);
        }
        void BuildDisplay()
        {
            int[] modes={(int)FullScreenMode.Windowed,(int)FullScreenMode.FullScreenWindow,(int)FullScreenMode.ExclusiveFullScreen};
            Dropdown(Row("screenMode","screenModeHint"),"ScreenMode",new List<string>{L10n.T("settings.windowed"),L10n.T("settings.borderless"),L10n.T("settings.fullscreen")},Mathf.Max(0,Array.IndexOf(modes,draft.fullscreenMode)),index=>draft.fullscreenMode=modes[index]);
            var resolutions=ResolutionOptions();int selected=resolutions.FindIndex(value=>value.width==draft.resolutionWidth&&value.height==draft.resolutionHeight&&value.hz==draft.refreshRate);
            Dropdown(Row("resolution","resolutionHint"),"Resolution",resolutions.Select(value=>value.width+" × "+value.height+"  ·  "+value.hz+" Hz").ToList(),Mathf.Max(0,selected),index=>{draft.resolutionWidth=resolutions[index].width;draft.resolutionHeight=resolutions[index].height;draft.refreshRate=resolutions[index].hz;});
            Dropdown(Row("vsync","vsyncHint"),"VSync",new List<string>{L10n.T("settings.off"),L10n.T("settings.vsyncOne"),L10n.T("settings.vsyncTwo")},draft.vSyncCount,index=>{draft.vSyncCount=index;rebuildRequested=true;});
            int[] caps={-1,30,60,90,120,144,165,240,360};
            var capList=caps.ToList();if(!capList.Contains(draft.targetFrameRate))capList.Add(draft.targetFrameRate);
            var capRow=Row("frameCap","frameCapHint");
            var dropdown=surface.Dropdown("FrameCap",capList.Select(value=>value<0?L10n.T("settings.unlimited"):value+" FPS").ToList(),Mathf.Max(0,capList.IndexOf(draft.targetFrameRate)),0,0,1,1,index=>{draft.targetFrameRate=capList[index];MarkChanged();});
            dropdown.interactable=draft.vSyncCount==0;Local(dropdown,capRow,930,12,410,46);
            if(Application.isEditor)Note("editorDisplayNote");
        }
        List<DisplayResolution> ResolutionOptions()
        {
            var result=new List<DisplayResolution>();
            foreach(var resolution in Screen.resolutions)
            {
                if(resolution.width<640||resolution.height<480)continue;
                int hz=Mathf.Max(24,(int)Math.Round(resolution.refreshRateRatio.value));
                if(!result.Any(value=>value.width==resolution.width&&value.height==resolution.height&&value.hz==hz))result.Add(new DisplayResolution(resolution.width,resolution.height,hz));
            }
            if(!result.Any(value=>value.width==draft.resolutionWidth&&value.height==draft.resolutionHeight&&value.hz==draft.refreshRate))result.Add(new DisplayResolution(draft.resolutionWidth,draft.resolutionHeight,draft.refreshRate));
            result.Sort((a,b)=>{int order=b.width.CompareTo(a.width);if(order==0)order=b.height.CompareTo(a.height);return order==0?b.hz.CompareTo(a.hz):order;});return result;
        }
        void BuildGraphics()
        {
            var names=QualitySettings.names.ToList();
            if(names.Count>0)Dropdown(Row("quality","qualityHint"),"Quality",names,Mathf.Clamp(draft.qualityLevel,0,names.Count-1),index=>draft.qualityLevel=index);
            SliderRow("fieldOfView",45,100,draft.fieldOfView,value=>draft.fieldOfView=Mathf.Round(value),value=>Mathf.RoundToInt(value)+"°","fieldOfViewHint");
            ToggleRow("bloom",draft.bloomEnabled,value=>{draft.bloomEnabled=value;rebuildRequested=true;},"bloomHint");
            SliderRow("bloomIntensity",0,2,draft.bloomIntensity,value=>draft.bloomIntensity=value,value=>value.ToString("0.00"),"bloomIntensityHint",draft.bloomEnabled);
        }
        void BuildAudio()
        {
            SliderRow("masterVolume",0,1,draft.masterVolume,value=>{draft.masterVolume=value;GameAudio.Preview(draft);},Percent);
            SliderRow("musicVolume",0,1,draft.musicVolume,value=>{draft.musicVolume=value;GameAudio.Preview(draft);},Percent,"musicHint");
            SliderRow("sfxVolume",0,1,draft.sfxVolume,value=>{draft.sfxVolume=value;GameAudio.Preview(draft);},Percent,"sfxHint");
            SliderRow("uiVolume",0,1,draft.uiVolume,value=>{draft.uiVolume=value;GameAudio.Preview(draft);},Percent,"uiHint");
            ToggleRow("muteUnfocused",draft.muteWhenUnfocused,value=>{draft.muteWhenUnfocused=value;GameAudio.Preview(draft);},"muteUnfocusedHint");
            var row=Row("testAudio",null,68);
            Local(surface.Button("TestSfx",L10n.T("settings.testSfx"),0,0,195,44,()=>{GameAudio.Preview(draft);GameAudio.PreviewSfx();}),row,930,8,195,44);
            Local(surface.Button("TestUi",L10n.T("settings.testUi"),0,0,195,44,()=>{GameAudio.Preview(draft);GameAudio.PlayUiClick();}),row,1145,8,195,44);
        }
        static string Percent(float value)=>Mathf.RoundToInt(value*100)+"%";
        void BuildControls()
        {
            SliderRow("sensitivity",.2f,3,draft.mouseSensitivity,value=>draft.mouseSensitivity=value,value=>value.ToString("0.00")+"×","sensitivityHint");
            ToggleRow("invertY",draft.invertY,value=>draft.invertY=value,"invertYHint");
            SliderRow("smoothing",0,3,draft.aimSmoothing,value=>draft.aimSmoothing=value,value=>value.ToString("0.00")+"×","smoothingHint");
            Note("bindingHint");
            var world=FindFirstObjectByType<Starframe.Combat.RangeCombatWorld>();
            var blueprint=world&&world.Player!=null?world.Player.ship.BlueprintSnapshot:ShipWorkspace.Ship;
            foreach(GameAction action in Enum.GetValues(typeof(GameAction)))
            {
                var binding=GameInput.Get(action,draft);bool weaponConflict=GameInput.HasWeaponBindingConflict(blueprint,binding.path)||GameInput.HasWeaponBindingConflict(blueprint,binding.alternatePath);
                var selected=action;var row=Row("action."+action,null,weaponConflict?100:66);
                Local(surface.Button("Binding"+action,GameInput.BindingLabel(action,draft),0,0,410,46,()=>BeginCapture(selected)),row,930,4,410,46);
                if(weaponConflict)Local(surface.Label("WeaponBindingWarning_"+action,L10n.T("settings.weaponBindingWarning"),0,0,1,1,13,UiTheme.Current.amber),row,0,51,1340,42);
            }
        }
        void BuildAccessibility()
        {ToggleRow("reduceFlashes",draft.reduceFlashes,value=>draft.reduceFlashes=value,"reduceFlashesHint");Note("accessibilityNote");}
        void BuildLanguage()
        {
            Dropdown(Row("language","languageHint"),"Language",new List<string>{"English","简体中文"},draft.localeCode=="zh-Hans"?1:0,index=>draft.localeCode=index==1?"zh-Hans":"en");
            Note("languageNote");
        }
        void Note(string key)
        {
            var go=new GameObject("Note_"+key,typeof(RectTransform),typeof(UnityEngine.UI.LayoutElement));go.layer=5;go.transform.SetParent(content,false);
            var element=go.GetComponent<UnityEngine.UI.LayoutElement>();element.preferredHeight=element.minHeight=78;
            Local(surface.Label(key,L10n.T("settings."+key),0,0,1,1,14,GlassUiPalette.Muted),(RectTransform)go.transform,0,12,1340,62);
        }
        void MarkChanged(bool clearStatus=true)
        {
            if(applyButton)applyButton.interactable=Dirty;
            if(revertButton)revertButton.interactable=Dirty;
            if(status&&clearStatus)status.text=L10n.T(Dirty?"settings.unsaved":"settings.applied");
        }
        void RestoreDefaults(){draft=GameSettings.Defaults();draft.Validate();GameAudio.Preview(draft);Build();}
        void Revert(){draft=baseline.Clone();GameAudio.Apply();Build();}
        bool DisplayChanged()=>draft.fullscreenMode!=baseline.fullscreenMode||draft.resolutionWidth!=baseline.resolutionWidth||draft.resolutionHeight!=baseline.resolutionHeight||draft.refreshRate!=baseline.refreshRate;
        void Apply()
        {
            draft.Validate();bool checkDisplay=!Application.isEditor&&DisplayChanged();
            beforeDisplay=checkDisplay?baseline.Clone():null;
            GameSettings.SetCurrent(draft);GameSettings.Apply();
            if(checkDisplay){displayDeadline=Time.unscaledTime+15;dialog=Dialog.Display;Build();return;}
            FinishApply();
        }
        void FinishApply()
        {
            bool saved=GameSettings.Save();
            if(saved){baseline=GameSettings.Current.Clone();draft=baseline.Clone();}
            else{GameSettings.SetCurrent(baseline);GameSettings.Apply();}
            beforeDisplay=null;dialog=Dialog.None;Build();
            if(status)status.text=L10n.T(saved?"settings.saved":"settings.saveFailed");
        }
        void RevertDisplay()
        {
            if(beforeDisplay!=null){GameSettings.SetCurrent(beforeDisplay);GameSettings.Apply();baseline=beforeDisplay.Clone();draft=baseline.Clone();}
            beforeDisplay=null;dialog=Dialog.None;Build();if(status)status.text=L10n.T("settings.displayRestored");
        }
        void RequestClose()
        {
            if(dialog==Dialog.Display){RevertDisplay();return;}
            if(Dirty){dialog=Dialog.Discard;BuildDialog();}else Close();
        }
        public void Close()
        {
            if(!open)return;open=false;L10n.Changed-=LocaleChanged;
            if(beforeDisplay!=null){GameSettings.SetCurrent(beforeDisplay);GameSettings.Apply();beforeDisplay=null;}
            GameAudio.Apply();GameInput.Suspended=previousSuspended;onClose?.Invoke();
        }
        void BeginCapture(GameAction action)
        {captureAction=action;captureFrame=Time.frameCount;captureError=null;pendingPath=null;dialog=Dialog.Capture;BuildDialog();}
        void CancelCapture(){dialog=Dialog.None;pendingPath=null;captureError=null;DestroyModal();}
        void Capture(string path)
        {
            if(GameInput.IsReserved(path)){captureError="settings.reserved";if(captureHint)captureHint.text=L10n.T(captureError);return;}
            if(!GameInput.IsSupported(path))return;
            var conflicts=GameInput.Conflicts(draft,captureAction,path);
            if(conflicts.Count>1){captureError="settings.multipleConflicts";if(captureHint)captureHint.text=L10n.T(captureError);return;}
            if(conflicts.Count==1){conflictAction=conflicts[0];pendingPath=path;dialog=Dialog.Conflict;BuildDialog();return;}
            var world=FindFirstObjectByType<Starframe.Combat.RangeCombatWorld>();
            var blueprint=world&&world.Player!=null?world.Player.ship.BlueprintSnapshot:ShipWorkspace.Ship;
            if(GameInput.HasWeaponBindingConflict(blueprint,path)){pendingPath=path;dialog=Dialog.WeaponConflict;BuildDialog();return;}
            var binding=GameInput.Get(captureAction,draft);binding.path=path;binding.alternatePath=null;dialog=Dialog.None;Build();
        }
        void UseSharedWeaponBinding()
        {
            var binding=GameInput.Get(captureAction,draft);binding.path=pendingPath;binding.alternatePath=null;pendingPath=null;dialog=Dialog.None;Build();
        }
        void SwapBinding()
        {
            var current=GameInput.Get(captureAction,draft);var other=GameInput.Get(conflictAction,draft);
            string old=current.path,alternate=current.alternatePath;current.path=pendingPath;current.alternatePath=null;other.path=old;other.alternatePath=alternate;
            dialog=Dialog.None;pendingPath=null;Build();
        }
        void DestroyModal(){if(modal){modal.gameObject.SetActive(false);Destroy(modal.gameObject);}modal=null;}
        T InModal<T>(T component) where T:Component{component.transform.SetParent(modal,false);return component;}
        void BuildDialog()
        {
            DestroyModal();captureCancelRect=null;if(dialog==Dialog.None)return;
            modal=MenuSurface.Rect(surface.Root,"SettingsModal",0,0,1920,1080);
            var blocker=modal.gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.62f);blocker.raycastTarget=true;
            InModal(surface.Panel("SettingsDialog",540,354,840,372,true));
            string title=dialog==Dialog.Display?"settings.keepDisplay":dialog==Dialog.Discard?"settings.discardTitle":dialog==Dialog.WeaponConflict?"settings.weaponConflictTitle":dialog==Dialog.Conflict?"settings.conflictTitle":"settings.captureTitle";
            InModal(surface.Label("SettingsDialogTitle",L10n.T(title),580,390,760,42,25));
            if(dialog==Dialog.Capture)
            {
                InModal(surface.Label("CaptureAction",L10n.T("settings.captureAction",L10n.T("settings.action."+captureAction)),580,449,760,30,18,GlassUiPalette.Accent));
                captureHint=InModal(surface.Label("CaptureHint",L10n.T(captureError??"settings.captureHint"),580,499,760,82,16,GlassUiPalette.Muted));
                captureCancelRect=(RectTransform)InModal(surface.Button("CancelCapture",L10n.T("ui.cancel"),580,632,760,50,CancelCapture)).transform;
            }
            else if(dialog==Dialog.Conflict)
            {
                InModal(surface.Label("ConflictHint",L10n.T("settings.conflictHint",GameInput.PathLabel(pendingPath),L10n.T("settings.action."+conflictAction)),580,452,760,100,17,GlassUiPalette.Muted));
                InModal(surface.Button("SwapBinding",L10n.T("settings.swap"),580,632,360,50,SwapBinding,true));
                InModal(surface.Button("ChooseAnother",L10n.T("settings.chooseAnother"),980,632,360,50,()=>{captureFrame=Time.frameCount;dialog=Dialog.Capture;captureError=null;BuildDialog();}));
            }
            else if(dialog==Dialog.WeaponConflict)
            {
                InModal(surface.Label("WeaponConflictHint",L10n.T("settings.weaponConflictHint",GameInput.PathLabel(pendingPath)),580,452,760,130,17,GlassUiPalette.Muted));
                InModal(surface.Button("UseSharedWeaponBinding",L10n.T("settings.useSharedBinding"),580,632,360,50,UseSharedWeaponBinding));
                InModal(surface.Button("ChooseAnother",L10n.T("settings.chooseAnother"),980,632,360,50,()=>{captureFrame=Time.frameCount;dialog=Dialog.Capture;captureError=null;BuildDialog();},true));
            }
            else if(dialog==Dialog.Display)
            {
                displayCountdown=InModal(surface.Label("DisplayCountdown","",580,452,760,90,17,GlassUiPalette.Muted));
                UpdateCountdown();
                InModal(surface.Button("KeepDisplay",L10n.T("settings.keep"),580,632,360,50,FinishApply,true));
                InModal(surface.Button("RevertDisplay",L10n.T("settings.revert"),980,632,360,50,RevertDisplay));
            }
            else
            {
                InModal(surface.Label("DiscardHint",L10n.T("settings.discardHint"),580,452,760,90,17,GlassUiPalette.Muted));
                InModal(surface.Button("DiscardSettings",L10n.T("settings.discard"),580,632,360,50,Close));
                InModal(surface.Button("KeepSettings",L10n.T("settings.keepEditing"),980,632,360,50,()=>{dialog=Dialog.None;DestroyModal();},true));
            }
            modal.SetAsLastSibling();if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
        }
        void UpdateCountdown(){if(displayCountdown)displayCountdown.text=L10n.T("settings.displayCountdown",Mathf.Max(0,Mathf.CeilToInt(displayDeadline-Time.unscaledTime)));}
        void Update()
        {
            if(!open)return;
            if(rebuildRequested){rebuildRequested=false;Build();}
            if(dialog==Dialog.Display){UpdateCountdown();if(Time.unscaledTime>=displayDeadline){RevertDisplay();return;}}
            var keyboard=Keyboard.current;
            if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)
            {
                var dropdown=surface.Root.GetComponentsInChildren<TMP_Dropdown>().FirstOrDefault(value=>value.IsExpanded);
                if(dropdown){dropdown.Hide();return;}
                if(dialog==Dialog.Capture||dialog==Dialog.Conflict||dialog==Dialog.WeaponConflict)CancelCapture();
                else if(dialog==Dialog.Display)RevertDisplay();
                else if(dialog==Dialog.Discard){dialog=Dialog.None;DestroyModal();}
                else RequestClose();return;
            }
            if(dialog==Dialog.Capture&&Time.frameCount>captureFrame+1)
            {
                var mouse=Mouse.current;
                if(captureCancelRect&&mouse!=null&&mouse.leftButton.wasPressedThisFrame&&RectTransformUtility.RectangleContainsScreenPoint(captureCancelRect,mouse.position.ReadValue(),surface.DisplayCamera))return;
                if(GameInput.TryCapture(out var path))Capture(path);
            }
        }
        void OnDestroy()
        {
            if(!open)return;open=false;L10n.Changed-=LocaleChanged;
            if(beforeDisplay!=null){GameSettings.SetCurrent(beforeDisplay);GameSettings.Apply();}
            GameAudio.Apply();GameInput.Suspended=previousSuspended;
        }
    }
}
