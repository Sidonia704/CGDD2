using System;
using System.Collections.Generic;
using Starframe.Maps;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.EditorTools
{
    // Exact rectangular union of complete unit grid faces only. Other triangles are copied unchanged.
    public static class SceneVoxelFaceMerger
    {
        struct Face : IEquatable<Face>
        {
            public int slot, axis, sign, plane;
            public bool Equals(Face b) => slot == b.slot && axis == b.axis && sign == b.sign && plane == b.plane;
            public override bool Equals(object obj) => obj is Face other && Equals(other);
            public override int GetHashCode() => unchecked(((slot * 397 + axis) * 397 + sign) * 397 + plane);
        }
        sealed class Square
        {
            public readonly List<int> triangles = new List<int>(2);
            public readonly List<int> masks = new List<int>(2);
        }
        sealed class Group
        {
            public readonly Dictionary<Vector2Int, Square> squares = new Dictionary<Vector2Int, Square>();
        }
        public static Mesh Merge(Mesh source, VoxelAssetDefinition asset)
        {
            if (!source || !asset || !asset.hullModules ||
                !UnityEditor.AssetDatabase.GetAssetPath(asset.hullModules).StartsWith(SceneVoxelModules.Root + "/", StringComparison.Ordinal)) return source;
            var vertices = source.vertices; var normals = source.normals; var uv = source.uv; var tangents = source.tangents;
            var input = new int[source.subMeshCount][]; var removed = new bool[source.subMeshCount][];
            var groups = new Dictionary<Face, Group>();
            for (int slot = 0; slot < input.Length; slot++)
            {
                input[slot] = source.GetTriangles(slot); removed[slot] = new bool[input[slot].Length / 3];
                if (!FlatMaterial(asset, slot)) continue;
                var indices = input[slot];
                for (int t = 0; t < indices.Length; t += 3)
                {
                    if (!TrySquareTriangle(vertices, normals, indices[t], indices[t+1], indices[t+2], asset.cellSize, slot,
                        out Face face, out Vector2Int cell, out int mask)) continue;
                    if (!groups.TryGetValue(face, out var group)) { group = new Group(); groups.Add(face, group); }
                    if (!group.squares.TryGetValue(cell, out var square)) { square = new Square(); group.squares.Add(cell, square); }
                    square.triangles.Add(t / 3); square.masks.Add(mask);
                }
            }
            var outputVertices = new List<Vector3>(); var outputNormals = new List<Vector3>();
            var outputUV = new List<Vector2>(); var outputTangents = new List<Vector4>();
            var outputIndices = new List<int>[input.Length];
            for (int i = 0; i < input.Length; i++) outputIndices[i] = new List<int>();
            double expectedArea = 0, rectangleArea = 0; int expectedSquares = 0, emittedSquares = 0;
            foreach (var pair in groups)
            {
                var face = pair.Key; var remaining = new HashSet<Vector2Int>();
                foreach (var entry in pair.Value.squares)
                {
                    var square = entry.Value;
                    if (square.triangles.Count != 2 || (square.masks[0] | square.masks[1]) != 15) continue;
                    int shared = square.masks[0] & square.masks[1];
                    if (shared != 5 && shared != 10) continue; // The common edge must be a diagonal, not a side.
                    remaining.Add(entry.Key); expectedSquares++;
                    foreach (int triangle in square.triangles) removed[face.slot][triangle] = true;
                }
                expectedArea += remaining.Count * (double)asset.cellSize * asset.cellSize;
                var ordered = new List<Vector2Int>(remaining);
                ordered.Sort((a,b) => { int v = a.y.CompareTo(b.y); return v != 0 ? v : a.x.CompareTo(b.x); });
                foreach (var origin in ordered)
                {
                    if (!remaining.Contains(origin)) continue;
                    int width = 1, height = 1;
                    while (remaining.Contains(origin + Vector2Int.right * width)) width++;
                    bool complete = true;
                    while (complete)
                    {
                        for (int x = 0; x < width; x++) if (!remaining.Contains(origin + new Vector2Int(x,height))) { complete = false; break; }
                        if (complete) height++;
                    }
                    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    {
                        if (!remaining.Remove(origin + new Vector2Int(x,y))) throw new InvalidOperationException("Voxel face rectangle overlaps or fills a hole.");
                        emittedSquares++;
                    }
                    rectangleArea += (double)width * height * asset.cellSize * asset.cellSize;
                    AddRectangle(face, origin, width, height, asset.cellSize, outputVertices, outputNormals, outputUV, outputTangents, outputIndices[face.slot]);
                }
                if (remaining.Count != 0) throw new InvalidOperationException("Voxel face rectangle coverage is incomplete.");
            }
            var remap = new Dictionary<int,int>();
            for (int slot = 0; slot < input.Length; slot++)
            {
                var indices = input[slot];
                for (int t = 0; t < indices.Length; t += 3)
                {
                    if (removed[slot][t / 3]) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int sourceIndex = indices[t+k];
                        if (!remap.TryGetValue(sourceIndex, out int index))
                        {
                            index = outputVertices.Count; remap.Add(sourceIndex,index);
                            outputVertices.Add(vertices[sourceIndex]); outputNormals.Add(normals[sourceIndex]);
                            outputUV.Add(uv.Length > sourceIndex ? uv[sourceIndex] : Vector2.zero);
                            outputTangents.Add(tangents.Length > sourceIndex ? tangents[sourceIndex] : new Vector4(1,0,0,1));
                        }
                        outputIndices[slot].Add(index);
                    }
                }
            }
            if (expectedSquares != emittedSquares || Math.Abs(expectedArea - rectangleArea) > Math.Max(1e-5, expectedArea * 1e-7))
                throw new InvalidOperationException("Voxel face area or exact grid coverage changed during merging.");
            var result = new Mesh { name = source.name, indexFormat = outputVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                result.SetVertices(outputVertices); result.SetNormals(outputNormals); result.SetUVs(0,outputUV); result.SetTangents(outputTangents);
                result.subMeshCount = outputIndices.Length;
                for (int slot = 0; slot < outputIndices.Length; slot++) result.SetTriangles(outputIndices[slot],slot,false);
                result.RecalculateBounds();
                double before = Area(vertices,input), after = Area(outputVertices.ToArray(),outputIndices);
                float epsilon = Mathf.Max(1e-5f, asset.cellSize * 1e-4f);
                if (Math.Abs(before-after) > Math.Max(1e-5,before*1e-6) ||
                    (source.bounds.min-result.bounds.min).sqrMagnitude > epsilon*epsilon ||
                    (source.bounds.max-result.bounds.max).sqrMagnitude > epsilon*epsilon)
                    throw new InvalidOperationException("Voxel face merge changed surface area or outer bounds.");
                return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }
        static bool FlatMaterial(VoxelAssetDefinition asset, int slot)
        {
            int surface = slot / VoxelHullAppearance.ShapeCount;
            var material = asset.surfaces != null && surface < asset.surfaces.Length ? asset.surfaces[surface] : null;
            if (!material) material = asset.hullModules.Get(VoxelHullAppearance.Shapes[slot % VoxelHullAppearance.ShapeCount])?.material;
            if (!material) return false;
            foreach (string property in material.GetTexturePropertyNames()) if (material.GetTexture(property)) return false;
            return true;
        }
        static bool TrySquareTriangle(Vector3[] vertices, Vector3[] normals, int ia, int ib, int ic, float size, int slot,
            out Face face, out Vector2Int cell, out int mask)
        {
            face = default; cell = default; mask = 0;
            Vector3 a = vertices[ia]/size, b = vertices[ib]/size, c = vertices[ic]/size;
            var normal = Vector3.Cross(b-a,c-a); float magnitude = normal.magnitude;
            if (Mathf.Abs(magnitude-1f) > .0001f) return false; // One triangle of a complete unit square has area 1/2.
            normal /= magnitude; int axis = 0;
            if (Mathf.Abs(normal.y)>Mathf.Abs(normal[axis])) axis=1;
            if (Mathf.Abs(normal.z)>Mathf.Abs(normal[axis])) axis=2;
            int sign = normal[axis]>0 ? 1 : -1; Vector3 expected = Vector3.zero; expected[axis] = sign;
            if ((normal-expected).sqrMagnitude>.00000001f || normals.Length<=Math.Max(ia,Math.Max(ib,ic)) ||
                Vector3.Dot(normals[ia],expected)<.99999f || Vector3.Dot(normals[ib],expected)<.99999f || Vector3.Dot(normals[ic],expected)<.99999f) return false;
            int u=(axis+1)%3, v=(axis+2)%3;
            float minU=Mathf.Min(a[u],Mathf.Min(b[u],c[u])), minV=Mathf.Min(a[v],Mathf.Min(b[v],c[v]));
            int gridU=Mathf.RoundToInt(minU+.5f), gridV=Mathf.RoundToInt(minV+.5f), plane=Mathf.RoundToInt(a[axis]*2);
            if (Mathf.Abs(minU-(gridU-.5f))>.0001f || Mathf.Abs(minV-(gridV-.5f))>.0001f ||
                Mathf.Abs(a[axis]-plane*.5f)>.0001f || Mathf.Abs(b[axis]-a[axis])>.0001f || Mathf.Abs(c[axis]-a[axis])>.0001f) return false;
            for (int cornerIndex = 0; cornerIndex < 3; cornerIndex++)
            {
                var point = cornerIndex == 0 ? a : cornerIndex == 1 ? b : c;
                float du=point[u]-minU,dv=point[v]-minV; int x=Mathf.RoundToInt(du),y=Mathf.RoundToInt(dv);
                if (x<0 || x>1 || y<0 || y>1 || Mathf.Abs(du-x)>.0001f || Mathf.Abs(dv-y)>.0001f) return false;
                int corner=y==0?x:3-x; int bit=1<<corner;
                if ((mask&bit)!=0) return false; mask|=bit;
            }
            face=new Face{slot=slot,axis=axis,sign=sign,plane=plane}; cell=new Vector2Int(gridU,gridV); return true;
        }
        static void AddRectangle(Face face, Vector2Int cell, int width, int height, float size, List<Vector3> vertices,
            List<Vector3> normals,List<Vector2> uv,List<Vector4> tangents,List<int> indices)
        {
            int start=vertices.Count,u=(face.axis+1)%3,v=(face.axis+2)%3;
            var n=Vector3.zero;n[face.axis]=face.sign;var tangent=Vector3.zero;tangent[u]=1;
            for(int corner=0;corner<4;corner++)
            {
                float x=cell.x-.5f+((corner==1||corner==2)?width:0), y=cell.y-.5f+(corner>=2?height:0);
                var point=Vector3.zero;point[face.axis]=face.plane*.5f;point[u]=x;point[v]=y;
                vertices.Add(point*size);normals.Add(n);uv.Add(new Vector2(x,y));tangents.Add(new Vector4(tangent.x,tangent.y,tangent.z,face.sign));
            }
            if(face.sign>0){indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);}
            else{indices.Add(start);indices.Add(start+2);indices.Add(start+1);indices.Add(start);indices.Add(start+3);indices.Add(start+2);}
        }
        static double Area(Vector3[] vertices,int[][] indices)
        {
            double area=0;foreach(var slot in indices)for(int i=0;i<slot.Length;i+=3)area+=Vector3.Cross(vertices[slot[i+1]]-vertices[slot[i]],vertices[slot[i+2]]-vertices[slot[i]]).magnitude*.5;return area;
        }
        static double Area(Vector3[] vertices,List<int>[] indices)
        {
            double area=0;foreach(var slot in indices)for(int i=0;i<slot.Count;i+=3)area+=Vector3.Cross(vertices[slot[i+1]]-vertices[slot[i]],vertices[slot[i+2]]-vertices[slot[i]]).magnitude*.5;return area;
        }
    }
}
