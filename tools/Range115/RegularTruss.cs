using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;
using Starframe.Construction;
using Starframe.EditorTools;

// External authoring tool: one regular open box truss, entirely assembled from existing Cube/Wedge modules.
public static class Range115RegularTruss
{
    const string Root="Assets/_Starframe/Maps/Range115Voxels";
    public static string Run()
    {
        const string name="Facility_Cylinder_023";
        var source=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Starframe/Maps/Range115/Meshes/"+name+".asset");
        var asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(Root+"/Assets/"+name+".asset");if(!source||!asset)throw new Exception("Missing truss reference/output");
        Backup(asset);
        float originalMass=asset.massPerCell*asset.cells.Count*Mathf.Pow(asset.cellSize,3);
        float cell;var cells=BuildCells(source.bounds,out cell);
        if(cells.Count>15000)throw new Exception("Regular truss exceeds15000cell target");
        asset.cells=cells;asset.cellSize=cell;asset.massPerCell=originalMass/(cells.Count*Mathf.Pow(cell,3));asset.hullModules=SceneVoxelModules.GetOrCreate();
        var mesh=VoxelAssemblyBaker.Bake(asset,Root+"/Meshes/"+name+".asset");
        string result="Regular open box truss approximation: four continuous axial chords, integer16cell bays, closed transverse frames, alternating exact45degree side braces; source bounds proportions and localZ/layout retained; no triangle voxel sampling. cells="+cells.Count+", cellSize="+cell+", triangles="+VoxelAssemblyBaker.TriangleCount(mesh)+", shapes="+string.Join(",",cells.GroupBy(c=>c.shape).Select(g=>g.Key+"="+g.Count()))+", bounds="+asset.LocalBounds+", mass="+originalMass+". Former fine source-topology cells/mesh preserved inReferenceBackups.";
        File.WriteAllText(Root+"/RegularTrussReport.txt",result);AssetDatabase.SaveAssets();return result;
    }
    static void Backup(VoxelAssetDefinition asset)
    {
        string folder=Root+"/ReferenceBackups";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder(Root,"ReferenceBackups");
        string path=folder+"/Truss023_FineBeforeRegular.asset";if(AssetDatabase.LoadMainAssetAtPath(path))return;
        var copy=UnityEngine.Object.Instantiate(asset);copy.name="Truss023 FineBeforeRegular";
        if(asset.initialLodMesh){var mesh=UnityEngine.Object.Instantiate(asset.initialLodMesh);mesh.name="Truss023 FineBeforeRegular Mesh";AssetDatabase.CreateAsset(mesh,folder+"/Truss023_FineBeforeRegular_Mesh.asset");copy.initialLodMesh=mesh;}
        AssetDatabase.CreateAsset(copy,path);
    }
    public static List<VoxelCell> BuildCells(Bounds bounds,out float cellSize)
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

