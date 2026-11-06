using System;
using System.Collections.Generic;
using System.Linq;
using Starframe.Maps;
using Starframe.Construction;
using UnityEngine;

namespace Starframe.EditorTools
{
    // Reviewed Range115 box-truss rule: actual existing Cube/Wedge cells, never a source visual shell.
    public static class RegularVoxelTruss
    {
    public static List<VoxelCell> Build(Bounds bounds,out float cellSize)
    {
        //19cells across the outer width gives two-cell chords and a16cell clear-node span.
        // The same span is used axially: braces therefore have genuine45degree planes at every resolution.
        const int low=-9,high=9,span=16;
        cellSize=Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.y)/19f,.5f,10f);
        int length=Mathf.Max(span+1,Mathf.RoundToInt(bounds.size.z/cellSize));int first=-length/2,last=first+length-1;
        var centre=Vector3Int.RoundToInt(bounds.center/cellSize);
        var cells=new Dictionary<Vector3Int,VoxelCell>();
        Action<int,int,int,BlockShape,Vector3Int> add=(x,y,z,shape,turns)=>{
            var key=new Vector3Int(x,y,z)+centre;
            if(cells.TryGetValue(key,out var existing)){
                if(existing.shape==BlockShape.Cube)return;
                if(shape==BlockShape.Cube||existing.quarterTurns!=turns){existing.shape=BlockShape.Cube;existing.quarterTurns=Vector3Int.zero;}return;
            }
            cells.Add(key,new VoxelCell{position=key,shape=shape,quarterTurns=turns});
        };
        int[] chord={low,low+1,high-1,high};
        // Four2x2 axial chords. Cube unions at every node explicitly close all brace/frame joints.
        for(int z=first;z<=last;z++)foreach(int x in chord)foreach(int y in chord)add(x,y,z,BlockShape.Cube,Vector3Int.zero);
        var stations=new List<int>();for(int z=first;z<=last;z+=span)stations.Add(z);if(stations[stations.Count-1]!=last)stations.Add(last);
        foreach(int z in stations){
            for(int x=low;x<=high;x++)foreach(int y in chord)add(x,y,z,BlockShape.Cube,Vector3Int.zero);
            for(int y=low;y<=high;y++)foreach(int x in chord)add(x,y,z,BlockShape.Cube,Vector3Int.zero);
        }
        // Braces have the exact lattice strip |z-sign*x-offset|<=1. Boundary cells use the one matching Wedge,
        // not a sequence of diagonal cubes. Adjacent Wedges share one global plane across the whole bay.
        for(int bay=0;bay+1<stations.Count;bay++){
            int start=stations[bay],end=stations[bay+1];if(end-start!=span)continue; // Final remainder stays straight chord + transverse frame.
            for(int face=0;face<4;face++){
                int sign=((bay+face)&1)==0?1:-1;
                int constant=start+8;
                for(int transverse=-8;transverse<=8;transverse++)for(int z=start;z<=end;z++){
                    int d=z-sign*transverse-constant;if(Math.Abs(d)>1)continue;
                    var shape=d==0?BlockShape.Cube:BlockShape.Wedge;
                    var normal=face<2?new Vector3(-sign,0,1):new Vector3(0,-sign,1);
                    var turns=d==0?Vector3Int.zero:WedgeTurns(normal*Mathf.Sign(d));
                    foreach(int depth in face%2==0?new[]{low,low+1}:new[]{high-1,high}){
                        if(face<2)add(transverse,depth,z,shape,turns);else add(depth,transverse,z,shape,turns);
                    }
                }
            }
        }
        return cells.Values.OrderBy(c=>c.position.z).ThenBy(c=>c.position.y).ThenBy(c=>c.position.x).ToList();
    }
    static Vector3Int WedgeTurns(Vector3 normal)
    {
        normal.Normalize();for(int x=0;x<4;x++)for(int y=0;y<4;y++)for(int z=0;z<4;z++)if(Vector3.Dot(Quaternion.Euler(x*90,y*90,z*90)*new Vector3(0,1,-1).normalized,normal)>.999f)return new Vector3Int(x,y,z);
        throw new Exception("No native wedge orientation");
    }
    }
}
