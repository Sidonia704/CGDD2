using System;
using System.Collections.Generic;
using System.Linq;
using Starframe.Maps;
using Starframe.Construction;
using Starframe.EditorTools;
using UnityEngine;
using UnityEditor;

public static class VoxelMergeCheck
{
    public static object Run()
    {
        var asset=ScriptableObject.CreateInstance<VoxelAssetDefinition>();
        asset.hullModules=SceneVoxelModules.GetOrCreate();
        var cells=new Dictionary<Vector3Int,VoxelCellState>();
        for(int x=0;x<3;x++)for(int y=0;y<3;y++)if(x!=1||y!=1){var p=new Vector3Int(x,y,0);cells.Add(p,new VoxelCellState{position=p});}
        var w=new Vector3Int(4,0,0);cells.Add(w,new VoxelCellState{position=w,shape=BlockShape.Wedge});
        Mesh raw=null,merged=null;GameObject go=null;
        try
        {
            raw=VoxelGeometry.Build(cells,8,1,asset.hullModules);merged=SceneVoxelFaceMerger.Merge(raw,asset);
            if(merged.GetIndexCount(1)!=raw.GetIndexCount(1))throw new Exception("Wedge triangles changed");
            go=new GameObject("Temporary voxel merge check"){hideFlags=HideFlags.HideAndDontSave};
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=merged;Physics.SyncTransforms();
            if(collider.Raycast(new Ray(new Vector3(1,1,-2),Vector3.forward),out var hole,4))throw new Exception("Centre hole was filled");
            if(!collider.Raycast(new Ray(new Vector3(0,1,-2),Vector3.forward),out var solid,4))throw new Exception("Solid face disappeared");
            var seven=asset.hullModules.Get(BlockShape.Block7).mesh.bounds;
            if(Mathf.Abs(seven.min.y+.5f)>.0001f||Mathf.Abs(seven.max.y)>.0001f||Mathf.Abs(seven.size.z-2)>.0001f)throw new Exception("Block7 half-height changed");
            return new{rawTriangles=raw.triangles.Length/3,mergedTriangles=merged.triangles.Length/3,holeOpen=true,wedgeUnchanged=true,halfHeightBlock7=true};
        }
        finally{if(go)UnityEngine.Object.DestroyImmediate(go);if(merged&&merged!=raw)UnityEngine.Object.DestroyImmediate(merged);if(raw)UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(asset);}
    }
}
