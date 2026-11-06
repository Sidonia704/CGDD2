using System;
using System.Collections.Generic;
using Starframe.Construction;
using Starframe.EditorTools;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

public static class FullCellCoverageCheck
{
    public static object Run()
    {
        var database = AssetDatabase.LoadAssetAtPath<ModuleDatabase>(SceneVoxelModules.Root + "/SceneVoxelModules.asset");
        if (!database) throw new Exception("Shared scenery module database is missing.");
        var eight = database.Get(BlockShape.Block8).occupiedCells;
        var seven = database.Get(BlockShape.Block7).occupiedCells;
        var block8 = new VoxelCellState { position = Vector3Int.zero, shape = BlockShape.Block8, modelOffset = eight[0] };
        var rotated8 = new VoxelCellState { position = Vector3Int.right, shape = BlockShape.Block8,
            modelOffset = eight[eight.Length - 1], quarterTurns = new Vector3Int(1, 1, 0) };
        CheckArea(database, new[] { block8 }, 6, "single full Block8 partition");
        CheckArea(database, new[] { block8, rotated8 }, 10, "distinct rotated Block8 sources");
        CheckArea(database, new[] { block8, new VoxelCellState { position = Vector3Int.right, shape = BlockShape.Cube } }, 10, "Block8 then Cube");
        CheckArea(database, new[] { new VoxelCellState { position = Vector3Int.zero, shape = BlockShape.Cube }, rotated8 }, 10, "Cube then rotated Block8");
        var half = new VoxelCellState { position = Vector3Int.right, shape = BlockShape.Block7, modelOffset = seven[0] };
        var cube = new VoxelCellState { position = Vector3Int.zero, shape = BlockShape.Cube };
        var halfMesh = Build(database, new[] { cube, half });
        try
        {
            float exposedCubeFace = FaceArea(halfMesh, .5f, Vector3.right);
            if (Mathf.Abs(exposedCubeFace - 1f) > .0001f)
                throw new Exception("Half-height Block7 incorrectly covered adjacent Cube face: " + exposedCubeFace);
        }
        finally { UnityEngine.Object.DestroyImmediate(halfMesh); }
        CheckSamplerContacts();
        return new { fullBlock8Area = 6, joinedArea = 10, block7ExposedCubeFace = 1,
            distinctOrigins = true, rotatedPartitions = true, samplerZeroAreaContactsRejected = true,
            samplerUnitSheet = true, samplerBoundarySolidSide = true };
    }
    static Mesh Build(ModuleDatabase database, VoxelCellState[] source)
    {
        var cells = new Dictionary<Vector3Int, VoxelCellState>();
        foreach (var cell in source) cells.Add(cell.position, cell);
        return VoxelHullAppearance.Build(cells, VoxelHullAppearance.ShapeCount, 1f, database);
    }
    static void CheckArea(ModuleDatabase database, VoxelCellState[] cells, float expected, string label)
    {
        var mesh = Build(database, cells);
        try
        {
            float area = Area(mesh);
            if (Mathf.Abs(area - expected) > .0001f) throw new Exception(label + " area " + area + " expected " + expected);
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
    static float Area(Mesh mesh)
    {
        var points = mesh.vertices; var indices = mesh.triangles; float sum = 0;
        for (int i = 0; i < indices.Length; i += 3)
            sum += Vector3.Cross(points[indices[i+1]] - points[indices[i]], points[indices[i+2]] - points[indices[i]]).magnitude * .5f;
        return sum;
    }
    static float FaceArea(Mesh mesh, float x, Vector3 direction)
    {
        var points = mesh.vertices; var indices = mesh.triangles; float sum = 0;
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = points[indices[i]]; var b = points[indices[i+1]]; var c = points[indices[i+2]];
            var cross = Vector3.Cross(b-a, c-a);
            if (Mathf.Abs(a.x-x) < .00001f && Mathf.Abs(b.x-x) < .00001f && Mathf.Abs(c.x-x) < .00001f && Vector3.Dot(cross.normalized, direction) > .9999f)
                sum += cross.magnitude * .5f;
        }
        return sum;
    }
    static void CheckSamplerContacts()
    {
        var indices = new[] { 0, 1, 2 };
        var sheet = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(-.5f,.5f,0) };
        var sampled = UniformSurfaceSampler.Build(sheet, indices, new[] { 0 }, 100);
        if (sampled.Count != 1 || sampled[0].position != Vector3Int.zero)
            throw new Exception("Triangle edge/corner contact thickened a unit sheet: " + sampled.Count);
        for (int i = 0; i < sheet.Length; i++) sheet[i].z = .5f;
        sampled = UniformSurfaceSampler.Build(sheet, indices, new[] { 0 }, 100);
        if (sampled.Count != 1 || sampled[0].position != Vector3Int.zero)
            throw new Exception("Boundary sheet failed solid-side selection: " + sampled.Count);
    }
}
