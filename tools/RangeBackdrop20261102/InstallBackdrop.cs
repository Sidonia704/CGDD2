using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    /// <summary>Native authoring of the backdrop's four shared voxel-shell meshes, material, and range component.</summary>
    public static class RangeBackdropInstall
    {
        const string ScenePath = "Assets/_Starframe/Scenes/Testing/FlightRange.unity";
        const string ShaderPath = "Assets/_Starframe/Art/Shaders/Environment/RangeAsteroidBackdrop.shader";
        const string MaterialPath = "Assets/_Starframe/Art/Materials/Environment/RangeAsteroidBackdrop.mat";
        const string MeshFolder = "Assets/_Starframe/Art/Models/Environment";

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before installing the range backdrop.");
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Range asteroid shader is missing or unsupported.");
            if (ShaderUtil.GetShaderMessages(shader).Any(message =>
                message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                throw new InvalidOperationException("Range asteroid shader has compile errors.");
            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder("Assets/_Starframe/Art/Models", "Environment");

            var highA = RockAsset("RangeRockA_High", 0, true);
            var highB = RockAsset("RangeRockB_High", 1, true);
            var lowA = RockAsset("RangeRockA_Low", 0, false);
            var lowB = RockAsset("RangeRockB_Low", 1, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "Range Asteroid Backdrop" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.enableInstancing = true;
            // The reference map uses untinted assets with AssemblyPlastic's neutral .93 hull.
            material.SetColor("_BaseColor", new Color(.93f, .93f, .93f, 1f));
            material.SetFloat("_Smoothness", .6f);
            material.SetFloat("_NearFadeStart", 80f);
            material.SetFloat("_NearFadeEnd", 350f);
            material.SetFloat("_FarFadeStart", 17000f);
            material.SetFloat("_FarFadeEnd", 25000f);
            EditorUtility.SetDirty(material);

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var bootstrap = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<FlightRangeBootstrap>(true)).FirstOrDefault();
                if (!bootstrap) throw new InvalidOperationException("FlightRangeBootstrap was not found in the range scene.");
                var camera = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                    .FirstOrDefault(value => value.CompareTag("MainCamera"));
                if (!camera) throw new InvalidOperationException("The range main camera was not found.");
                var backdrop = bootstrap.GetComponent<RangeAsteroidBackdrop>();
                if (!backdrop) backdrop = bootstrap.gameObject.AddComponent<RangeAsteroidBackdrop>();
                backdrop.Configure(camera, highA, highB, lowA, lowB, material);
                backdrop.backdropEnabled = true;
                EditorUtility.SetDirty(backdrop);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the range scene.");
                AssetDatabase.SaveAssets();
                return "Range backdrop installed: 50 km world-anchored Z span, four shared voxel-shell meshes, one instanced material, no collision or individual GameObjects.";
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        static Mesh RockAsset(string name, int variant, bool detailed)
        {
            var path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var rock = BuildRock(name, variant, detailed);
            if (existing)
            {
                // Rewrite the persistent asset in place so scene references keep the same GUID.
                existing.Clear();
                existing.vertices = rock.vertices;
                existing.normals = rock.normals;
                existing.colors32 = rock.colors32;
                existing.triangles = rock.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(rock);
                return existing;
            }
            AssetDatabase.CreateAsset(rock, path);
            return rock;
        }

        static Mesh BuildRock(string name, int variant, bool detailed)
        {
            int resolution = detailed ? 5 : 3;
            float step = 2f / resolution;
            var solid = new bool[resolution, resolution, resolution];
            float axisX = variant == 0 ? .92f : .79f;
            float axisY = variant == 0 ? .84f : .93f;
            float axisZ = variant == 0 ? 1.02f : 1.08f;
            for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                    for (int z = 0; z < resolution; z++)
                    {
                        var center = new Vector3(-1f + (x + .5f) * step,
                            -1f + (y + .5f) * step, -1f + (z + .5f) * step);
                        float envelope = center.x * center.x / (axisX * axisX)
                            + center.y * center.y / (axisY * axisY)
                            + center.z * center.z / (axisZ * axisZ);
                        float roughness = (Hash(x * 67 + y * 17 + z * 5, variant) - .5f) * .22f;
                        solid[x, y, z] = envelope < (detailed ? 1.17f : 1.42f) + roughness;
                    }

            var vertices = new List<Vector3>(detailed ? 640 : 200);
            var normals = new List<Vector3>(vertices.Capacity);
            var colors = new List<Color32>(vertices.Capacity);
            var triangles = new List<int>(vertices.Capacity * 3 / 2);
            var directions = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up,
                Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                    for (int z = 0; z < resolution; z++)
                    {
                        if (!solid[x, y, z]) continue;
                        var cellCenter = new Vector3(-1f + (x + .5f) * step,
                            -1f + (y + .5f) * step, -1f + (z + .5f) * step);
                        for (int face = 0; face < directions.Length; face++)
                        {
                            var direction = directions[face];
                            int adjacentX = x + direction.x, adjacentY = y + direction.y,
                                adjacentZ = z + direction.z;
                            if (adjacentX >= 0 && adjacentX < resolution &&
                                adjacentY >= 0 && adjacentY < resolution &&
                                adjacentZ >= 0 && adjacentZ < resolution &&
                                solid[adjacentX, adjacentY, adjacentZ]) continue;
                            AddFace(cellCenter + (Vector3)direction * step * .5f,
                                face, step * .5f, x * 67 + y * 17 + z * 5,
                                variant, vertices, normals, colors, triangles);
                        }
                    }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddFace(Vector3 center, int face, float halfSize, int cell, int variant,
            List<Vector3> vertices, List<Vector3> normals, List<Color32> colors, List<int> triangles)
        {
            Vector3 normal, u, v;
            switch (face)
            {
                case 0: normal = Vector3.right; u = Vector3.up; v = Vector3.forward; break;
                case 1: normal = Vector3.left; u = Vector3.forward; v = Vector3.up; break;
                case 2: normal = Vector3.up; u = Vector3.forward; v = Vector3.right; break;
                case 3: normal = Vector3.down; u = Vector3.right; v = Vector3.forward; break;
                case 4: normal = Vector3.forward; u = Vector3.right; v = Vector3.up; break;
                default: normal = Vector3.back; u = Vector3.up; v = Vector3.right; break;
            }
            u *= halfSize; v *= halfSize;
            int start = vertices.Count;
            vertices.Add(center - u - v); vertices.Add(center + u - v);
            vertices.Add(center + u + v); vertices.Add(center - u + v);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            byte tone = (byte)Mathf.RoundToInt(Mathf.Lerp(236f, 255f, Hash(cell + face * 211, variant)));
            var color = new Color32(tone, tone, tone, 255);
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        static float Hash(int index, int variant)
        {
            uint value = unchecked((uint)(index * 1664525 + variant * 1013904223 + 982451653));
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return (value & 0x00ffffff) / 16777215f;
        }
    }
}
