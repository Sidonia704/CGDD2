// @role Run opt-in development checks using isolated preferences and blueprints.

#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.App
{
    /// <summary>Opt-in development-player validation. It never saves the real user's preferences or blueprints.</summary>
    public sealed class FrameworkSmokeRun:MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool passed;public string unityVersion,stage,startedUtc,finishedUtc;
            public int width,height;public List<string> checks=new List<string>(),errors=new List<string>(),screenshots=new List<string>();
        }
        Report report;string output;IDisposable storage;GameAppBootstrap app;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            string[] args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"-starframe-smoke")<0)return;
            var host=new GameObject("Development player validation");DontDestroyOnLoad(host);var run=host.AddComponent<FrameworkSmokeRun>();
            int index=Array.IndexOf(args,"-starframe-smoke-output");
            run.output=Path.GetFullPath(index>=0&&index+1<args.Length?args[index+1]:Path.Combine(Application.dataPath,"..","QA"));
            Directory.CreateDirectory(run.output);
            run.report=new Report{unityVersion=Application.unityVersion,startedUtc=DateTime.UtcNow.ToString("O"),stage="initializing"};
            Application.logMessageReceived+=run.OnLog;
            var ship=ShipWorkspace.Ship;run.storage=ShipWorkspace.UseStorageRoot(Path.Combine(run.output,"IsolatedProfile"));ShipWorkspace.Set(ship,true);
            var preferences=GameSettings.Current.Clone();preferences.localeCode="en";preferences.resolutionWidth=1280;preferences.resolutionHeight=720;
            preferences.fullscreenMode=(int)FullScreenMode.Windowed;preferences.vSyncCount=0;preferences.targetFrameRate=120;preferences.masterVolume=0;
            GameSettings.SetCurrent(preferences);Application.runInBackground=true;
            run.StartCoroutine(run.Pump(run.Run()));
        }
        void OnLog(string message,string trace,LogType type)
        {
            if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){report.errors.Add(message+"\n"+trace);Write();}
        }
        void Check(bool condition,string name)
        {
            if(!condition)throw new InvalidOperationException(name);report.checks.Add(name);Write();
        }
        void Stage(string value){report.stage=value;Write();}
        void Write(){if(report!=null&&output!=null)File.WriteAllText(Path.Combine(output,"Report.json"),JsonUtility.ToJson(report,true));}
        IEnumerator Wait(Func<bool> ready,string label,float seconds=45)
        {
            Stage(label);float deadline=Time.realtimeSinceStartup+seconds;
            while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(ready(),label);
        }
        IEnumerator Snapshot(string name)
        {
            Canvas.ForceUpdateCanvases();yield return null;yield return null;
            string path=Path.Combine(output,name+".png");
            // A hidden Windows swapchain can skip presentation; render the actual UI camera offscreen instead.
            var cameras=FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(value=>value.enabled&&value.gameObject.activeInHierarchy).OrderBy(value=>value.depth).ToArray();
            foreach(var world in cameras)if(world.targetTexture)world.Render();
            var camera=cameras.LastOrDefault(value=>!value.targetTexture);Check(camera,"UI camera available for "+name);
            var target=new RenderTexture(Screen.width,Screen.height,24);var previous=camera.targetTexture;var active=RenderTexture.active;Texture2D pixels=null;
            try
            {
                camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                pixels=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);pixels.Apply();
                var colors=pixels.GetPixels32();int low=765,high=0;for(int i=0;i<colors.Length;i+=113){int light=colors[i].r+colors[i].g+colors[i].b;low=Mathf.Min(low,light);high=Mathf.Max(high,light);}
                Check(high-low>24,"visible rendered content for "+name);File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{camera.targetTexture=previous;RenderTexture.active=active;target.Release();Destroy(target);if(pixels)Destroy(pixels);}
            report.screenshots.Add(path);Write();
        }
        static UnityEngine.UI.Button Button(Transform root,string name)=>root.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(value=>value.name==name);
        static void Click(Transform root,string name)
        {
            var button=Button(root,name);if(!button||!button.IsInteractable())throw new InvalidOperationException("Unavailable action: "+name);button.onClick.Invoke();
        }
        TextMeshProUGUI Label(string name)=>app.Surface.Root.GetComponentsInChildren<TextMeshProUGUI>().FirstOrDefault(value=>value.name==name);
        IEnumerator AppReady(AppPage page)
        {
            yield return Wait(()=>{app=FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady&&app.CurrentPage==page;},"page "+page);
            yield return null;
        }
        IEnumerator Run()
        {
            yield return AppReady(AppPage.Home);yield return L10n.EnsureReady();L10n.SelectLocale("en");yield return null;
            Check(app.Preview.Assembly.Count==ShipWorkspace.Ship.parts.Count,"home displays the workspace ship");
            Check(app.catalog&&app.catalog.missions.Count>=2,"two operations installed");
            Check(Shader.Find("Starframe/UI/BackdropBlur")&&Shader.Find("Starframe/Combat/VoxelGlow")&&Shader.Find("Starframe/Flight/RangeGrid"),"runtime shaders survived stripping");
            yield return Snapshot("Home-English");L10n.SelectLocale("zh-Hans");yield return null;yield return Snapshot("Home-Chinese");
            L10n.SelectLocale("en");yield return null;app.ShowSettings();yield return null;
            Check(Label("SettingsTitle")&&Label("SettingsTitle").text=="Settings","English preferences and asset-table font loaded");
            Canvas.ForceUpdateCanvases();
            foreach(var arrow in app.Surface.Root.GetComponentsInChildren<MenuDropdownArrow>())Check(arrow.canvasRenderer&&arrow.canvasRenderer.GetMesh()&&arrow.canvasRenderer.GetMesh().vertexCount>=3,"dropdown arrow rendered");
            yield return Snapshot("Settings-English");
            foreach(string tab in new[]{"Graphics","Audio","Controls","Accessibility","Language"}){Click(app.Surface.Root,"SettingsTab"+tab);yield return null;Check(Label("SettingsSectionTitle")!=null,"settings category "+tab);}
            app.GetComponent<SettingsScreen>().Close();yield return null;
            app.SelectMission(app.catalog.missions[0].id);yield return null;
            Check(app.CurrentDeployment!=null,"deployment computes readiness");yield return Snapshot("Deployment-English");
            if(app.CurrentDeployment.IsValid){Check(app.ConfirmLaunch(),"valid ship confirmed");Check(MissionSession.Current!=null,"independent battle snapshot created");yield return Snapshot("Deployment-Confirmed");Check(MissionResults.Finish(MissionOutcome.Victory,new MissionRunSummary{durationSeconds=125,enemiesDestroyed=3,damageDealt=400,damageTaken=80,survivingParts=ShipWorkspace.Ship.parts.Count}),"level result API accepts a completed operation");app.ShowResults();yield return Snapshot("Operation-Report");Click(app.Surface.Root,"ResultsPrepare");}
            app.OpenEditor();AssemblyAppNavigation navigation=null;ShipEditorController editor=null;
            yield return Wait(()=>{navigation=FindFirstObjectByType<AssemblyAppNavigation>();editor=FindFirstObjectByType<ShipEditorController>();return navigation&&editor&&editor.Assembly;},"assembly initialized");
            Check(editor.Assembly.Count==ShipWorkspace.Ship.parts.Count,"assembly restored the current ship");yield return Snapshot("Assembly-English");
            navigation.ReturnToPage(AppPage.WeaponGroups);yield return AppReady(AppPage.WeaponGroups);
            var groups=app.GetComponent<WeaponGroupScreen>();
            var add=Button(app.Surface.Root,"AddWeaponGroup");if(add&&add.IsInteractable())add.onClick.Invoke();
            var weapon=groups.Draft.parts.FirstOrDefault(value=>WeaponGroups.IsWeapon(value));
            if(weapon!=null){groups.SelectWeapon(weapon.id);var group=groups.Draft.weaponGroups.Last();Click(app.Surface.Root,"AssignGroup_"+group.id);Check(WeaponGroups.GetGroupId(groups.Draft,weapon.id)==group.id,"individual weapon assignment responds");}
            yield return Snapshot("Weapon-Groups-English");
            // The smoke profile owns this entire draft; no real named blueprint is touched.
            app.ShowHome();AppNavigation.WeaponGroupsReturnToEditor=false;AppNavigation.SettingsReturnToEditor=false;AppNavigation.RangeReturnToEditor=false;
            var rangeDraft=ShipWorkspace.Ship;
            while(rangeDraft.weaponGroups.Count<4)WeaponGroups.AddGroup(rangeDraft,null,WeaponInputBinding.MouseLeft());
            ShipWorkspace.Set(rangeDraft,true);
            app.ShowRangeSetup();yield return null;yield return Snapshot("Range-Setup-English");app.EnterRange();
            RangeCombatWorld world=null;RangeHUD hud=null;
            yield return Wait(()=>{world=FindFirstObjectByType<RangeCombatWorld>();hud=FindFirstObjectByType<RangeHUD>();return world&&world.Player!=null&&world.Targeting&&hud;},"range initialized");
            world.SetBotMode(RangeBotMode.Stationary,false);world.Targeting.ToggleLock();yield return new WaitForSeconds(.25f);
            Check(world.Player.ship.BlueprintSnapshot.parts.Count==ShipWorkspace.Ship.parts.Count,"range consumes a separate battle copy");
            var selection=world.Player.weapons;Check(selection.Groups.All(group=>selection.IsGroupEnabled(group.id)),"all weapon groups start enabled");
            selection.ToggleGroup(2);selection.ToggleGroup(3);yield return new WaitForSecondsRealtime(.16f);
            Check(selection.IsGroupEnabled(0)&&selection.IsGroupEnabled(1)&&!selection.IsGroupEnabled(2)&&!selection.IsGroupEnabled(3),"groups 3 and 4 toggle independently of groups 1 and 2");
            var groupHud=hud.RootCanvas.transform.Find("WeaponGroupHudLabel_3")?.GetComponent<TMP_Text>();
            Check(groupHud&&groupHud.text.Contains(L10n.T("hud.weaponGroupOff")),"combat HUD reflects the group selection");
            var groupGlass=hud.RootCanvas.transform.Find("WeaponGroupHudGlass")?.GetComponent<UnityEngine.UI.RawImage>();
            Check(groupGlass&&groupGlass.texture==hud.WorldCamera.targetTexture,"weapon selection panel uses the live glass background");yield return Snapshot("Range-HUD-English");
            hud.OpenExit();Click(hud.RootCanvas.transform,"RangeOptions");yield return null;
            var rangeNavigation=FindFirstObjectByType<RangeAppNavigation>();Check(rangeNavigation&&rangeNavigation.IsOpen&&hud.ExitDialogOpen,"live practice overlay locks player input");
            yield return Snapshot("Range-Live-Options");rangeNavigation.Close();yield return null;
            Check(hud.WorldCamera.targetTexture&&hud.WorldCamera.targetTexture.IsCreated(),"closing overlay retains HUD render target");
            Click(hud.RootCanvas.transform,"ConfirmExit");yield return AppReady(AppPage.RangeSetup);app.ShowHome();yield return null;
            report.width=Screen.width;report.height=Screen.height;report.passed=report.errors.Count==0;Stage("completed");
        }
        IEnumerator Pump(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);bool failed=false;
            while(stack.Count>0)
            {
                bool more=false;object value=null;Exception failure=null;
                try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}catch(Exception exception){failure=exception;}
                if(failure!=null){report.errors.Add(failure.ToString());failed=true;break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(value is IEnumerator nested)stack.Push(nested);else yield return value;
            }
            if(failed)report.passed=false;report.finishedUtc=DateTime.UtcNow.ToString("O");Write();
            Application.logMessageReceived-=OnLog;storage?.Dispose();storage=null;
            Application.Quit(report.passed?0:1);
        }
    }
}
#endif
