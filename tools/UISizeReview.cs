using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Starframe.App;
using Starframe.Construction;
using L10n=Starframe.Presentation.L10n;

public static class UISizeReview
{
    const string Folder="Documentation/UI-Polish-2026-11-06";
    static GameAppBootstrap App=>UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();
    static EditorWindow View=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    public static object Prepare(){Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/background.txt",Application.runInBackground.ToString());File.WriteAllText(Folder+"/maximized.txt",View.maximized.ToString());Application.runInBackground=true;View.maximized=true;return State();}
    public static object State()=>new{frame=Time.frameCount,playing=Application.isPlaying,width=Screen.width,height=Screen.height,scene=SceneManager.GetActiveScene().name,ready=App&&App.IsReady};
    public static object Focus(){View.Focus();View.Repaint();EditorApplication.QueuePlayerLoopUpdate();return State();}
    public static object Isolate(){var ship=ShipWorkspace.Ship;string from=ShipWorkspace.BlueprintDirectory,to=Path.GetFullPath(Folder+"/IsolatedProfile");Directory.CreateDirectory(to+"/Blueprints");foreach(var f in Directory.GetFiles(from,"*.json"))File.Copy(f,Path.Combine(to,"Blueprints",Path.GetFileName(f)),true);ShipWorkspace.UseStorageRoot(to);ShipWorkspace.Set(ship,true);return new{parts=ship.parts.Count,workspace=ShipWorkspace.WorkspacePath};}
    public static object English(){var p=GameSettings.Current.Clone();p.localeCode="en";p.uiScale=1;GameSettings.SetCurrent(p);L10n.SelectLocale("en");App.ShowHome();return State();}
    public static object Chinese(){var p=GameSettings.Current.Clone();p.localeCode="zh-Hans";GameSettings.SetCurrent(p);L10n.SelectLocale("zh-Hans");App.ShowHome();return State();}
    public static object Home(){AppNavigation.OpenHome();return State();}
    public static object Range(){App.ShowRangeSetup();return State();}
    public static object Deployment(){App.ShowPage(AppPage.Deployment);return State();}
    public static object Blueprints(){App.ShowPage(AppPage.BlueprintLibrary);return State();}
    public static object Settings(){App.ShowSettings();return State();}
    public static object Editor(){App.OpenEditor();return State();}
    public static object Groups(){UnityEngine.Object.FindFirstObjectByType<ShipEditorUI>().ToggleWeaponGroups();return State();}
    public static object Click(string name){var b=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(v=>v.name==name&&v.gameObject.activeInHierarchy);if(!b||!b.IsInteractable())throw new Exception("Unavailable "+name);b.onClick.Invoke();return State();}
    public static object Audit(){Canvas.ForceUpdateCanvases();var texts=UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None);foreach(var t in texts)t.ForceMeshUpdate();return new{state=State(),countermeasure=GameInput.BindingLabel(GameAction.Countermeasure),truncated=texts.Where(t=>t.isTextTruncated&&t.gameObject.activeInHierarchy).Select(t=>new{t.name,t.text,t.fontSize}).ToArray(),sliders=UnityEngine.Object.FindObjectsByType<Slider>(FindObjectsSortMode.None).Select(s=>new{s.name,handle=s.handleRect.rect.size.ToString()}).ToArray()};}
    public static object Finish(){if(Application.isPlaying)throw new Exception("Stop Play first");View.maximized=bool.Parse(File.ReadAllText(Folder+"/maximized.txt"));Application.runInBackground=bool.Parse(File.ReadAllText(Folder+"/background.txt"));ShipWorkspace.UseStorageRoot(Application.persistentDataPath);ShipWorkspace.Reload();GameSettings.Load();TraceDocsExporter.ExportForAutomation();return new{state=State(),compilationFailed=EditorUtility.scriptCompilationFailed,workspace=ShipWorkspace.WorkspacePath};}
}
