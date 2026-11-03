using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Starframe.App;
using Starframe.Combat;
using Starframe.Flight;
using Starframe.Maps;
using Starframe.Presentation;
public static class FinalCombatHudSmoke
{
 static Task running;
 public static string Start(){running=Run();return "Started final native-range smoke";}
 static async Task Frame(){int f=Time.frameCount;while(f==Time.frameCount)await Task.Yield();}
 static void Check(bool value,string note){if(!value)throw new Exception(note);}
 static async Task Run()
 {
  const string path="Documentation/CombatHud-2026-11-03/final-native-smoke.txt";
  try{
   FlightSession.Begin(ShipWorkspace.Ship,ShipWorkspace.Dirty);AppNavigation.HasAppContext=true;
   RangeLoadingOverlay.LoadScene(FlightSession.RangeScene);double until=Time.realtimeSinceStartupAsDouble+150;RangeCombatWorld w=null;
   while((w=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>())==null||w.Player==null||RangeLoadingOverlay.IsLoading){if(Time.realtimeSinceStartupAsDouble>until)throw new Exception("Readiness timeout");await Frame();}
   w.SetBotMode(RangeBotMode.Stationary,false);for(int i=0;i<5;i++)await Frame();Canvas.ForceUpdateCanvases();
   var hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();var g=UnityEngine.Object.FindFirstObjectByType<FlightInstrumentGraphic>();
   Check(g&&g.canvasRenderer&&g.canvasRenderer.GetMesh().vertexCount>0,"Visible instrument geometry missing");
   foreach(string name in new[]{"TelemetryGlass","RadarGlass","EnergyGlass","WeaponGroupHudGlass"}){
    var image=hud.RootCanvas.transform.Find(name).GetComponent<RawImage>();Check(image.color.a==0&&!image.raycastTarget,name+" still has a frame or catches input");
   }
   foreach(string name in new[]{"OwnStatusGlass","TargetStatusGlass"}){
    var image=hud.RootCanvas.transform.Find(name).GetComponent<RawImage>();Check(image.texture==hud.WorldCamera.targetTexture&&image.material.shader.name=="Starframe/UI/BackdropBlur",name+" lost live glass");
   }
   var map=UnityEngine.Object.FindFirstObjectByType<SpaceMapRuntime>();Check(map.BuildIsComplete,"Map incomplete");
   Check(map.Structures.Any(b=>b.UsesLightweightDamageVisuals),"Coarse damage path not configured");
   Check(map.ActiveMapColliderCount<=map.activeColliderBudget,"Map collider budget exceeded");
   var color=UiTheme.Current.combatInstrument;Check(color.g>.99f&&color.r>.8f&&color.b<.25f,"Native lime palette not retained");
   File.WriteAllText(path,$"PASS\nscene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}\nplayerParts={w.Player.ship.Parts.Count}\nactors={w.Actors.Count}\nmapBodies={map.Structures.Count}\ncolliders={map.ActiveMapColliderCount}\ninstrumentVertices={g.canvasRenderer.GetMesh().vertexCount}\ncriticalColor={color}\n");
  }catch(Exception e){File.WriteAllText(path,"FAIL "+e);}
 }
}
