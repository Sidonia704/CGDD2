using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.EditorTools
{
    /// <summary>
    /// Render-only exact union of full unit wedge rectangles. Call only for untextured materials.
    /// No asset, source mesh, collider, voxel data or cache signature is modified.
    /// </summary>
    public static class DiagonalVoxelFaceMerger
    {
        const float Epsilon = .0001f;
        static readonly float RootTwo = Mathf.Sqrt(2f);

        public struct MergeStatistics
        {
            public int inputTriangles, outputTriangles, candidateTriangles, completeFaces, mergedRectangles;
            public int removedTriangles => inputTriangles - outputTriangles;
            public override string ToString() => inputTriangles + " -> " + outputTriangles +
                " triangles; " + completeFaces + " full diagonal faces -> " + mergedRectangles + " rectangles";
        }

        struct Face : IEquatable<Face>
        {
            public int slot, a, b, sa, sb, plane;
            // Attribute seams remain seams, even for untextured surfaces.
            public Vector3 normal;
            public Vector4 tangent;
            public bool Equals(Face other) => slot == other.slot && a == other.a && b == other.b &&
                sa == other.sa && sb == other.sb && plane == other.plane &&
                normal.Equals(other.normal) && tangent.Equals(other.tangent);
            public override bool Equals(object obj) => obj is Face other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = slot;
                    hash = hash * 397 ^ a; hash = hash * 397 ^ b; hash = hash * 397 ^ sa;
                    hash = hash * 397 ^ sb; hash = hash * 397 ^ plane;
                    hash = hash * 397 ^ normal.GetHashCode(); return hash * 397 ^ tangent.GetHashCode();
                }
            }
        }
        sealed class Square
        {
            public int count, triangle0, triangle1, mask0, mask1;
            public void Add(int triangle, int mask)
            {
                if (count == 0) { triangle0 = triangle; mask0 = mask; }
                else if (count == 1) { triangle1 = triangle; mask1 = mask; }
                count++;
            }
            public bool Complete => count == 2 && (mask0 | mask1) == 15 &&
                ((mask0 & mask1) == 5 || (mask0 & mask1) == 10);
        }
        struct Rectangle
        {
            public Face face;
            public Vector2Int cell;
            public int width, height;
        }

        public static Mesh Merge(Mesh source) => Merge(source, out _);

        /// <summary>Returns source when no triangle reduction is possible, otherwise a caller-owned Mesh.</summary>
        public static Mesh Merge(Mesh source, out MergeStatistics statistics)
        {
            statistics = default;
            if (!source || !source.isReadable) return source;
            // Native voxel cache meshes contain only these channels. Fail closed for all other mesh types.
            foreach (var attribute in source.GetVertexAttributes())
                if (attribute.attribute != VertexAttribute.Position && attribute.attribute != VertexAttribute.Normal &&
                    attribute.attribute != VertexAttribute.Tangent && attribute.attribute != VertexAttribute.TexCoord0) return source;
            if (source.blendShapeCount != 0 ||
                (source.HasVertexAttribute(VertexAttribute.TexCoord0) &&
                 source.GetVertexAttributeDimension(VertexAttribute.TexCoord0) != 2)) return source;
            var vertices = source.vertices; var normals = source.normals;
            var uv = source.uv; var tangents = source.tangents;
            if (normals.Length != vertices.Length ||
                (tangents.Length != 0 && tangents.Length != vertices.Length)) return source;
            var input = new int[source.subMeshCount][];
            var removed = new bool[source.subMeshCount][];
            var groups = new Dictionary<Face, Dictionary<Vector2Int, Square>>();
            for (int slot = 0; slot < input.Length; slot++)
            {
                if (source.GetTopology(slot) != MeshTopology.Triangles) return source;
                input[slot] = source.GetTriangles(slot);
                removed[slot] = new bool[input[slot].Length / 3];
                statistics.inputTriangles += input[slot].Length / 3;
                for (int t = 0; t < input[slot].Length; t += 3)
                {
                    var indices = input[slot];
                    if (!TryTriangle(vertices, normals, tangents, indices[t], indices[t + 1], indices[t + 2],
                        slot, out var face, out var cell, out int mask)) continue;
                    statistics.candidateTriangles++;
                    if (!groups.TryGetValue(face, out var squares))
                    {
                        squares = new Dictionary<Vector2Int, Square>(); groups.Add(face, squares);
                    }
                    if (!squares.TryGetValue(cell, out var square))
                    {
                        square = new Square(); squares.Add(cell, square);
                    }
                    square.Add(t / 3, mask);
                }
            }
            statistics.outputTriangles = statistics.inputTriangles;
            var rectangles = new List<Rectangle>();
            int emittedCells = 0;
            foreach (var group in groups)
            {
                var remaining = new HashSet<Vector2Int>();
                foreach (var entry in group.Value)
                {
                    if (!entry.Value.Complete) continue;
                    remaining.Add(entry.Key);
                    removed[group.Key.slot][entry.Value.triangle0] = true;
                    removed[group.Key.slot][entry.Value.triangle1] = true;
                    statistics.completeFaces++;
                }
                var ordered = new List<Vector2Int>(remaining);
                ordered.Sort((x, y) => x.y != y.y ? x.y.CompareTo(y.y) : x.x.CompareTo(y.x));
                foreach (var origin in ordered)
                {
                    if (!remaining.Contains(origin)) continue;
                    int width = 1, height = 1;
                    while (remaining.Contains(origin + Vector2Int.right * width)) width++;
                    bool complete = true;
                    while (complete)
                    {
                        for (int x = 0; x < width; x++)
                            if (!remaining.Contains(origin + new Vector2Int(x, height))) { complete = false; break; }
                        if (complete) height++;
                    }
                    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    {
                        if (!remaining.Remove(origin + new Vector2Int(x, y)))
                            throw new InvalidOperationException("Diagonal rectangle overlaps or fills a missing face.");
                        emittedCells++;
                    }
                    rectangles.Add(new Rectangle { face = group.Key, cell = origin, width = width, height = height });
                }
                if (remaining.Count != 0) throw new InvalidOperationException("Diagonal face coverage incomplete.");
            }
            statistics.mergedRectangles = rectangles.Count;
            statistics.outputTriangles += rectangles.Count * 2 - statistics.completeFaces * 2;
            if (emittedCells != statistics.completeFaces) throw new InvalidOperationException("Diagonal face area changed.");
            if (statistics.outputTriangles >= statistics.inputTriangles) return source;
            var outputVertices = new List<Vector3>();
            var outputNormals = new List<Vector3>();
            var outputUV = new List<Vector2>();
            var outputTangents = new List<Vector4>();
            var outputIndices = new List<int>[input.Length];
            for (int slot = 0; slot < input.Length; slot++) outputIndices[slot] = new List<int>();
            foreach (var rectangle in rectangles)
                AddRectangle(rectangle, outputVertices, outputNormals, outputUV, outputTangents, outputIndices[rectangle.face.slot]);
            var remap = new Dictionary<int, int>();
            for (int slot = 0; slot < input.Length; slot++)
                for (int t = 0; t < input[slot].Length; t += 3)
                {
                    if (removed[slot][t / 3]) continue;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int sourceIndex = input[slot][t + corner];
                        if (!remap.TryGetValue(sourceIndex, out int index))
                        {
                            index = outputVertices.Count; remap.Add(sourceIndex, index);
                            outputVertices.Add(vertices[sourceIndex]); outputNormals.Add(normals[sourceIndex]);
                            if (uv.Length != 0) outputUV.Add(uv[sourceIndex]);
                            if (tangents.Length != 0) outputTangents.Add(tangents[sourceIndex]);
                        }
                        outputIndices[slot].Add(index);
                    }
                }
            // The unused streams generated for rectangles are discarded when source did not have that stream.
            // Existing streams and every untouched triangle retain their original values and winding.
            if (uv.Length == 0) outputUV.Clear();
            if (tangents.Length == 0) outputTangents.Clear();
            var result = new Mesh { name = source.name,
                indexFormat = outputVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                result.SetVertices(outputVertices); result.SetNormals(outputNormals);
                if (uv.Length != 0) result.SetUVs(0, outputUV);
                if (tangents.Length != 0) result.SetTangents(outputTangents);
                result.subMeshCount = input.Length;
                for (int slot = 0; slot < input.Length; slot++)
                {
                    result.SetTriangles(outputIndices[slot], slot, false);
                    double before = Area(vertices, input[slot]), after = Area(outputVertices, outputIndices[slot]);
                    if (Math.Abs(before - after) > Math.Max(1e-5, before * 1e-6))
                        throw new InvalidOperationException("Diagonal merge changed submesh " + slot + " surface area.");
                }
                result.RecalculateBounds();
                var expectedBounds = ReferencedBounds(vertices, input);
                if ((expectedBounds.min - result.bounds.min).sqrMagnitude > Epsilon * Epsilon ||
                    (expectedBounds.max - result.bounds.max).sqrMagnitude > Epsilon * Epsilon)
                    throw new InvalidOperationException("Diagonal merge changed referenced outer bounds.");
                return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }

        static bool TryTriangle(Vector3[] vertices, Vector3[] normals, Vector4[] tangents,
            int ia, int ib, int ic, int slot, out Face face, out Vector2Int cell, out int mask)
        {
            face = default; cell = default; mask = 0;
            var p = vertices[ia]; var q = vertices[ib]; var r = vertices[ic];
            var geometricNormal = Vector3.Cross(q - p, r - p);
            if (Mathf.Abs(geometricNormal.magnitude - RootTwo) > Epsilon) return false;
            geometricNormal /= RootTwo;
            int a = -1, b = -1, third = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(geometricNormal[axis]) < Epsilon) third = axis;
                else if (a < 0) a = axis;
                else if (b < 0) b = axis;
                else return false;
            }
            if (a < 0 || b < 0 || third < 0) return false;
            int sa = geometricNormal[a] > 0 ? 1 : -1, sb = geometricNormal[b] > 0 ? 1 : -1;
            var expected = Vector3.zero; expected[a] = sa / RootTwo; expected[b] = sb / RootTwo;
            if ((geometricNormal - expected).sqrMagnitude > Epsilon * Epsilon) return false;
            // Reject smooth shading or tangent interpolation; one attribute value must describe the entire face.
            if ((normals[ia] - expected).sqrMagnitude > Epsilon * Epsilon ||
                !normals[ia].Equals(normals[ib]) || !normals[ia].Equals(normals[ic])) return false;
            var tangent = tangents.Length == 0 ? Vector4.zero : tangents[ia];
            if (tangents.Length != 0 && (!tangent.Equals(tangents[ib]) || !tangent.Equals(tangents[ic]))) return false;
            float d = sa * p[a] + sb * p[b]; int plane = Mathf.RoundToInt(d);
            if (Mathf.Abs(d - plane) > Epsilon ||
                Mathf.Abs(sa * q[a] + sb * q[b] - plane) > Epsilon ||
                Mathf.Abs(sa * r[a] + sb * r[b] - plane) > Epsilon) return false;
            float pu = (sa * p[a] - sb * p[b]) * .5f;
            float qu = (sa * q[a] - sb * q[b]) * .5f;
            float ru = (sa * r[a] - sb * r[b]) * .5f;
            float minU = Mathf.Min(pu, Mathf.Min(qu, ru));
            float minV = Mathf.Min(p[third], Mathf.Min(q[third], r[third]));
            float phase = (plane & 1) * .5f;
            int gridU = Mathf.RoundToInt(minU + .5f - phase), gridV = Mathf.RoundToInt(minV + .5f);
            if (Mathf.Abs(minU - (gridU - .5f + phase)) > Epsilon ||
                Mathf.Abs(minV - (gridV - .5f)) > Epsilon) return false;
            for (int corner = 0; corner < 3; corner++)
            {
                var point = corner == 0 ? p : corner == 1 ? q : r;
                float du = (sa * point[a] - sb * point[b]) * .5f - minU, dv = point[third] - minV;
                int x = Mathf.RoundToInt(du), y = Mathf.RoundToInt(dv);
                if (x < 0 || x > 1 || y < 0 || y > 1 ||
                    Mathf.Abs(du - x) > Epsilon || Mathf.Abs(dv - y) > Epsilon) return false;
                int bit = 1 << (y == 0 ? x : 3 - x);
                if ((mask & bit) != 0) return false; mask |= bit;
            }
            face = new Face { slot = slot, a = a, b = b, sa = sa, sb = sb, plane = plane,
                normal = normals[ia], tangent = tangent };
            cell = new Vector2Int(gridU, gridV); return true;
        }

        static Vector3 Point(Face face, float u, float v)
        {
            var point = Vector3.zero;
            point[face.a] = face.sa * (u + face.plane * .5f);
            point[face.b] = face.sb * (face.plane * .5f - u);
            point[3 - face.a - face.b] = v;
            return point;
        }
        static void AddRectangle(Rectangle rectangle, List<Vector3> vertices, List<Vector3> normals,
            List<Vector2> uv, List<Vector4> tangents, List<int> indices)
        {
            var face = rectangle.face; int start = vertices.Count;
            float minU = rectangle.cell.x - .5f + (face.plane & 1) * .5f, minV = rectangle.cell.y - .5f;
            for (int corner = 0; corner < 4; corner++)
            {
                float u = minU + (corner == 1 || corner == 2 ? rectangle.width : 0);
                float v = minV + (corner >= 2 ? rectangle.height : 0);
                vertices.Add(Point(face, u, v)); normals.Add(face.normal); uv.Add(new Vector2(u, v)); tangents.Add(face.tangent);
            }
            bool forward = Vector3.Dot(Vector3.Cross(vertices[start + 1] - vertices[start],
                vertices[start + 2] - vertices[start]), face.normal) > 0;
            indices.Add(start); indices.Add(start + (forward ? 1 : 2)); indices.Add(start + (forward ? 2 : 1));
            indices.Add(start); indices.Add(start + (forward ? 2 : 3)); indices.Add(start + (forward ? 3 : 2));
        }
        static double Area(IList<Vector3> vertices, IList<int> indices)
        {
            double area = 0;
            for (int t = 0; t < indices.Count; t += 3)
                area += Vector3.Cross(vertices[indices[t + 1]] - vertices[indices[t]],
                    vertices[indices[t + 2]] - vertices[indices[t]]).magnitude * .5;
            return area;
        }
        static Bounds ReferencedBounds(Vector3[] vertices, int[][] indices)
        {
            bool first = true; var bounds = new Bounds();
            foreach (var slot in indices) foreach (int index in slot)
            {
                if (first) { bounds = new Bounds(vertices[index], Vector3.zero); first = false; }
                else bounds.Encapsulate(vertices[index]);
            }
            return bounds;
        }
    }
}

