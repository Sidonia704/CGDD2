using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Starframe.App;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
public static class NativeRangeReentry
{
    static Task running;static string report;
    static void Log(string value)=>File.AppendAllText(report,value+Environment.NewLine);
    public static string Start(string name="before",bool inheritedMenuSuspension=false)
    {
        if(!Application.isPlaying)throw new Exception("Owned Play session required.");
        if(running!=null&&!running.IsCompleted)throw new Exception("A reproduction is already running.");
        report="Documentation/CombatDamageReentry-2026-11-02/reentry-"+name+".txt";File.WriteAllText(report,"");running=Run(inheritedMenuSuspension);return "Reentry reproduction started; poll "+report;
    }
    static async Task Until(Func<bool> predicate,string label,int seconds=120)
    {
        double end=Time.realtimeSinceStartupAsDouble+seconds;
        while(!predicate()){if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Timed out: "+label);await Task.Yield();}
    }
    static async Task Frames(int count){int end=Time.frameCount+count;await Until(()=>Time.frameCount>=end,"rendered frames",10);}
    static void Click(Transform root,string name)
    {
        var b=root.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(x=>x.name==name&&x.gameObject.activeInHierarchy);
        if(!b)throw new Exception("Missing button "+name);b.onClick.Invoke();
    }
    static async Task<GameAppBootstrap> App()
    {
        GameAppBootstrap app=null;await Until(()=>{app=UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();return app&&app.IsReady;},"hangar ready");return app;
    }
    static async Task Run(bool inheritedMenuSuspension)
    {
        string oldRoster=PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);bool hadRoster=PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);
        int oldMode=PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);bool hadMode=PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey);
        bool oldSuspended=GameInput.Suspended,oldContext=AppNavigation.HasAppContext,oldRangeReturn=AppNavigation.RangeReturnToEditor;
        var oldReturn=AppNavigation.ReturnPage;var oldDraft=FlightSession.EditorDraft;bool oldDirty=FlightSession.EditorDirty,oldReturning=FlightSession.ReturningToEditor;
        var keyboard=InputSystem.FindControl("<Keyboard>/f")?.device as Keyboard;
        var keys=keyboard?.allKeys.Where(k=>k.isPressed).Select(k=>k.keyCode).ToArray()??Array.Empty<Key>();
        var scope=ShipWorkspace.UseStorageRoot(Path.GetFullPath("Temp/NativeRangeReentry/"+Guid.NewGuid().ToString("N")));
        bool passed=false;
        try {
            if(keyboard==null)throw new Exception("Keyboard unavailable");
            var blueprint=FlightRangeBootstrap.DefaultShip();blueprint.name="reentry-target";ShipWorkspace.Repository.Save(blueprint);ShipWorkspace.Set(blueprint,true);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,"{\"entries\":[{\"file\":\"reentry-target.json\",\"count\":1}]}");
            PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey,(int)RangeBotMode.Stationary);
            AppNavigation.HasAppContext=true;AppNavigation.RangeReturnToEditor=false;AppNavigation.ReturnPage=AppPage.RangeSetup;
            await SceneManager.LoadSceneAsync(AppNavigation.HomeScene);
            for(int round=0;round<3;round++){
                var app=await App();app.ShowRangeSetup();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));
                if(round==0&&inheritedMenuSuspension){
                    Log("Inherited menu suspension before entry: "+GameInput.Suspended);
                    FlightSession.Begin(ShipWorkspace.Ship,ShipWorkspace.Dirty);RangeLoadingOverlay.LoadScene(FlightSession.RangeScene);
                }else app.EnterRange();
                if(app&&app.Surface.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Any(b=>b.name=="ConfirmationAccept"))Click(app.Surface.Root,"ConfirmationAccept");
                RangeCombatWorld world=null;RangeHUD hud=null;
                await Until(()=>{world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();return world&&world.Player!=null&&world.Targeting&&hud&&!RangeLoadingOverlay.IsLoading;},"range ready");
                await Frames(4);
                if(world.Targeting.HasTarget)throw new Exception("Held menu key unexpectedly acquired a target on entry");
                if(GameInput.Suspended||GameInput.TextInputFocused||hud.ExitDialogOpen)throw new Exception("Input remains blocked on entry "+round+": suspended="+GameInput.Suspended+", text="+GameInput.TextInputFocused+", modal="+hud.ExitDialogOpen);
                world.enabled=false;foreach(var actor in world.Actors)actor.flight.enabled=false;world.Projectiles.Clear();
                var camera=hud.WorldCamera;var chase=camera.GetComponent<FlightChaseCamera>();if(chase)chase.enabled=false;
                var heading=Quaternion.LookRotation(world.Bot.ship.CorePosition-world.Player.ship.CorePosition,Vector3.up);
                world.Player.flight.ResetMotion(world.Player.body.position,heading);world.Player.flight.SetExternalInput(Vector3.zero,Vector2.zero);
                camera.transform.rotation=heading;camera.transform.position=world.Bot.ship.CorePosition-camera.transform.forward*500;
                world.Sensors.Update(world.Actors,0);world.Targeting.Clear();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Frames(5);
                if(!Application.isFocused)throw new Exception("Owned game view lost focus");
                var marker=(Vector2)camera.WorldToScreenPoint(world.Bot.ship.CorePosition);hud.TryGetAimCursorScreenPoint(out var cursor);
                Log("Entry "+round+": targets="+world.Targeting.DetectedTargets.Count+", cursor error="+Vector2.Distance(marker,cursor)+", suspended="+GameInput.Suspended+", new target instance="+world.Targeting.GetInstanceID());
                for(int i=0;i<6;i++){
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));await Frames(4);
                    bool expected=i%2==0;
                    if(world.Targeting.HasTarget!=expected)throw new Exception("F failed at entry "+round+", press "+i+", held="+GameInput.Held(GameAction.TargetLock)+", focus="+Application.isFocused+", suspended="+GameInput.Suspended+", text="+GameInput.TextInputFocused+", latch="+typeof(TargetLockSystem).GetField("lockKeyHeld",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(world.Targeting));
                    await Frames(4);if(world.Targeting.HasTarget!=expected)throw new Exception("Repeated held-key toggle");
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Frames(4);
                }
                Log("Entry "+round+": 6 F presses passed.");
                // Second exit returns to assembly, then uses the actual Test Flight button to reopen setup.
                AppNavigation.RangeReturnToEditor=round==1;hud.OpenExit();Click(hud.RootCanvas.transform,"ConfirmExit");
                if(round==1){
                    ShipEditorController editor=null;await Until(()=>{editor=UnityEngine.Object.FindFirstObjectByType<ShipEditorController>();return editor&&editor.GetComponent<AssemblyAppNavigation>();},"assembly ready");
                    Click(editor.GetComponent<ShipEditorUI>().transform,"EnterFlightRange");
                }
                await App();
            }
            passed=true;Log("PASS: 3 entries, 18 actual F presses, menu and assembly return paths; no input flags forced.");
        } catch(Exception error){Log("FAIL: "+error);}
        finally {
            if(keyboard!=null)InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
            var scene=SceneManager.GetActiveScene();var cleanup=SceneManager.CreateScene("NativeReentryCleanup");SceneManager.SetActiveScene(cleanup);
            if(scene.IsValid()&&scene.isLoaded)await SceneManager.UnloadSceneAsync(scene);
            if(hadRoster)PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,oldRoster);else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            if(hadMode)PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey,oldMode);else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);PlayerPrefs.Save();
            scope.Dispose();GameInput.Suspended=oldSuspended;AppNavigation.HasAppContext=oldContext;AppNavigation.RangeReturnToEditor=oldRangeReturn;AppNavigation.ReturnPage=oldReturn;
            typeof(FlightSession).GetProperty("EditorDraft").SetValue(null,oldDraft);typeof(FlightSession).GetProperty("EditorDirty").SetValue(null,oldDirty);typeof(FlightSession).GetProperty("ReturningToEditor").SetValue(null,oldReturning);
            Log("DONE "+(passed?"PASS":"FAIL")+"; workspace and preferences restored.");
        }
    }
}