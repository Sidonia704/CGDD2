// Standalone mathematical/component-identity harness only; never imported by Unity.
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) => o is not null; public static void DestroyImmediate(Object o) {} }
    public class GameObject : Object
    {
        public readonly System.Collections.Generic.List<BoxCollider> Components = new();
        public T AddComponent<T>() where T : new() { var t = new T(); if (t is BoxCollider b) { b.gameObject = this; Components.Add(b); } return t; }
    }
    public class BoxCollider : Object { public GameObject gameObject; public Vector3 center, size; public bool enabled = true; }
    public struct Vector3Int : System.IEquatable<Vector3Int>
    {
        public int x,y,z;
        public Vector3Int(int x,int y,int z){this.x=x;this.y=y;this.z=z;}
        public static Vector3Int zero=>new(0,0,0); public static Vector3Int right=>new(1,0,0); public static Vector3Int left=>new(-1,0,0); public static Vector3Int up=>new(0,1,0); public static Vector3Int down=>new(0,-1,0); public static Vector3Int forward=>new(0,0,1); public static Vector3Int back=>new(0,0,-1);
        public static Vector3Int operator +(Vector3Int a,Vector3Int b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3Int operator *(Vector3Int a,int b)=>new(a.x*b,a.y*b,a.z*b);
        public static Vector3Int Min(Vector3Int a,Vector3Int b)=>new(System.Math.Min(a.x,b.x),System.Math.Min(a.y,b.y),System.Math.Min(a.z,b.z));
        public static Vector3Int Max(Vector3Int a,Vector3Int b)=>new(System.Math.Max(a.x,b.x),System.Math.Max(a.y,b.y),System.Math.Max(a.z,b.z));
        public bool Equals(Vector3Int b)=>x==b.x&&y==b.y&&z==b.z; public override bool Equals(object b)=>b is Vector3Int v&&Equals(v); public override int GetHashCode()=>System.HashCode.Combine(x,y,z); public override string ToString()=>$"{x},{y},{z}";
        public static implicit operator Vector3(Vector3Int a)=>new(a.x,a.y,a.z);
    }
    public struct Vector3 : System.IEquatable<Vector3>
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>new(0,0,0);public static Vector3 one=>new(1,1,1);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public bool Equals(Vector3 b)=>x.Equals(b.x)&&y.Equals(b.y)&&z.Equals(b.z);public override bool Equals(object b)=>b is Vector3 v&&Equals(v);public override int GetHashCode()=>System.HashCode.Combine(x,y,z);
    }
    public struct Vector2 { public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;} }
    public struct Bounds : System.IEquatable<Bounds>
    {
        public Vector3 center,extents;
        public Bounds(Vector3 c,Vector3 s){center=c;extents=s*.5f;}
        public Vector3 size {get=>extents*2;set=>extents=value*.5f;}
        public void Encapsulate(Bounds b){var min=center-extents;var max=center+extents;var bmin=b.center-b.extents;var bmax=b.center+b.extents;min=new(System.Math.Min(min.x,bmin.x),System.Math.Min(min.y,bmin.y),System.Math.Min(min.z,bmin.z));max=new(System.Math.Max(max.x,bmax.x),System.Math.Max(max.y,bmax.y),System.Math.Max(max.z,bmax.z));center=(min+max)*.5f;extents=(max-min)*.5f;}
        public bool Equals(Bounds b)=>center.Equals(b.center)&&extents.Equals(b.extents);public override bool Equals(object b)=>b is Bounds v&&Equals(v);public override int GetHashCode()=>System.HashCode.Combine(center,extents);
    }
    public static class Mathf { public static int FloorToInt(float f)=>(int)System.Math.Floor(f);public static int Max(int a,int b)=>System.Math.Max(a,b);public static int Clamp(int a,int min,int max)=>System.Math.Clamp(a,min,max); }
    public class Mesh
    {
        public string name;public Rendering.IndexFormat indexFormat;public int subMeshCount;
        public void SetVertices(System.Collections.Generic.List<Vector3> v){}public void SetNormals(System.Collections.Generic.List<Vector3> v){}public void SetUVs(int c,System.Collections.Generic.List<Vector2> v){}public void SetTriangles(System.Collections.Generic.List<int> v,int sub,bool calc){}public void RecalculateBounds(){}public void RecalculateTangents(){}
    }
}
namespace UnityEngine.Rendering { public enum IndexFormat { UInt16,UInt32 } }
namespace Starframe.Construction {public enum BlockShape {Cube,Wedge}}
namespace Starframe.Flight {public class ModuleDatabase : UnityEngine.Object {}}
namespace Starframe.Maps
{
    public class VoxelCellState {public UnityEngine.Vector3Int position;public bool removed;public bool Alive=>!removed;public int surface;public Starframe.Construction.BlockShape shape;}
    public static class VoxelHullAppearance {public static UnityEngine.Mesh Build(System.Collections.Generic.IReadOnlyDictionary<UnityEngine.Vector3Int,VoxelCellState> cells,int surfaces,float size,Starframe.Flight.ModuleDatabase db)=>new();}
}
