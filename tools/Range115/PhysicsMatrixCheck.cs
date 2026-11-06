using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Maps;
using Starframe.EditorTools;
public static class PhysicsMatrixCheck
{
 static double[,] Matrix(Vector3 tensor,Quaternion rotation)
 {
  var r=Matrix4x4.Rotate(rotation);var a=new double[3,3];for(int x=0;x<3;x++)for(int y=0;y<3;y++)for(int k=0;k<3;k++)a[x,y]+=r[x,k]*r[y,k]*tensor[k];return a;
 }
 public static string Run()
 {
  var previous=SceneManager.GetActiveScene();var scene=SceneManager.CreateScene("Temporary source inertia verification",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
  var reports=new List<string>();
  try
  {
   foreach(var spec in new[]{new {path="Assets/_Starframe/Maps/Range115Voxels/Assets/Facility_Cylinder_003.asset",scale=1f},new{path="Assets/_Starframe/Maps/SaturnRingVoxels/Assets/domeleft_0000_0001_00.asset",scale=1f},new{path="Assets/_Starframe/Maps/Range115Voxels/Assets/Asteroid_Rock_209.asset",scale=.436102062f}})
   {
    var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(spec.path);var expected=UniformPhysicsCapture.Capture(a,spec.scale);
    var states=a.cells.ToDictionary(c=>c.position,c=>new VoxelCellState{position=c.position});var boxes=VoxelGeometry.CollisionBoxes(states,a.cellSize*spec.scale);
    var go=new GameObject("Temporary ordinary dynamic body");SceneManager.MoveGameObjectToScene(go,scene);
    try
    {
     foreach(var b in boxes){var collider=go.AddComponent<BoxCollider>();collider.center=b.center;collider.size=b.size;}
     var body=go.AddComponent<Rigidbody>();body.useGravity=false;body.mass=expected.mass;body.centerOfMass=expected.centerOfMass;body.ResetInertiaTensor();
     scene.GetPhysicsScene().Simulate(.0001f);body.ResetInertiaTensor();
     var measured=Matrix(body.inertiaTensor,body.inertiaTensorRotation);var analytic=Matrix(expected.inertiaTensor,expected.inertiaTensorRotation);double norm=0,error=0;
     for(int x=0;x<3;x++)for(int y=0;y<3;y++){norm+=analytic[x,y]*analytic[x,y];double d=analytic[x,y]-measured[x,y];error+=d*d;}
     double relative=Math.Sqrt(error/Math.Max(1e-20,norm));float com=(body.centerOfMass-expected.centerOfMass).magnitude;
     reports.Add(a.name+": matrixRelativeError="+relative.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+", COM="+com.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+", tensor="+body.inertiaTensor.ToString("R"));
     if(relative>.001||com>.001)throw new Exception("Inertia benchmark failed: "+reports.Last());
    }finally{UnityEngine.Object.DestroyImmediate(go);}
   }
   var single=ScriptableObject.CreateInstance<VoxelAssetDefinition>();try{single.cellSize=2;single.massPerCell=10;single.cells.Add(new VoxelCell());var m=UniformPhysicsCapture.Capture(single,1);float expected=80*4/6f;if((m.inertiaTensor-Vector3.one*expected).magnitude>.001)throw new Exception("Single cube inertia failed");reports.Add("Single 2u cube: I=M*l^2/6 passed");}finally{UnityEngine.Object.DestroyImmediate(single);}
   var report=string.Join("\n",reports);File.WriteAllText("tools/Range115/physics-matrix-check.txt",report);return report;
  }
  finally{if(Application.isPlaying)SceneManager.UnloadSceneAsync(scene);else EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
 }
}

