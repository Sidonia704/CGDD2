using System;
using System.Collections.Generic;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    public static class SceneVoxelModules
    {
        public const string Root = "Assets/_Starframe/Maps/SharedVoxelModules";
        const string DatabasePath = Root + "/SceneVoxelModules.asset";
        public static ModuleDatabase GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ModuleDatabase>(DatabasePath);
            if (existing && Complete(existing)) return existing;
            var source = AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            if (!source) throw new InvalidOperationException("Original module database is missing.");
            EnsureFolder(Root);
            var database = existing ? existing : ScriptableObject.CreateInstance<ModuleDatabase>();
            database.name = "Scene Voxel Modules";
            var modules = new List<ModuleDefinition>();
            foreach (var shape in VoxelHullAppearance.Shapes)
            {
                var original = source.Get(shape);
                if (!original) throw new InvalidOperationException("Original hull module is missing: " + shape);
                // Compatibility only: authored scenery converts decorative full bevelled cubes to Cube.
                if (shape == BlockShape.Block5 || shape == BlockShape.Block6) { modules.Add(original); continue; }
                string path = Root + "/" + shape + ".asset";
                var definition = AssetDatabase.LoadAssetAtPath<ModuleDefinition>(path);
                if (definition) EditorUtility.CopySerialized(original, definition);
                else { definition = UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(definition, path); }
                definition.name = "Scene " + shape;
                definition.occupiedCells = (Vector3Int[])original.occupiedCells.Clone();
                definition.drawOutline = false; definition.outline = null;
                var built = CreateGeometry(shape, definition.occupiedCells);
                definition.mesh = StoreMesh(built, Root + "/" + shape + "Mesh.asset");
                if (original.material)
                {
                    string materialPath = Root + "/" + shape + "Material.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material) EditorUtility.CopySerialized(original.material, material);
                    else { material = new Material(original.material); AssetDatabase.CreateAsset(material, materialPath); }
                    material.name = "Scene " + shape + " Material"; EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
                    definition.material = material;
                }
                EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition); modules.Add(definition);
            }
            database.modules = modules;
            if (!existing) AssetDatabase.CreateAsset(database, DatabasePath);
            EditorUtility.SetDirty(database); AssetDatabase.SaveAssetIfDirty(database);
            return database;
        }
        static bool Complete(ModuleDatabase database)
        {
            foreach (var shape in VoxelHullAppearance.Shapes)
            {
                var definition = database.Get(shape);
                if (!definition || !definition.mesh) return false;
                if (shape != BlockShape.Block5 && shape != BlockShape.Block6 &&
                    AssetDatabase.GetAssetPath(definition) != Root + "/" + shape + ".asset") return false;
            }
            return true;
        }
        static Mesh CreateGeometry(BlockShape shape, Vector3Int[] occupied)
        {
            if (shape != BlockShape.Block7 && shape != BlockShape.Block8) return BlockGeometry.CreateSolid(shape);
            if (occupied == null || occupied.Length == 0) throw new InvalidOperationException("Multi-cell module has no occupied cells.");
            var bounds = new Bounds((Vector3)occupied[0], Vector3.one);
            foreach (var cell in occupied) bounds.Encapsulate(new Bounds((Vector3)cell, Vector3.one));
            var mesh = BlockGeometry.CreateSolid(BlockShape.Cube);
            var vertices = mesh.vertices;
            // Block7 reserves two full cells, but its actual geometry fills only their lower half.
            var geometry = bounds;
            if (shape == BlockShape.Block7)
            {
                var min = bounds.min; var max = bounds.max; max.y = min.y + bounds.size.y * .5f;
                geometry.SetMinMax(min, max);
            }
            for (int i = 0; i < vertices.Length; i++) vertices[i] = geometry.center + Vector3.Scale(vertices[i], geometry.size);
            mesh.vertices = vertices; mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }
        static Mesh StoreMesh(Mesh built, string path)
        {
            built.name = System.IO.Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing) { AssetDatabase.CreateAsset(built, path); return built; }
            try { EditorUtility.CopySerialized(built, existing); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); return existing; }
            finally { UnityEngine.Object.DestroyImmediate(built); }
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
