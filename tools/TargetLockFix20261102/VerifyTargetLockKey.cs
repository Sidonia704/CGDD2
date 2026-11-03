using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Starframe.App;
using Starframe.Combat;
using Starframe.Flight;
public static class VerifyTargetLockKey
{
    static async Task Frames(int count)
    {
        int end=Time.frameCount+count;double deadline=Time.realtimeSinceStartupAsDouble+5;
        while(Time.frameCount<end){if(Time.realtimeSinceStartupAsDouble>deadline)throw new Exception("Player frames stopped advancing.");await Task.Yield();}
    }
    public static async Task<string> Main()
    {
        var world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
        if(!world||world.Player==null||world.Bot==null)throw new Exception("Range must finish loading first.");
        if(!Application.isFocused)throw new Exception("Focus the owned Game view before key verification.");
        var keyboard=InputSystem.FindControl("<Keyboard>/f")?.device as Keyboard;
        if(keyboard==null)throw new Exception("No keyboard for the actual F binding.");
        var original=keyboard.allKeys.Where(k=>k.isPressed).Select(k=>k.keyCode).ToArray();
        world.enabled=false;foreach(var actor in world.Actors)actor.flight.enabled=false;world.Projectiles.Clear();
        var hud=UnityEngine.Object.FindFirstObjectByType<RangeHUD>();var camera=hud.WorldCamera;
        var chase=camera.GetComponent<FlightChaseCamera>();if(chase)chase.enabled=false;
        var player=world.Player;var enemy=world.Bot;
        var heading=Quaternion.LookRotation(enemy.ship.CorePosition-player.ship.CorePosition,Vector3.up);
        player.flight.ResetMotion(player.body.position,heading);player.flight.SetExternalInput(Vector3.zero,Vector2.zero);
        camera.transform.rotation=heading;camera.transform.position=enemy.ship.CorePosition-camera.transform.forward*500;
        world.Sensors.Update(world.Actors,0);world.Targeting.Clear();GameInput.Suspended=false;
        try {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Frames(5);
            bool primed=GameInput.Pressed(GameAction.TargetLock);
            if(!hud.TryGetAimCursorScreenPoint(out var cursor))throw new Exception("Visible cursor unavailable.");
            var marker=(Vector2)camera.WorldToScreenPoint(enemy.ship.CorePosition);
            if(Vector2.Distance(cursor,marker)>20)throw new Exception("Fixture cursor is not aligned: "+Vector2.Distance(cursor,marker));
            int toggles=0;
            for(int i=0;i<20;i++){
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));await Frames(3);
                bool expected=i%2==0;
                if(world.Targeting.HasTarget!=expected)throw new Exception("F press "+(i+1)+" failed: expected="+expected+", focus="+Application.isFocused+", suspended="+GameInput.Suspended);
                await Frames(5);
                if(world.Targeting.HasTarget!=expected)throw new Exception("Holding F repeated toggle "+(i+1));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Frames(3);
                if(world.Targeting.HasTarget!=expected)throw new Exception("Releasing F changed the lock.");
                toggles++;
            }
            return "PASS: "+toggles+" actual F presses, held/released across multiple rendered frames; alternating lock/unlock remained usable.";
        }finally{InputSystem.QueueStateEvent(keyboard,new KeyboardState(original));}
    }
}