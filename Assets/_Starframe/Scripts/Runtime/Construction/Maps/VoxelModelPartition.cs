// @role Partition a multi-cell hull mesh into closed per-cell pieces while preserving authored surface normals and UVs.
// @calls VoxelHullAppearance
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Starframe.Maps
{
    public static class VoxelModelPartition
    {
        struct Vertex
        {
            public Vector3 p,n;public Vector2 uv;public Vector4 t;
            public static Vertex Lerp(Vertex a,Vertex b,float f)=>new Vertex{p=Vector3.LerpUnclamped(a.p,b.p,f),n=Vector3.LerpUnclamped(a.n,b.n,f).normalized,uv=Vector2.LerpUnclamped(a.uv,b.uv,f),t=Vector4.LerpUnclamped(a.t,b.t,f)};
        }
        public static Mesh Create(Mesh source,Vector3Int cell)
        {
            var vertices=source.vertices;var normals=source.normals;var uv=source.uv;var tangents=source.tangents;var triangles=source.triangles;
            var polygons=new List<List<Vertex>>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                var polygon=new List<Vertex>();
                for(int j=0;j<3;j++){int index=triangles[i+j];polygon.Add(new Vertex{p=vertices[index]-(Vector3)cell,n=index<normals.Length?normals[index]:Vector3.up,uv=index<uv.Length?uv[index]:Vector2.zero,t=index<tangents.Length?tangents[index]:new Vector4(1,0,0,1)});}
                polygons.Add(polygon);
            }
            for(int axis=0;axis<3;axis++)for(int side=-1;side<=1;side+=2)
            {
                var next=new List<List<Vertex>>();var boundary=new List<Vector3>();
                foreach(var polygon in polygons)
                {
                    var clipped=new List<Vertex>();var previous=polygon[polygon.Count-1];float pd=.5f-side*previous.p[axis];
                    foreach(var current in polygon)
                    {
                        float cd=.5f-side*current.p[axis];
                        if((cd>=0)!=(pd>=0))clipped.Add(Vertex.Lerp(previous,current,pd/(pd-cd)));
                        if(cd>=0)clipped.Add(current);previous=current;pd=cd;
                    }
                    if(clipped.Count<3)continue;next.Add(clipped);
                    foreach(var v in clipped)if(Mathf.Abs(v.p[axis]-side*.5f)<.00001f)boundary.Add(v.p);
                }
                // Only interior grid cuts need caps. Existing exterior seams and bevels stay authored.
                float bound=cell[axis]+side*.5f;
                if(bound>source.bounds.min[axis]+.00001f&&bound<source.bounds.max[axis]-.00001f)
                {
                    var cap=Cap(boundary,axis,side);if(cap!=null)next.Add(cap);
                }
                polygons=next;
            }
            var outputVertices=new List<Vector3>();var outputNormals=new List<Vector3>();var outputUv=new List<Vector2>();var outputTangents=new List<Vector4>();var outputIndices=new List<int>();
            foreach(var polygon in polygons)
            {
                int start=outputVertices.Count;
                foreach(var v in polygon){outputVertices.Add(v.p);outputNormals.Add(v.n);outputUv.Add(v.uv);outputTangents.Add(v.t);}
                for(int i=1;i<polygon.Count-1;i++){outputIndices.Add(start);outputIndices.Add(start+i);outputIndices.Add(start+i+1);}
            }
            var mesh=new Mesh{name=source.name+" cell "+cell,indexFormat=outputVertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(outputVertices);mesh.SetNormals(outputNormals);mesh.SetUVs(0,outputUv);mesh.SetTangents(outputTangents);mesh.SetTriangles(outputIndices,0);mesh.RecalculateBounds();return mesh;
        }
        static Vector2 Project(Vector3 p,int axis)=>axis==0?new Vector2(p.y,p.z):axis==1?new Vector2(p.x,p.z):new Vector2(p.x,p.y);
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static List<Vertex> Cap(List<Vector3> points,int axis,int side)
        {
            var unique=new List<Vector3>();
            foreach(var p in points){bool found=false;foreach(var q in unique)if((p-q).sqrMagnitude<.000000001f){found=true;break;}if(!found)unique.Add(p);}
            if(unique.Count<3)return null;
            unique.Sort((a,b)=>{var p=Project(a,axis);var q=Project(b,axis);int x=p.x.CompareTo(q.x);return x!=0?x:p.y.CompareTo(q.y);});
            var hull=new List<Vector3>();
            foreach(var p in unique){while(hull.Count>=2&&Cross(Project(hull[hull.Count-1]-hull[hull.Count-2],axis),Project(p-hull[hull.Count-1],axis))<=.0000001f)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            int lower=hull.Count;
            for(int i=unique.Count-2;i>=0;i--){var p=unique[i];while(hull.Count>lower&&Cross(Project(hull[hull.Count-1]-hull[hull.Count-2],axis),Project(p-hull[hull.Count-1],axis))<=.0000001f)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            hull.RemoveAt(hull.Count-1);if(hull.Count<3)return null;
            var normal=Vector3.zero;normal[axis]=side;
            if(Vector3.Dot(Vector3.Cross(hull[1]-hull[0],hull[2]-hull[0]),normal)<0)hull.Reverse();
            var tangent=axis==0?Vector3.up:Vector3.right;var result=new List<Vertex>();
            foreach(var p in hull)result.Add(new Vertex{p=p,n=normal,uv=Project(p,axis)+Vector2.one*.5f,t=new Vector4(tangent.x,tangent.y,tangent.z,1)});
            return result;
        }
    }
}
