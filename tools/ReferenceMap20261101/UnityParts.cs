using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.Flight;

public static class ReferenceMapUnityParts
{
    [Serializable] public sealed class Part { public string shape; public int id; public Vector3[] vertices; public int[] triangles; public Vector3Int[] occupied; public Vector3 min,max; }
    [Serializable] public sealed class Pose { public int[] turns, matrix; }
    [Serializable] public sealed class Library { public Part[] parts; public Pose[] rotations; }
    public static object Export()
    {
        var db = AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var parts = VoxelHullAppearance.Shapes.Select(shape => { var d = db.Get(shape); var m = d.mesh; return new Part { shape=shape.ToString(), id=(int)shape, vertices=m.vertices, triangles=m.triangles, occupied=d.occupiedCells, min=m.bounds.min, max=m.bounds.max }; }).ToArray();
        var seen = new HashSet<string>(); var poses = new List<Pose>();
        for(int x=0;x<4;x++)for(int y=0;y<4;y++)for(int z=0;z<4;z++)
        {
            var m=Matrix4x4.Rotate(Quaternion.Euler(x*90,y*90,z*90)); var values=new int[9];
            for(int row=0;row<3;row++)for(int col=0;col<3;col++)values[row*3+col]=Mathf.RoundToInt(m[row,col]);
            if(seen.Add(string.Join(",",values)))poses.Add(new Pose{turns=new[]{x,y,z},matrix=values});
        }
        string path="tools/ReferenceMap20261101/hull-templates.json";
        var payload = new { parts = parts.Select(p => new { p.shape,p.id,vertices=p.vertices.Select(v=>new[]{v.x,v.y,v.z}).ToArray(),p.triangles,occupied=p.occupied.Select(v=>new[]{v.x,v.y,v.z}).ToArray(), min=new[]{p.min.x,p.min.y,p.min.z},max=new[]{p.max.x,p.max.y,p.max.z} }).ToArray(), rotations=poses.ToArray() };
        var jsonType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Newtonsoft.Json.JsonConvert",false)).First(t=>t!=null);
        var serialize=jsonType.GetMethod("SerializeObject",new[]{typeof(object)});
        Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,(string)serialize.Invoke(null,new object[]{payload}));
        return new { path, parts=parts.Select(p=>new {p.shape,vertices=p.vertices.Length,cells=p.occupied.Length}).ToArray(), rotations=poses.Count };
    }
}
