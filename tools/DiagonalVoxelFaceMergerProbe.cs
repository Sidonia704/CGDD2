using System;
using System.Collections.Generic;
using Starframe.EditorTools;
using UnityEngine;

// External editor probe: compile/evaluate this source in the existing Editor, then invoke Run().
// Does not create objects, edit assets or enter Play.
public static class DiagonalVoxelFaceMergerProbe
{
    static int cases;
    public static object Run()
    {
        cases = 0;
        for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
            foreach (int sa in new[] { -1, 1 }) foreach (int sb in new[] { -1, 1 })
                for (int plane = -3; plane <= 3; plane++)
                    Check(Build(a, b, sa, sb, plane, 3, 2, false, false, false, false), 2, 6, "orientation/parity");
        Check(Build(0, 1, 1, 1, 1, 3, 3, true, false, false, false), 8, 8, "hole");
        Check(Build(0, 2, -1, 1, -1, 2, 1, false, true, false, false), 3, 1, "partial triangle", true);
        Check(Build(1, 2, 1, -1, 2, 2, 1, false, false, true, false), 4, 2, "submesh isolation", true);
        Check(Build(0, 1, 1, 1, 0, 2, 1, false, false, false, true), 4, 2, "tangent seam", true);
        CheckDuplicate();
        return new { passed = cases, message = "Exact diagonal rectangles: 84 orientation/parity cases, hole, partial, material, tangent seam and duplicate coverage." };
    }

    static Mesh Build(int a, int b, int sa, int sb, int plane, int width, int height,
        bool hole, bool partial, bool splitMaterial, bool splitTangent)
    {
        var vertices = new List<Vector3>(); var normals = new List<Vector3>();
        var tangents = new List<Vector4>(); var uv = new List<Vector2>();
        var indices = new[] { new List<int>(), new List<int>() };
        var normal = Vector3.zero; normal[a] = sa; normal[b] = sb; normal.Normalize();
        int third = 3 - a - b;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (hole && x == 1 && y == 1) continue;
            int start = vertices.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                float u = x - .5f + (plane & 1) * .5f + (corner == 1 || corner == 2 ? 1 : 0);
                float v = y - .5f + (corner >= 2 ? 1 : 0);
                var point = Vector3.zero;
                point[a] = sa * (u + plane * .5f); point[b] = sb * (plane * .5f - u); point[third] = v;
                vertices.Add(point); normals.Add(normal); uv.Add(new Vector2(u, v));
                var tangent = Vector4.zero; tangent[third] = splitTangent && x == 1 ? -1 : 1; tangent.w = 1;
                tangents.Add(tangent);
            }
            bool forward = Vector3.Dot(Vector3.Cross(vertices[start + 1] - vertices[start],
                vertices[start + 2] - vertices[start]), normal) > 0;
            var output = indices[splitMaterial && x == 1 ? 1 : 0];
            output.Add(start); output.Add(start + (forward ? 1 : 2)); output.Add(start + (forward ? 2 : 1));
            if (!partial || x != 1)
            {
                output.Add(start); output.Add(start + (forward ? 2 : 3)); output.Add(start + (forward ? 3 : 2));
            }
        }
        var mesh = new Mesh { name = "DiagonalSynthetic" };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTangents(tangents);
        mesh.subMeshCount = 2; mesh.SetTriangles(indices[0], 0); mesh.SetTriangles(indices[1], 1); mesh.RecalculateBounds();
        return mesh;
    }
    static void Check(Mesh source, int expectedTriangles, int expectedFaces, string label, bool expectSource = false)
    {
        Mesh output = null;
        try
        {
            var originalVertices = source.vertices; var originalTriangles = source.triangles;
            output = DiagonalVoxelFaceMerger.Merge(source, out var stats);
            if (stats.outputTriangles != expectedTriangles || output.triangles.Length / 3 != expectedTriangles ||
                stats.completeFaces != expectedFaces || output.subMeshCount != source.subMeshCount ||
                (expectSource && output != source))
                throw new Exception(label + " failed: " + stats);
            var vertices = source.vertices; var triangles = source.triangles;
            if (vertices.Length != originalVertices.Length || triangles.Length != originalTriangles.Length)
                throw new Exception("Source modified");
            for (int i = 0; i < triangles.Length; i++) if (triangles[i] != originalTriangles[i]) throw new Exception("Source indices modified");
            for (int i = 0; i < vertices.Length; i++) if (!vertices[i].Equals(originalVertices[i])) throw new Exception("Source vertices modified");
            // Independent area and oriented-normal check, per material slot.
            for (int slot = 0; slot < source.subMeshCount; slot++)
            {
                if (Math.Abs(Area(source, slot) - Area(output, slot)) > .00001) throw new Exception(label + " submesh area");
                var outputIndices = output.GetTriangles(slot); var outputVertices = output.vertices; var outputNormals = output.normals;
                for (int t = 0; t < outputIndices.Length; t += 3)
                {
                    int i = outputIndices[t], j = outputIndices[t + 1], k = outputIndices[t + 2];
                    if (Vector3.Dot(Vector3.Cross(outputVertices[j] - outputVertices[i], outputVertices[k] - outputVertices[i]), outputNormals[i]) <= 0)
                        throw new Exception(label + " winding");
                }
            }
            if (label == "hole")
            {
                // Hole centre in this test's projection is u=1.5, v=1, plane=1.
                var holeCentre = new Vector3(2f, -1f, 1);
                foreach (int slot in new[] { 0, 1 })
                {
                    var outputIndices = output.GetTriangles(slot); var outputVertices = output.vertices;
                    for (int t = 0; t < outputIndices.Length; t += 3)
                        if (Inside(holeCentre, outputVertices[outputIndices[t]], outputVertices[outputIndices[t + 1]], outputVertices[outputIndices[t + 2]]))
                            throw new Exception("Hole filled");
                }
            }
            cases++;
        }
        finally { if (output && output != source) UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(source); }
    }
    static void CheckDuplicate()
    {
        var source = Build(0, 1, 1, 1, 0, 1, 1, false, false, false, false);
        var t = source.GetTriangles(0); source.SetTriangles(new[] { t[0], t[1], t[2], t[3], t[4], t[5], t[0], t[1], t[2], t[3], t[4], t[5] }, 0);
        Check(source, 4, 0, "duplicate full coverage", true);
    }
    static double Area(Mesh mesh, int slot)
    {
        var vertices = mesh.vertices; var indices = mesh.GetTriangles(slot); double result = 0;
        for (int t = 0; t < indices.Length; t += 3)
            result += Vector3.Cross(vertices[indices[t + 1]] - vertices[indices[t]], vertices[indices[t + 2]] - vertices[indices[t]]).magnitude * .5;
        return result;
    }
    static bool Inside(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var v0 = b - a; var v1 = c - a; var v2 = p - a;
        float aa = Vector3.Dot(v0, v0), ab = Vector3.Dot(v0, v1), bb = Vector3.Dot(v1, v1);
        float pa = Vector3.Dot(v2, v0), pb = Vector3.Dot(v2, v1), denominator = aa * bb - ab * ab;
        float u = (bb * pa - ab * pb) / denominator, v = (aa * pb - ab * pa) / denominator;
        return u >= -.00001f && v >= -.00001f && u + v <= 1.00001f;
    }
}


