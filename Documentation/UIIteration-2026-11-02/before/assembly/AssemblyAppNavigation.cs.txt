// @role Preserve the edited ship while navigating between construction and menus.
// @calls ShipEditorController, ShipWorkspace, FlightSession, GameAudio

using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.App
{
    public sealed class AssemblyAppNavigation:MonoBehaviour
    {
        ShipEditorController editor;ShipEditorUI editorUi;Canvas canvas;
        RectTransform toolbar;bool changed;float saveAt;
        public void Initialize(ShipEditorController value)
        {
            editor=value;editorUi=value.GetComponent<ShipEditorUI>();canvas=value.GetComponentInChildren<Canvas>();
            if(!editorUi)editorUi=FindFirstObjectByType<ShipEditorUI>();if(!canvas)canvas=editorUi.GetComponentInChildren<Canvas>();
            editor.Assembly.Changed+=OnChanged;L10n.Changed+=Rebuild;Rebuild();
            var test=canvas.transform.Find("Toolbox/EnterFlightRange");
            var button=Array.Find(canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true),b=>b.name=="EnterFlightRange");
            if(button){button.onClick.RemoveAllListeners();button.onClick.AddListener(OpenRangeSetup);}
        }
        void Rebuild()
        {
            if(!canvas)return;if(toolbar){toolbar.gameObject.SetActive(false);Destroy(toolbar.gameObject);}
            toolbar=MenuSurface.Rect((RectTransform)canvas.transform,"AppNavigation",0,0,1920,1080);
            Button("ReturnToHangar",L10n.T("app.backHangar"),628,32,190,44,ReturnToHangar);
            Button("AssemblyWeaponGroups",L10n.T("app.weaponGroups"),834,32,228,44,()=>ReturnToPage(AppPage.WeaponGroups));
            Button("AssemblySettings",L10n.T("app.settings"),1078,32,158,44,()=>ReturnToPage(AppPage.Settings));
            if(AppNavigation.SelectedMissionId!=null)Button("ResumeDeployment",L10n.T("app.backDeployment"),1252,32,220,44,()=>ReturnToPage(AppPage.Deployment));
        }
        void Button(string name,string text,float x,float y,float w,float h,Action action)
        {
            var rect=MenuSurface.Rect(toolbar,name,x,y,w,h);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=GlassUiPalette.Button;
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.onClick.AddListener(()=>{GameAudio.PlayUiClick();try{action();}catch(Exception e)when(e is System.IO.IOException||e is System.IO.InvalidDataException||e is UnauthorizedAccessException){editorUi.SetStatusKey(L10n.ErrorKey(e),true);}});
            var labelObject=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));labelObject.layer=5;var r=(RectTransform)labelObject.transform;r.SetParent(rect,false);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(8,2);r.offsetMax=new Vector2(-8,-2);
            var label=labelObject.GetComponent<TextMeshProUGUI>();label.font=L10n.CurrentFont;label.text=text;label.fontSize=14;label.color=GlassUiPalette.Ink;label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;label.textWrappingMode=TextWrappingModes.NoWrap;
        }
        void OnChanged(){changed=true;saveAt=Time.unscaledTime+.75f;}
        void Update(){if(changed&&Time.unscaledTime>=saveAt){try{Capture();changed=false;}catch(Exception e)when(e is System.IO.IOException||e is System.IO.InvalidDataException||e is UnauthorizedAccessException){editorUi.SetStatusKey(L10n.ErrorKey(e),true);saveAt=Time.unscaledTime+10;}}}
        void Capture()
        {
            var data=editor.Assembly.Export(editorUi.BlueprintName);ShipWorkspace.Set(data,editor.Dirty,ShipWorkspace.SourceFile);
        }
        public void ReturnToHangar()=>ReturnToPage(AppNavigation.ReturnPage==AppPage.Deployment||AppNavigation.ReturnPage==AppPage.RangeSetup?AppNavigation.ReturnPage:AppPage.Home);
        public void ReturnToPage(AppPage target)
        {
            AppNavigation.SettingsReturnToEditor=target==AppPage.Settings;
            AppNavigation.WeaponGroupsReturnToEditor=target==AppPage.WeaponGroups;
            if(target==AppPage.Settings||target==AppPage.WeaponGroups)AppNavigation.EditorResumePage=AppNavigation.ReturnPage;
            Capture();AppNavigation.OpenHome(target);
        }
        public void OpenRangeSetup()
        {
            Capture();AppNavigation.RangeReturnToEditor=true;AppNavigation.OpenHome(AppPage.RangeSetup);
        }
        void OnDestroy(){L10n.Changed-=Rebuild;if(editor&&editor.Assembly)editor.Assembly.Changed-=OnChanged;}
    }
    public sealed class AppRuntimeBridge:MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install()
        {
            if(FindFirstObjectByType<AppRuntimeBridge>())return;var go=new GameObject("GameRuntime");DontDestroyOnLoad(go);go.AddComponent<AppRuntimeBridge>();
        }
        void Awake(){SceneManager.sceneLoaded+=OnSceneLoaded;StartCoroutine(InitializeScene(SceneManager.GetActiveScene()));}
        void OnSceneLoaded(Scene scene,LoadSceneMode mode)=>StartCoroutine(InitializeScene(scene));
        IEnumerator InitializeScene(Scene scene)
        {
            var prefs=GameSettings.Current;yield return L10n.EnsureReady();GameSettings.Apply();yield return null;
            if(scene!=SceneManager.GetActiveScene())yield break;
            if(scene.name==FlightSession.EditorScene){
                var controller=FindFirstObjectByType<ShipEditorController>();if(controller&&!controller.GetComponent<AssemblyAppNavigation>())controller.gameObject.AddComponent<AssemblyAppNavigation>().Initialize(controller);
                GameInput.Suspended=false;
            }
        }
        void OnDestroy(){SceneManager.sceneLoaded-=OnSceneLoaded;}
    }
}
