using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Starframe.Maps;
using Starframe.EditorTools;

public static class VoxelMapAudit
{
    public static object Run(string mapPath, int exactSamples=3)
    {
        var map=AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(mapPath);
        if(!map)throw new Exception("Missing map "+mapPath);
        var errors=new List<string>();if(!map.Validate(out var error))errors.Add(error);
        var assets=map.instances.Select(i=>i.asset).Distinct().ToArray();
        foreach(var a in assets)if(!a.HasValidInitialLod||a.initialLodMesh.name!=a.name+" Voxel Assembly")errors.Add(a.name+" lacks voxel cache");
        var samples=assets.OrderBy(a=>a.cells.Count).Where((a,i)=>i==0||i==assets.Length/2||i==assets.Length-1).Take(exactSamples).ToArray();
        foreach(var a in samples)
        {
            var cells=a.cells.ToDictionary(c=>c.position,c=>new VoxelCellState{position=c.position,shape=c.shape,surface=c.surface,quarterTurns=c.quarterTurns,modelOffset=c.modelOffset});
            var mesh=VoxelGeometry.Build(cells,a.RenderMaterialCount,a.cellSize,a.hullModules);
            try
            {
                var cached=a.initialLodMesh;
                if(mesh.subMeshCount!=cached.subMeshCount||(mesh.bounds.center-cached.bounds.center).sqrMagnitude>1e-6f||(mesh.bounds.size-cached.bounds.size).sqrMagnitude>1e-6f)errors.Add(a.name+" bounds/slots differ from actual block models");
                for(int s=0;s<mesh.subMeshCount;s++)if(Math.Abs(Area(mesh,s)-Area(cached,s))>Math.Max(.01,Area(mesh,s)*.00002))errors.Add(a.name+" surface area mismatch "+s);
                if(!Coverage(mesh,cached)||!Coverage(cached,mesh))errors.Add(a.name+" surface probe mismatch");
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        return new{map=mapPath,instances=map.instances.Count,assets=assets.Length,links=map.assemblyLinks.Count,
            uniqueCells=assets.Sum(a=>(long)a.cells.Count),weightedCells=map.instances.Sum(i=>(long)i.asset.cells.Count),
            uniqueTriangles=assets.Sum(a=>VoxelAssemblyBaker.TriangleCount(a.initialLodMesh)),weightedTriangles=map.instances.Sum(i=>VoxelAssemblyBaker.TriangleCount(i.asset.initialLodMesh)),
            shapeCounts=assets.SelectMany(a=>a.cells).GroupBy(c=>c.shape).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
            exactSamples=samples.Select(a=>a.name).ToArray(),errors};
    }
    static double Area(Mesh mesh,int slot)
    {
        var v=mesh.vertices;var t=mesh.GetIndices(slot);double area=0;
        for(int i=0;i<t.Length;i+=3)area+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5;
        return area;
    }
    static bool Coverage(Mesh sample,Mesh target)
    {
        var go=new GameObject("Temporary voxel surface audit"){hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=target;Physics.SyncTransforms();
            var v=sample.vertices;var t=sample.triangles;int step=Math.Max(1,t.Length/3/128);
            for(int i=0;i<t.Length/3;i+=step)
            {
                var a=v[t[i*3]];var b=v[t[i*3+1]];var c=v[t[i*3+2]];var normal=Vector3.Cross(b-a,c-a);
                if(normal.sqrMagnitude<1e-10)continue;normal.Normalize();var p=(a+b+c)/3;
                if(!collider.Raycast(new Ray(p+normal*.05f,-normal),out var hit,.1f)||(hit.point-p).sqrMagnitude>.000025f)return false;
            }
            return true;
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
}
