using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Starframe.App;
using Starframe.Combat;
using Starframe.Flight;
public static class VerifyLockRepair
{
 static Task running;
 public static string Start(){running=Run();return "Started native F/reentry verification";}
 static async Task Frames(int n=2){for(int i=0;i<n;i++){int f=Time.frameCount;while(f==Time.frameCount)await Task.Yield();}}
 static void Check(bool value,string note){if(!value)throw new Exception(note);}
 static async Task Run()
 {
  const string path="Documentation/LockRepair-2026-11-03/native-input.txt";
  var keyboard=Keyboard.current;var original=keyboard.allKeys.Where(k=>k.isPressed).Select(k=>k.keyCode).ToArray();
  var notes=new List<string>();
  try{
   _=keyboard.fKey.wasPressedThisFrame;
   FlightSession.Begin(ShipWorkspace.Ship,ShipWorkspace.Dirty);AppNavigation.HasAppContext=true;
   for(int entry=0;entry<2;entry++){
    RangeLoadingOverlay.LoadScene(FlightSession.RangeScene);double deadline=Time.realtimeSinceStartupAsDouble+150;RangeCombatWorld world=null;
    while((world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>())==null||world.Player==null||RangeLoadingOverlay.IsLoading){Check(Time.realtimeSinceStartupAsDouble<deadline,"Loading timeout");await Frames(1);}
    Check(!GameInput.Suspended,"Loading leaked input suspension");Check(Application.isFocused,"Unity must be focused for real Update input verification");
    world.SetBotMode(RangeBotMode.Stationary,false);world.enabled=false;
    foreach(var a in world.Actors){a.flight.enabled=false;a.body.isKinematic=true;}
    var hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();var camera=hud.WorldCamera;
    var target=world.Bots.First();var flight=world.Player.flight;
    flight.SetExternalInput(Vector3.zero,new Vector2(.9f,.3f));await Frames(8);
    Check(hud.TryGetForwardReticleScreenPoint(out var cross),"Missing forward reticle");
    Vector3 desired=camera.ScreenPointToRay(cross).GetPoint(350);
    var offset=target.ship.CorePhysicsPosition-target.body.position;target.body.position=desired-offset;
    foreach(var other in world.Bots)if(other!=target)other.body.position=world.Player.body.position-camera.transform.forward*400;
    Physics.SyncTransforms();world.Sensors.Update(world.Actors,0);await Frames(2);
    Check(world.Targeting.DetectedTargets.Contains(target.ship),"Prepared target not detected");
    Check(hud.TryGetAimCursorScreenPoint(out var cursor),"Missing steering cursor");
    var rules=(BattleTuning)typeof(TargetLockSystem).GetField("tuning",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(world.Targeting);
    Check(Vector2.Distance(cursor,(Vector2)camera.WorldToScreenPoint(target.ship.CorePosition))>rules.sensorAcquireRadiusPixels*camera.pixelHeight/1080f,"The cursor must be outside the ordinary acquisition radius for fallback verification");
    world.Targeting.Clear();world.Targeting.enabled=false;await Frames(1);world.Targeting.enabled=true;
    // A fresh press on the first allowed update must lock immediately, including an offset steering stick.
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));await Frames(2);
    Check(world.Targeting.HasTarget&&world.Targeting.LockedTarget==target.ship,"First fresh F press did not acquire the target under the forward reticle");
    await Frames(5);Check(world.Targeting.HasTarget,"Holding F retriggered");
    InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Frames(2);
    bool expected=true;
    for(int press=0;press<20;press++){
     // A complete tap occurs inside one InputSystem update; the rendered key level remains released.
     InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));InputSystem.QueueStateEvent(keyboard,new KeyboardState());
     await Frames(2);expected=!expected;
     Check(world.Targeting.HasTarget==expected,"Completed F tap "+press+" failed on entry "+entry);
    }
    Check(world.Targeting.HasTarget,"Unexpected final lock state");
    hud.OpenExit();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));await Frames(2);
    hud.CloseExit();await Frames(2);Check(world.Targeting.HasTarget,"A held modal key was incorrectly consumed");
    InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Frames(2);
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));await Frames(2);Check(!world.Targeting.HasTarget,"F did not rearm after the modal");
    InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Frames(2);
    notes.Add("entry "+entry+": fresh F, held F, 20 complete taps, offset steering cursor and modal rearm PASS");
    File.WriteAllLines(path,notes);
   }
   File.AppendAllText(path,"\nPASS");
  }catch(Exception e){File.AppendAllText(path,"\nFAIL "+e);}finally{InputSystem.QueueStateEvent(keyboard,new KeyboardState(original));await Frames(2);}
 }
}
