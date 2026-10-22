using System.Collections.Generic;
using UnityEngine;
namespace Starframe.Construction
{
    public static class BlockGeometry
    {
        static readonly Vector3[] CubePoints = {
            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
            new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,-.5f,.5f),
            new Vector3(-.5f,.5f,-.5f), new Vector3(.5f,.5f,-.5f),
            new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
        static readonly Vector3[] WedgePoints = {
            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
            new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,-.5f,.5f),
            new Vector3(-.5f,.5f,.5f), new Vector3(.5f,.5f,.5f) };
        static readonly Vector3[] LongCubePoints={
            new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),
            new Vector3(-.5f,1.5f,-.5f),new Vector3(.5f,1.5f,-.5f),new Vector3(.5f,1.5f,.5f),new Vector3(-.5f,1.5f,.5f)};

        static readonly Vector3[] SlabPoints={
            new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),
            new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(.5f,0,.5f),new Vector3(-.5f,0,.5f)};
        static readonly Vector3[] PyramidPoints={
            new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(0,.5f,0)};
        static readonly Vector3[] LaserPoints={new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,2.5f),new Vector3(-.5f,-.5f,2.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,2.5f),new Vector3(-.5f,.5f,2.5f)};
        public static Vector3[] Points(BlockShape shape)=>shape==BlockShape.Laser?LaserPoints:shape==BlockShape.Wedge?WedgePoints:shape==BlockShape.Thruster2?LongCubePoints:
            shape==BlockShape.Slab?SlabPoints:shape==BlockShape.Pyramid?PyramidPoints:CubePoints;
        static Vector3 Interior(BlockShape shape)=>shape==BlockShape.Wedge?new Vector3(0,-1f/6f,1f/6f):
            shape==BlockShape.Laser?Vector3.forward:shape==BlockShape.Thruster2?Vector3.up*.5f:shape==BlockShape.Slab||shape==BlockShape.Pyramid?Vector3.down*.25f:Vector3.zero;
        static int[][] Faces(BlockShape shape)=>shape==BlockShape.Wedge?WedgeFaces:shape==BlockShape.Pyramid?PyramidFaces:CubeFaces;
        public static int[] Edges(BlockShape shape)=>shape==BlockShape.Pyramid?new[]{0,1,1,2,2,3,3,0,0,4,1,4,2,4,3,4}:
            shape==BlockShape.Wedge?new[]{0,1,1,2,2,3,3,0,3,4,2,5,4,5,0,4,1,5}:
            new[]{0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7};

        public static void FindFace(BlockShape shape,Vector3 hint,out Vector3 center,out Vector3 normal)
        {
            var points=Points(shape);
            var faces=Faces(shape);
            var interior=Interior(shape);
            center=Vector3.zero;normal=Vector3.up;float best=-2;
            foreach(var face in faces)
            {
                var c=Vector3.zero;foreach(int index in face)c+=points[index];c/=face.Length;
                var n=Vector3.Cross(points[face[1]]-points[face[0]],points[face[2]]-points[face[0]]).normalized;
                if(Vector3.Dot(n,c-interior)<0)n=-n;
                float score=Vector3.Dot(n,hint.normalized);
                if(score>best){best=score;center=c;normal=n;}
            }
        }
        static readonly int[][] CubeFaces={
            new[]{0,1,2,3},new[]{4,7,6,5},new[]{0,4,5,1},
            new[]{3,2,6,7},new[]{0,3,7,4},new[]{1,5,6,2}};
        static readonly int[][] WedgeFaces={
            new[]{0,1,2,3},new[]{3,2,5,4},new[]{0,3,4},new[]{1,5,2},new[]{0,4,5,1}};

        static readonly int[][] PyramidFaces={new[]{0,1,2,3},new[]{0,4,1},new[]{1,4,2},new[]{2,4,3},new[]{3,4,0}};
        public static Mesh CreateSolid(BlockShape shape)
        {
            var p=Points(shape);
            var faces=Faces(shape);
            var vertices=new List<Vector3>();var indices=new List<int>();var uv=new List<Vector2>();
            Vector3 interior=Interior(shape);
            foreach(var face in faces)
            {
                var normal=Vector3.Cross(p[face[1]]-p[face[0]],p[face[2]]-p[face[0]]);
                var center=Vector3.zero;foreach(int i in face)center+=p[i];center/=face.Length;
                bool reverse=Vector3.Dot(normal,center-interior)<0;
                int first=vertices.Count;
                foreach(int i in face){vertices.Add(p[i]);uv.Add(new Vector2(p[i].x+.5f,p[i].z+.5f));}
                for(int i=1;i<face.Length-1;i++)
                {indices.Add(first);indices.Add(first+(reverse?i+1:i));indices.Add(first+(reverse?i:i+1));}
            }
            var mesh=new Mesh{name=shape+"Solid"};
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
            return mesh;
        }
        public static Mesh CreateCellCollision(BlockShape shape)
        {
            var cells=BlockModules.LocalCells(shape);var bounds=new Bounds((Vector3)cells[0],Vector3.one);
            foreach(var cell in cells)bounds.Encapsulate(new Bounds((Vector3)cell,Vector3.one));
            var mesh=CreateSolid(BlockShape.Cube);var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)vertices[i]=bounds.center+Vector3.Scale(vertices[i],bounds.size);
            mesh.vertices=vertices;mesh.name=shape+"FullCellCollision";mesh.RecalculateBounds();return mesh;
        }
        public static Mesh CreateOutline(BlockShape shape,float width=.023f)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            var p=Points(shape);var edges=Edges(shape);
            for(int i=0;i<edges.Length;i+=2)
            {
                var a=p[edges[i]];var b=p[edges[i+1]];var direction=(b-a).normalized;
                var side=Vector3.Cross(direction,Mathf.Abs(direction.y)<.9f?Vector3.up:Vector3.right).normalized*width*.5f;
                var up=Vector3.Cross(direction,side).normalized*width*.5f;
                var box=new[]{a-side-up,a+side-up,a+side+up,a-side+up,b-side-up,b+side-up,b+side+up,b-side+up};
                int start=vertices.Count;vertices.AddRange(box);
                int[] tris={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,1,2,6,1,6,5,0,4,7,0,7,3};
                foreach(int t in tris)indices.Add(start+t);
            }
            var mesh=new Mesh{name=shape+"Edges"};
            mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
    public sealed class BlockView : MonoBehaviour
    {
        public BlockRecord Record { get; private set; }
        public MeshRenderer Solid { get; private set; }
        public void Initialize(BlockRecord record,Mesh solid,Mesh outline,Material body,Material edges,bool collider,Mesh collisionMesh=null)
        {
            Record=record;
            transform.localPosition=record.position;
            transform.localRotation=record.rotation;
            gameObject.AddComponent<MeshFilter>().sharedMesh=solid;
            Solid=gameObject.AddComponent<MeshRenderer>();Solid.sharedMaterial=body;
            if(outline){
                var border=new GameObject("Edges");border.transform.SetParent(transform,false);
                border.AddComponent<MeshFilter>().sharedMesh=outline;
                border.AddComponent<MeshRenderer>().sharedMaterial=edges;
            }
            if(collider) {var hit=gameObject.AddComponent<MeshCollider>();hit.sharedMesh=collisionMesh?collisionMesh:solid;hit.convex=true;}
        }
    }
}
