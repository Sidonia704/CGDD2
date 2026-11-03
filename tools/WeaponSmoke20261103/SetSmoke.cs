using UnityEditor;
using Starframe.Flight;
public static class SetSmoke{
 public static string SetEnabled(bool enabled){
 int count=0;
 foreach(var name in new[]{"Gun1","Gun2","Gun3","Railgun1","Railgun2","Railgun3","Missile1","Missile2","Missile3"}){
 var w=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+name+"Weapon.asset");
 w.smokeTrail=enabled;EditorUtility.SetDirty(w);AssetDatabase.SaveAssetIfDirty(w);count++;
 }return "Smoke="+enabled+" on "+count+" native weapon profiles";
 }
}
