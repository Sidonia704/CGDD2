using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Combat;
using Starframe.Flight;
public static class SessionNov03NativeProbe {
 static Task run;static string status="idle";
 public static string Start(){if(!Application.isPlaying)throw new Exception("Owned Play required");run=Run();return "started";}
 public static string Status()=>status+" task="+run?.Status;
 static async Task Until(Func<bool> f,string label){double end=Time.realtimeSinceStartupAsDouble+180;while(!f()){if(!Application.isPlaying||Time.realtimeSinceStartupAsDouble>end)throw new Exception(label);await Task.Yield();}}
 static async Task Run(){try{
 status="loading native SaturnRing";var op=SceneManager.LoadSceneAsync("Assets/_Starframe/Scenes/Gameplay/SaturnRing.unity");await Until(()=>op.isDone,"scene load");
 await Until(()=>UnityEngine.Object.FindFirstObjectByType<ArcadeMissionDirector>()?.World?.Player?.ship,"world ready");
 var d=UnityEngine.Object.FindFirstObjectByType<ArcadeMissionDirector>();var w=d.World;w.Player.ship.InvulnerableUntil=(float)w.Player.ship.CombatTime+200;
 int first=Time.frameCount;await Until(()=>d.WaveIndex>=1&&d.AliveEnemies>0&&Time.frameCount-first>180,"real enemy and advancing frames");
 var labels=UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None);var stage=labels.FirstOrDefault(x=>x.name=="MissionStage");
 if(!stage||stage.text!="1/5")throw new Exception("Stage HUD missing");
 if(labels.Any(x=>x.name=="WaveCountdown"||x.name=="MissionContacts"))throw new Exception("Old wave HUD leaked");
 if(w.Player.flight.MenuCursorMode)throw new Exception("Mission captures flight input");
 if(w.Player.ship.Tuning.arenaHalfExtent<2200)throw new Exception("Mission extent override missing");
 var atmosphere=UnityEngine.Object.FindFirstObjectByType<RangeAtmosphere>();
 var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 var light=atmosphere?(Light)typeof(RangeAtmosphere).GetField("starLight",flags).GetValue(atmosphere):null;
 if(!light||Vector3.Dot(-light.transform.forward,new Vector3(.62f,.52f,.58f).normalized)<.999f)throw new Exception("Key light not aligned with sky sun");
 var oldStar=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(x=>x.name=="Star Direction Reference");
 if(oldStar&&oldStar.TryGetComponent<MeshRenderer>(out var oldRenderer)&&oldRenderer.enabled)throw new Exception("Old finite star still visible");
 var text=$"PASS native SaturnRing; star/key-light aligned; finite star hidden; frames advanced={Time.frameCount-first}; stage={stage.text}; alive={d.AliveEnemies}; pending={d.PendingEnemies}; extent={w.Player.ship.Tuning.arenaHalfExtent}; freeFlight={!w.Player.flight.MenuCursorMode}; scene={SceneManager.GetActiveScene().name}";
 File.WriteAllText("Documentation/SessionNov03/native-check.txt",text);ScreenCapture.CaptureScreenshot(Path.GetFullPath("Documentation/SessionNov03/native-hud.png"));status=text;
 }catch(Exception e){status="FAILED "+e;File.WriteAllText("Documentation/SessionNov03/native-check.txt",status);throw;}}
}
