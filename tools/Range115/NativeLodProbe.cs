using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Starframe.EditorTools;

/// <summary>External Editor probe; Run once through the Unity CLI execute-script entry point.</summary>
public static class NativeLodProbe
{
    public static object Run()
    {
        var results = new List<object>();
        foreach (int index in new[] { 0, 8, 15 })
        {
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(UniformBackdropBuilder.RenderPath(index));
            if (!source) throw new InvalidOperationException("Missing exact world-unit render source: " + index);
            foreach (float error in new[] { 4f, 8f })
            {
                Mesh lod = NativeRockLodBuilder.Build(source, error, out var stats);
                try
                {
                    float supportDelta = MaximumSupportDelta(source.vertices, lod.vertices);
                    results.Add(new
                    {
                        index, diameter = UniformBackdropBuilder.Diameter(index), requestedError = error,
                        stats.inputVertices, stats.outputVertices, stats.uniqueSourcePositions, stats.clusterCount,
                        stats.inputTriangles, stats.outputTriangles, stats.degenerateTriangles, stats.duplicateTriangles,
                        stats.maximumActualDisplacement, stats.gridWidth,
                        sourceBounds = stats.sourceBounds.ToString("F4"), geometryBounds = stats.geometryBounds.ToString("F4"),
                        conservativeBoundsPreserved = lod.bounds.Equals(source.bounds),
                        maximumSupportDelta26 = supportDelta,
                        displacementWithinLimit = stats.maximumActualDisplacement <= error + .0005f,
                        silhouetteSupportWithinLimit = supportDelta <= error + .001f,
                        requestedTriangleTarget = error == 4f ? 12000 : 4000,
                        triangleTargetMet = stats.outputTriangles <= (error == 4f ? 12000 : 4000)
                    });
                }
                finally { UnityEngine.Object.DestroyImmediate(lod); }
            }
        }
        return results;
    }

    static float MaximumSupportDelta(Vector3[] source, Vector3[] lod)
    {
        if (lod.Length == 0) return float.PositiveInfinity;
        float result = 0;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    Vector3 direction = new Vector3(x, y, z).normalized;
                    float a = float.NegativeInfinity, b = float.NegativeInfinity;
                    foreach (Vector3 p in source) a = Mathf.Max(a, Vector3.Dot(p, direction));
                    foreach (Vector3 p in lod) b = Mathf.Max(b, Vector3.Dot(p, direction));
                    result = Mathf.Max(result, Mathf.Abs(a - b));
                }
        return result;
    }
}
