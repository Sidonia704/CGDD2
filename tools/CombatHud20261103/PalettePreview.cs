using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Starframe.Presentation;
using Starframe.Combat;
public static class PalettePreview
{
 public static string Set(string name)
 {
  Color tint=name=="Magenta"?new Color(1,.36f,.78f,1):name=="Amber"?new Color(1,.8f,.2f,1):new Color(.84f,1,.18f,1);
  UiTheme.Current.combatInstrument=tint;
  foreach(var graphic in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None))graphic.SetVerticesDirty();
  return name;
 }
 public static string Target()
 {
  var world=UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
  var player=world.Player;var bot=world.Bots.First();
  bot.flight.ResetMotion(player.body.position+player.ship.transform.forward*350+player.ship.transform.right*50,player.body.rotation);
  Physics.SyncTransforms();world.Sensors.Update(world.Actors,.2f);
  world.Targeting.Clear();world.Targeting.ToggleLockAt(Camera.main.WorldToScreenPoint(bot.ship.CorePhysicsPosition));
  return "Target positioned; locked="+world.Targeting.HasTarget;
 }
}
