using UnityEngine;
public static class InertiaProbe
{
 public static object Run()
 {
  var rows=new System.Collections.Generic.List<string>();
  foreach(var flags in new[]{HideFlags.HideAndDontSave,HideFlags.None})
  {
   var go=new GameObject("Temporary inertia validation");go.hideFlags=flags;
   try{var box=go.AddComponent<BoxCollider>();box.center=new Vector3(5,2,3);box.size=new Vector3(20,10,30);var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.mass=100;body.centerOfMass=box.center;body.ResetInertiaTensor();rows.Add(flags.ToString()+" COM "+body.centerOfMass.ToString("R")+" inertia "+body.inertiaTensor.ToString("R"));}
   finally{Object.DestroyImmediate(go);}
  }
  return string.Join(" | ",rows);
 }
}

