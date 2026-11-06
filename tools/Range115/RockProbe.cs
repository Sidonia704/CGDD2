using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.EditorTools;
public static class RockProbe
{
 public static object Run(bool full=false)
 {
  var a=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>("Assets/_Starframe/Maps/Range115Voxels/Assets/Asteroid_Rock_267.asset");
  if(full)return PatchworkVoxelRocks.DiagnoseBoundary(a.LocalBounds,a.name);
  return PatchworkVoxelRocks.DiagnoseCells(a.LocalBounds,a.name,new[]{new Vector3Int(-21,1,37),new Vector3Int(-21,1,38),new Vector3Int(-21,1,39)});
 }
}
