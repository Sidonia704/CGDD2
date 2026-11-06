using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.EditorTools
{
    /// <summary>Render-only clustering of native world-unit rock vertices. Does not modify its source.</summary>
    public static class NativeRockLodBuilder
    {
        public struct Stats
        {
            public int inputVertices, outputVertices, uniqueSourcePositions, clusterCount;
            public int inputTriangles, outputTriangles, degenerateTriangles, duplicateTriangles;
            public float maximumVertexError, maximumActualDisplacement, gridWidth;
            public Bounds sourceBounds, geometryBounds;
            public float maxDisplacement => maximumActualDisplacement;
            public override string ToString() => inputTriangles + " -> " + outputTriangles +
                " triangles; displacement " + maximumActualDisplacement + " / " + maximumVertexError;
        }

        struct GridKey : IEquatable<GridKey>
        {
            public long x, y, z;
            public bool Equals(GridKey b) => x == b.x && y == b.y && z == b.z;
            public override bool Equals(object b) => b is GridKey k && Equals(k);
            public override int GetHashCode()
            { unchecked { return ((x.GetHashCode() * 397) ^ y.GetHashCode()) * 397 ^ z.GetHashCode(); } }
        }
        sealed class Cluster
        {
            public GridKey key;
            public readonly List<int> members = new List<int>();
            public double x, y, z;
            public Vector3 target;
        }
        struct AttributeKey : IEquatable<AttributeKey>
        {
            public int cluster;
            public Vector3 normal;
            public Vector4 tangent;
            public bool Equals(AttributeKey b) => cluster == b.cluster && normal.Equals(b.normal) && tangent.Equals(b.tangent);
            public override bool Equals(object b) => b is AttributeKey k && Equals(k);
            public override int GetHashCode()
            { unchecked { return ((cluster * 397) ^ normal.GetHashCode()) * 397 ^ tangent.GetHashCode(); } }
        }
        struct TriangleKey : IEquatable<TriangleKey>
        {
            public int a, b, c;
            public TriangleKey(int x, int y, int z)
            {
                // Cyclic rotations are identical. Reversed winding is deliberately distinct.
                if (x <= y && x <= z) { a = x; b = y; c = z; }
                else if (y <= x && y <= z) { a = y; b = z; c = x; }
                else { a = z; b = x; c = y; }
            }
            public bool Equals(TriangleKey t) => a == t.a && b == t.b && c == t.c;
            public override bool Equals(object b) => b is TriangleKey t && Equals(t);
            public override int GetHashCode()
            { unchecked { return ((a * 397) ^ b) * 397 ^ c; } }
        }

        /// <summary>
        /// Builds a caller-owned single-material mesh at original scale. Every occurrence of a geometric
        /// position shares one target, including normal/UV seams. UVs are intentionally omitted: this
        /// render-only path is for the untextured Backdrop shader. Original normals/tangents are retained.
        /// The error limit is vertex displacement, not a certified surface Hausdorff distance.
        /// </summary>
        public static Mesh Build(Mesh source, float maximumVertexError, out Stats stats)
        {
            if (!source || !source.isReadable) throw new ArgumentException("A readable native source mesh is required.", nameof(source));
            if (float.IsNaN(maximumVertexError) || float.IsInfinity(maximumVertexError) || maximumVertexError <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumVertexError));
            if (source.blendShapeCount != 0) throw new ArgumentException("Backdrop sources cannot have blend shapes.", nameof(source));
            var positions = source.vertices;
            var sourceNormals = source.normals;
            var sourceTangents = source.tangents;
            bool hasNormals = sourceNormals.Length == positions.Length;
            bool hasTangents = sourceTangents.Length == positions.Length;
            stats = new Stats { inputVertices = positions.Length, maximumVertexError = maximumVertexError, sourceBounds = source.bounds };
            // The grid-centre fallback has radius <= error; a tiny margin absorbs float conversion.
            double radius = maximumVertexError * (1.0 - 0.00001);
            double width = 2.0 * radius / Math.Sqrt(3.0);
            stats.gridWidth = (float)width;
            var unique = new List<Vector3>();
            var positionIds = new Dictionary<Vector3, int>();
            var sourcePosition = new int[positions.Length];
            var uniqueCluster = new List<int>();
            var grid = new Dictionary<GridKey, int>();
            var clusters = new List<Cluster>();
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = Canonical(positions[i]);
                if (!Finite(p)) throw new ArgumentException("Source contains a non-finite vertex.", nameof(source));
                if (!positionIds.TryGetValue(p, out int id))
                {
                    id = unique.Count; unique.Add(p); positionIds.Add(p, id);
                    var key = new GridKey { x = (long)Math.Floor(p.x / width), y = (long)Math.Floor(p.y / width), z = (long)Math.Floor(p.z / width) };
                    if (!grid.TryGetValue(key, out int clusterId))
                    { clusterId = clusters.Count; grid.Add(key, clusterId); clusters.Add(new Cluster { key = key }); }
                    uniqueCluster.Add(clusterId);
                    var group = clusters[clusterId];
                    group.members.Add(id); group.x += p.x; group.y += p.y; group.z += p.z;
                }
                sourcePosition[i] = id;
            }
            stats.uniqueSourcePositions = unique.Count; stats.clusterCount = clusters.Count;
            foreach (var group in clusters)
            {
                Vector3 centre = new Vector3((float)((group.key.x + .5) * width),
                    (float)((group.key.y + .5) * width), (float)((group.key.z + .5) * width));
                Vector3 mean = new Vector3((float)(group.x / group.members.Count),
                    (float)(group.y / group.members.Count), (float)(group.z / group.members.Count));
                // Feasible targets form a convex intersection of radius-error balls. Move from the
                // guaranteed centre towards the mean only while all member displacements remain valid.
                if (Fits(mean, group, unique, radius)) group.target = mean;
                else
                {
                    float lo = 0, hi = 1;
                    for (int step = 0; step < 24; step++)
                    {
                        float t = (lo + hi) * .5f;
                        if (Fits(Vector3.LerpUnclamped(centre, mean, t), group, unique, radius)) lo = t;
                        else hi = t;
                    }
                    group.target = Vector3.LerpUnclamped(centre, mean, lo);
                }
            }
            // Clamping to the original culling AABB cannot increase distance to source points inside it.
            foreach (var group in clusters)
                group.target = Vector3.Max(source.bounds.min, Vector3.Min(source.bounds.max, group.target));
            var vertexCluster = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                int cluster = uniqueCluster[sourcePosition[i]]; vertexCluster[i] = cluster;
                stats.maximumActualDisplacement = Mathf.Max(stats.maximumActualDisplacement,
                    Vector3.Distance(positions[i], clusters[cluster].target));
            }
            // Include canonicalisation error in the actual check. Never silently exceed the requested bound.
            if (stats.maximumActualDisplacement > maximumVertexError + .0005f)
                throw new InvalidOperationException("Native LOD vertex error exceeds its limit: " + stats.maximumActualDisplacement);
            var outputPositions = new List<Vector3>();
            var outputNormals = new List<Vector3>();
            var outputTangents = new List<Vector4>();
            var outputIndices = new List<int>();
            var attributes = new Dictionary<AttributeKey, int>();
            var triangles = new HashSet<TriangleKey>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles)
                    throw new ArgumentException("Native LOD requires triangle topology.", nameof(source));
                var indices = source.GetTriangles(sub);
                stats.inputTriangles += indices.Length / 3;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    int ca = vertexCluster[a], cb = vertexCluster[b], cc = vertexCluster[c];
                    Vector3 pa = clusters[ca].target, pb = clusters[cb].target, pc = clusters[cc].target;
                    if (ca == cb || cb == cc || cc == ca || Vector3.Cross(pb - pa, pc - pa).sqrMagnitude <= 1e-12f)
                    { stats.degenerateTriangles++; continue; }
                    if (!triangles.Add(new TriangleKey(ca, cb, cc))) { stats.duplicateTriangles++; continue; }
                    Vector3 faceNormal = hasNormals ? Vector3.zero : Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).normalized;
                    outputIndices.Add(OutputVertex(a, ca, faceNormal));
                    outputIndices.Add(OutputVertex(b, cb, faceNormal));
                    outputIndices.Add(OutputVertex(c, cc, faceNormal));
                }
            }
            int OutputVertex(int original, int cluster, Vector3 fallbackNormal)
            {
                var key = new AttributeKey { cluster = cluster, normal = hasNormals ? sourceNormals[original] : fallbackNormal,
                    tangent = hasTangents ? sourceTangents[original] : Vector4.zero };
                if (attributes.TryGetValue(key, out int existing)) return existing;
                int result = outputPositions.Count; attributes.Add(key, result);
                outputPositions.Add(clusters[cluster].target); outputNormals.Add(key.normal);
                if (hasTangents) outputTangents.Add(key.tangent);
                return result;
            }
            var mesh = new Mesh { name = source.name + " native LOD " + maximumVertexError + "m",
                indexFormat = outputPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(outputPositions); mesh.SetNormals(outputNormals);
            if (hasTangents) mesh.SetTangents(outputTangents);
            var white = new List<Color32>(outputPositions.Count);
            for (int i = 0; i < outputPositions.Count; i++) white.Add(new Color32(255, 255, 255, 255));
            mesh.SetColors(white); mesh.SetTriangles(outputIndices, 0, false);
            mesh.RecalculateBounds(); stats.geometryBounds = mesh.bounds;
            mesh.bounds = source.bounds; // Preserve conservative native culling bounds and world-unit extent.
            stats.outputVertices = mesh.vertexCount; stats.outputTriangles = outputIndices.Count / 3;
            mesh.UploadMeshData(false);
            return mesh;
        }

        static bool Fits(Vector3 target, Cluster group, List<Vector3> unique, double radius)
        {
            double squared = radius * radius;
            foreach (int index in group.members)
            {
                Vector3 p = unique[index];
                double x = target.x - p.x, y = target.y - p.y, z = target.z - p.z;
                if (x * x + y * y + z * z > squared) return false;
            }
            return true;
        }
        static Vector3 Canonical(Vector3 p) => new Vector3(Half(p.x), Half(p.y), Half(p.z));
        static float Half(float v)
        {
            float half = Mathf.Round(v * 2f) * .5f;
            return Mathf.Abs(v - half) <= .0001f ? half : v;
        }
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
    }
}

