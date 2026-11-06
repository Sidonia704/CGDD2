using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.EditorTools;
public static class DomeProbe
{
 public static object Run()
 {
  var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>("Assets/_Starframe/Maps/SaturnRingVoxels/Assets/domeleft_0000_0001_00.asset");
  var d=a.cells.ToDictionary(c=>c.position,c=>new VoxelCellState{position=c.position,shape=c.shape,surface=c.surface,quarterTurns=c.quarterTurns,modelOffset=c.modelOffset});
  var mesh=VoxelGeometry.Build(d,a.RenderMaterialCount,a.cellSize,a.hullModules);
  try{var result=UniformSurfaceSampler.Build(mesh,1,2000000);return new{oldTriangles=a.initialLodMesh.triangles.Length/3,correctTriangles=mesh.triangles.Length/3,cells=result.Count};}
  finally{Object.DestroyImmediate(mesh);}
 }
}
