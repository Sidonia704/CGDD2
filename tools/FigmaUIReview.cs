using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Starframe.App;
using Starframe.Construction;
using Starframe.Presentation;
using TMPro;
using L10n=Starframe.Presentation.L10n;
using Starframe.Combat;
using Starframe.Flight;

public static class FigmaUIReview
{
 const string Evidence="Documentation/FigmaUI-2026-11-06";
 static GameAppBootstrap App=>UnityEngine.Object.FindFirstObjectByType<GameAppBootstrap>();
 public static object Prepare(){Directory.CreateDirectory(Evidence);File.WriteAllText(Evidence+"/review-original-scene.txt",SceneManager.GetActiveScene().path);File.WriteAllText(Evidence+"/review-original-background.txt",Application.runInBackground.ToString());Application.runInBackground=true;return new{scene=SceneManager.GetActiveScene().path,background=Application.runInBackground};}
 public static object State()=>new{frame=Time.frameCount,scene=SceneManager.GetActiveScene().name,playing=Application.isPlaying,width=Screen.width,height=Screen.height,app=App?App.CurrentPage.ToString():null,ready=App&&App.IsReady};
 public static object GammaSky(){RenderSettings.skybox.SetColor("_Background",GameSettings.Current.backgroundColor.gamma);return State();}
 public static object Focus(){var type=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(type);window.Focus();window.Repaint();EditorApplication.QueuePlayerLoopUpdate();return State();}
 public static object PlaceTarget(){var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();w.SetBotMode(RangeBotMode.Stationary,false);var h=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();var b=w.Bot;b.body.position=h.WorldCamera.transform.position+h.WorldCamera.transform.forward*800;b.body.linearVelocity=Vector3.zero;b.body.angularVelocity=Vector3.zero;b.ship.transform.position=b.body.position;Physics.SyncTransforms();return State();}
 public static object Ungraded(){RenderSettings.skybox.SetColor("_Background",GameSettings.Current.backgroundColor);foreach(var v in UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))foreach(var c in v.profile.components)if(c is UnityEngine.Rendering.Universal.Tonemapping||c is UnityEngine.Rendering.Universal.ColorAdjustments||c is UnityEngine.Rendering.Universal.Vignette)c.active=false;return State();}
 public static object Home(){AppNavigation.OpenHome();return State();}
 public static object English(){var p=GameSettings.Current.Clone();p.localeCode="en";p.uiScale=1;GameSettings.SetCurrent(p);L10n.SelectLocale("en");if(App)App.ShowHome();return State();}
 public static object Chinese(){var p=GameSettings.Current.Clone();p.localeCode="zh-Hans";GameSettings.SetCurrent(p);L10n.SelectLocale("zh-Hans");if(App)App.ShowHome();return State();}
 public static object Range(){App.ShowRangeSetup();return State();}
 public static object Deployment(){App.ShowPage(AppPage.Deployment);return State();}
 public static object Blueprints(){App.ShowPage(AppPage.BlueprintLibrary);return State();}
 public static object Settings(){App.ShowSettings();return State();}
 public static object Editor(){App.OpenEditor();return State();}
 public static object Catalogue(){UnityEngine.Object.FindFirstObjectByType<ShipEditorUI>().ToggleCatalogue();return State();}
 public static object Groups(){UnityEngine.Object.FindFirstObjectByType<ShipEditorUI>().ToggleWeaponGroups();return State();}
 public static object Targets()=>Click("SelectTarget");
 public static object Accessibility()=>Click("SettingsTabAccessibility");
 public static object EnterRange(){var ui=UnityEngine.Object.FindFirstObjectByType<ShipEditorController>();if(ui)ui.EnterFlightRange();else App.EnterRange();return State();}
 public static object ModuleInfo(){return UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(v=>v.name=="ModuleName"||v.name=="ModuleStatistics").Select(v=>new{name=v.name,text=v.text,active=v.gameObject.activeInHierarchy,enabled=v.enabled,scale=v.transform.lossyScale.ToString(),rect=v.rectTransform.rect.ToString(),color=v.color.ToString(),culled=v.canvasRenderer.cull,vertices=v.textInfo.characterCount}).ToArray();}
 public static object Lock(){var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();w.SetBotMode(RangeBotMode.Stationary,false);var hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();var points=w.Targeting.DetectedTargets.Select(t=>hud.WorldCamera.WorldToScreenPoint(t.CorePosition)).Where(q=>q.z>0&&q.x>0&&q.x<Screen.width&&q.y>0&&q.y<Screen.height).ToArray();if(points.Length==0)return new{error="No front target"};var p=points[0];w.Targeting.ToggleLockAt(p);return new{locked=w.Targeting.HasTarget,target=p.ToString(),parts=w.Player.ship.Parts.Count};}
 public static object Weapons(){var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();var states=new System.Collections.Generic.List<WeaponHudState>();w.Player.weapons.GetHudWeaponStates(states);return states.Select(v=>new{part=v.Part.record.shape.ToString(),family=v.Definition.family.ToString(),v.Enabled,v.Blocked,v.Heat,v.Overheated,v.ReloadRemaining}).ToArray();}
 public static object LiveOptions(){UnityEngine.Object.FindFirstObjectByType<RangeAppNavigation>().OpenPractice();return State();}
 public static object FinalHud(){var nav=UnityEngine.Object.FindFirstObjectByType<RangeAppNavigation>();if(nav&&nav.IsOpen)nav.Close();var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();float row=222f/w.Player.weapons.Groups.Count;foreach(var t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))if(t.name.StartsWith("WeaponGroupHudLabel_")){UiStyle.ApplyText(t,Mathf.Min(13.5f,(row-10)*.4f));t.overflowMode=TextOverflowModes.Overflow;}return State();}
 public static object RestoreReview(){RangePracticeSettings.Mode=RangeBotMode.FlyOnly;return new{mode=RangePracticeSettings.Mode.ToString(),workspace=ShipWorkspace.WorkspacePath};}
 public static object LiveOptionsChecks(){var nav=UnityEngine.Object.FindFirstObjectByType<RangeAppNavigation>();var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();w.SetBotMode(RangeBotMode.Stationary,false);nav.OpenPractice();Click("LiveAttack");if(w.BotMode!=RangeBotMode.StationaryFire)throw new Exception("attack toggle");Click("LiveMove");if(w.BotMode!=RangeBotMode.Combat)throw new Exception("move toggle");Click("LiveAttack");if(w.BotMode!=RangeBotMode.FlyOnly)throw new Exception("independent attack toggle");Click("LiveMove");if(w.BotMode!=RangeBotMode.Stationary)throw new Exception("independent move toggle");nav.Close();return new{passed=true,combinations=4};}
 public static object BudgetCheck(){var m=AssetDatabase.LoadAssetAtPath<MissionCatalog>("Assets/_Starframe/Settings/Game/Missions.asset").missions[0];var c=Resources.Load<TMP_FontAsset>("UI/Fonts/ChineseFallback");if(Mathf.Abs(m.BudgetFor(MissionDifficulty.Hell)-m.baseBudget*.5f)>.01f||Mathf.Abs(c.faceInfo.scale-.7f)>.001f)throw new Exception("native content mismatch");return new{hell=m.BudgetFor(MissionDifficulty.Hell),standard=m.baseBudget,chineseGlyphScale=c.faceInfo.scale};}
 public static string CombatInfo(){var w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();var h=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();return "frame="+Time.frameCount+" t="+Time.time+" suspended="+GameInput.Suspended+" camera="+h.WorldCamera.name+" pos="+h.WorldCamera.transform.position+" forward="+h.WorldCamera.transform.forward+" pixelRect="+h.WorldCamera.pixelRect+" detected="+w.Targeting.DetectedTargets.Count+"\n"+string.Join("\n",w.Bots.Select(b=>b.blueprintName+" core="+b.ship.CorePosition+" body="+b.body.position+" screen="+h.WorldCamera.WorldToScreenPoint(b.ship.CorePosition)));}
 public static object Isolate(){var ship=ShipWorkspace.Ship;string from=ShipWorkspace.BlueprintDirectory,to=Path.GetFullPath(Evidence+"/IsolatedProfile");Directory.CreateDirectory(to+"/Blueprints");foreach(var f in Directory.GetFiles(from,"*.json"))File.Copy(f,Path.Combine(to,"Blueprints",Path.GetFileName(f)),true);ShipWorkspace.UseStorageRoot(to);ShipWorkspace.Set(ship,true);return new{profile=ShipWorkspace.WorkspacePath,parts=ship.parts.Count};}
 public static object MixedReviewShip(){if(!ShipWorkspace.WorkspacePath.Contains("IsolatedProfile"))throw new Exception("Requires isolated profile");var d=ShipWorkspace.Ship;int i=0;foreach(var p in d.parts)if(p.shape==BlockShape.Railgun3){p.shape=i++<4?BlockShape.Gun1:BlockShape.Laser;}d.name="HUD review — isolated copy";ShipWorkspace.Set(d,true);App.ShowHome();return new{parts=d.parts.Count,replaced=i};}
 public static object AssemblyChecks(){var c=UnityEngine.Object.FindFirstObjectByType<ShipEditorController>();var u=UnityEngine.Object.FindFirstObjectByType<ShipEditorUI>();int count=c.Assembly.Count;var pose=c.Orbit.Capture();u.ToggleCatalogue();if(!u.IsCatalogueOpen)throw new Exception("catalogue did not open");u.ToggleWeaponGroups();if(u.IsCatalogueOpen||!u.IsWeaponGroupsOpen||c.Assembly.Count!=count)throw new Exception("mode transition lost geometry");while(c.Assembly.Export("review").weaponGroups.Count<6)Click("AddWeaponGroup");if(UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Any(b=>b.name=="AddWeaponGroup"&&b.gameObject.activeInHierarchy))throw new Exception("six-group cap absent");var data=c.Assembly.Export("review");int id=data.weaponGroups[0].id;var before=data.weaponGroups[0].binding.source;Click("GroupFireBinding_"+id);if(c.Assembly.Export("review").weaponGroups[0].binding.source==before)throw new Exception("binding did not switch");Click("GroupFireBinding_"+id);u.ToggleWeaponGroups();if(c.Assembly.Count!=count)throw new Exception("geometry changed");return new{passed=true,count,groups=c.Assembly.Export("review").weaponGroups.Count,catalogueIndependent=true,mouseBindingChanged=true};}
 public static object Click(string name){var b=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).FirstOrDefault(v=>v.name==name&&v.gameObject.activeInHierarchy);if(!b||!b.IsInteractable())throw new Exception("Unavailable: "+name);b.onClick.Invoke();return State();}
 public static object Inspect(){return UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(v=>v.gameObject.activeInHierarchy).Select(v=>new{name=v.name,text=v.text,size=v.fontSize,font=v.font?v.font.name:"none",overflow=v.isTextOverflowing}).ToArray();}
 public static object Lighting(){return new {background=GameSettings.Current.backgroundColor,locale=GameSettings.Current.localeCode,sky=RenderSettings.skybox?RenderSettings.skybox.name:"none",shader=RenderSettings.skybox?RenderSettings.skybox.shader.name:"none",color=RenderSettings.skybox&&RenderSettings.skybox.HasProperty("_Background")?RenderSettings.skybox.GetColor("_Background"):Color.clear,volumes=UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None).Select(v=>new{name=v.name,components=v.profile.components.Select(c=>c.name).ToArray()}).ToArray()};}
 public static string RenderInfo(){return "ColorSpace="+QualitySettings.activeColorSpace+"\n"+string.Join("\n",UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Select(v=>v.name+" RT="+(v.targetTexture?v.targetTexture.graphicsFormat.ToString():"screen")+" post="+(v.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()?.renderPostProcessing)))+"\n"+string.Join("\n",UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None).SelectMany(v=>v.profile.components.Select(c=>JsonUtility.ToJson(c))));}
}



