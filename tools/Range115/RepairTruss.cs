using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;
using Starframe.EditorTools;

// External targeted tool. Call Run only after the owner authorizes asset writes in a stopped Editor.
public static class Range115TrussRepair
{
    const string Root="Assets/_Starframe/Maps/Range115Voxels";
    public static string Run()
    {
        const string name="Facility_Cylinder_023";
        var source=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Starframe/Maps/Range115/Meshes/"+name+".asset");
        var target=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(Root+"/Assets/"+name+".asset");
        if(!source||!target)throw new Exception("Missing source or target truss");
        int before=target.cells.Count;float beforeMass=target.massPerCell*before*Mathf.Pow(target.cellSize,3);
        float cell=1.0f;List<VoxelCell> cells=null;
        for(int attempt=0;attempt<12;attempt++)try{
            // Full authored triangles retain diagonal members and all apertures. No radial revolutions or interior flood.
            cells=Range115FittedSampling.Voxelize(source.vertices,source.GetTriangles(0),cell,false,80000);break;
        }catch(InvalidOperationException){if(cell>=6f)throw;cell=Mathf.Min(6f,cell*1.15f);}
        if(cells==null||cells.Count==0)throw new Exception("Truss topology sampling failed");
        target.cells=cells;target.cellSize=cell;target.massPerCell=beforeMass/(cells.Count*Mathf.Pow(cell,3));target.hullModules=SceneVoxelModules.GetOrCreate();
        var mesh=VoxelAssemblyBaker.Bake(target,Root+"/Meshes/"+name+".asset");
        string result=name+" repaired from complete canonical source triangle topology; phase0 retained (same as former cylinder output); all layout transforms unchanged; cells "+before+" -> "+cells.Count+"; cellSize="+cell+"; triangles="+VoxelAssemblyBaker.TriangleCount(mesh)+"; mass="+beforeMass+"; components6="+Components(cells,false)+"; components26="+Components(cells,true)+"; shapes="+string.Join(",",cells.GroupBy(c=>c.shape).Select(g=>g.Key+"="+g.Count()));
        File.WriteAllText(Root+"/TrussRepairReport.txt",result);File.AppendAllText(Root+"/BuildReport.txt","\nCORRECTION: "+result+"\n");AssetDatabase.SaveAssets();return result;
    }
    static int Components(List<VoxelCell> cells,bool diagonal)
    {
        var pending=new HashSet<Vector3Int>(cells.Select(c=>c.position));var queue=new Queue<Vector3Int>();int components=0;
        while(pending.Count>0){components++;var first=pending.First();pending.Remove(first);queue.Enqueue(first);
            while(queue.Count>0){var p=queue.Dequeue();for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++){
                int distance=Math.Abs(x)+Math.Abs(y)+Math.Abs(z);if(distance==0||(!diagonal&&distance!=1))continue;var q=p+new Vector3Int(x,y,z);if(pending.Remove(q))queue.Enqueue(q);
            }}
        }return components;
    }
}

